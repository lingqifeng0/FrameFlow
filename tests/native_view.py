"""Real EXE regression for fullscreen overlays and message-based Ctrl mouse input.

Uses only this test's process, configuration and pipe; never moves the desktop
cursor or changes global keyboard state. Run after build-portable.ps1.
"""
import ctypes as C
from ctypes import wintypes as W
import json
import math
import msvcrt
import os
from pathlib import Path
import shutil
import subprocess
import time

root = Path(__file__).resolve().parents[1]
exe = Path(os.environ.get('MPVNET_TEST_EXE', root / 'dist/FrameFlow/FrameFlow.exe'))
fixture = root / 'test-results/frame-test.mp4'
second = root / 'test-results/frame-test-view-2.mp4'
shutil.copyfile(fixture, second)
config = root / 'test-results/view-config'
config.mkdir(parents=True, exist_ok=True)
(config / 'mpv.conf').write_text('osc=no\nborder=no\ntitle-bar=no\nkeepaspect-window=no\nkeep-open=yes\n', encoding='utf8')
(config / 'mpvnet.conf').write_text('process-instance=multi\nauto-load-folder=no\n', encoding='utf8')
# Deliberately retain an older Ctrl-wheel seek binding. The new enabled mouse
# feature must handle the event without also executing this or changing volume.
(config / 'input.conf').write_text('MBTN_LEFT cycle pause\nMBTN_LEFT_DBL cycle fullscreen\nWHEEL_UP add volume 1\nWHEEL_DOWN add volume -1\nCtrl+WHEEL_UP seek 7 exact\nCtrl+WHEEL_DOWN seek -7 exact\n', encoding='utf8')
(config / 'modern-ui.json').write_text(json.dumps({'RememberPosition': False, 'RememberSpeed': False, 'AutoFolderPlaylist': False}), encoding='utf8')
pipe_path = rf'\\.\pipe\mpvnet-view-test-{os.getpid()}'
u = C.WinDLL('user32', use_last_error=True)
k = C.WinDLL('kernel32', use_last_error=True)
callback_type = C.WINFUNCTYPE(W.BOOL, W.HWND, W.LPARAM)
u.SetProcessDpiAwarenessContext.argtypes = [W.HANDLE]
u.SetProcessDpiAwarenessContext(C.c_void_p(-4))
u.EnumWindows.argtypes = [callback_type, W.LPARAM]
u.EnumChildWindows.argtypes = [W.HWND, callback_type, W.LPARAM]
u.GetWindowThreadProcessId.argtypes = [W.HWND, C.POINTER(W.DWORD)]
u.GetWindowTextW.argtypes = [W.HWND, W.LPWSTR, C.c_int]
u.GetClassNameW.argtypes = [W.HWND, W.LPWSTR, C.c_int]
u.GetWindowRect.argtypes = [W.HWND, C.POINTER(W.RECT)]
u.GetParent.argtypes = [W.HWND]
u.GetParent.restype = W.HWND
u.IsWindowVisible.argtypes = [W.HWND]
u.SendMessageTimeoutW.argtypes = [W.HWND, W.UINT, W.WPARAM, W.LPARAM, W.UINT, W.UINT, C.POINTER(C.c_size_t)]
k.PeekNamedPipe.argtypes = [W.HANDLE, W.LPVOID, W.DWORD, W.LPVOID, C.POINTER(W.DWORD), W.LPVOID]
reports = []
sequence = 0
process = None
pipe = None


def request(command):
    global sequence
    sequence += 1
    pipe.write((json.dumps({'command': command, 'request_id': sequence}) + '\n').encode())
    deadline = time.monotonic() + 6
    while time.monotonic() < deadline:
        available = W.DWORD()
        if not k.PeekNamedPipe(W.HANDLE(msvcrt.get_osfhandle(pipe.fileno())), None, 0, None, C.byref(available), None):
            raise OSError('Test player disconnected')
        if not available.value:
            time.sleep(.02)
            continue
        reply = json.loads(pipe.readline())
        if reply.get('request_id') != sequence:
            continue
        if reply.get('error') in ('property unavailable', 'property not found') and command[0] == 'get_property':
            return None
        if reply.get('error') != 'success':
            raise RuntimeError(reply)
        return reply.get('data')
    raise TimeoutError(command)


def get(name): return request(['get_property', name])
def snapshot(): return json.loads(get('user-data/modern/window') or '{}')


def wait(predicate, title, timeout=6):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        value = predicate()
        if value:
            return value
        time.sleep(.04)
    raise AssertionError((title, snapshot()))


def check(ok, title, data=None):
    if not ok:
        raise AssertionError((title, data))
    reports.append({'check': title, 'result': 'passed', 'data': data})
    print('PASS:', title, data if data is not None else '', flush=True)


def send(hwnd, message, flags=0, x=20, y=18):
    result = C.c_size_t()
    lp = (x & 0xffff) | ((y & 0xffff) << 16)
    if not u.SendMessageTimeoutW(hwnd, message, flags, lp, 2, 1000, C.byref(result)):
        raise TimeoutError((hwnd, message))


def rect(hwnd):
    value = W.RECT()
    if not u.GetWindowRect(hwnd, C.byref(value)):
        raise OSError('Own test window not found')
    return value


def windows(parent=None):
    result = []

    @callback_type
    def each(hwnd, _):
        pid = W.DWORD()
        u.GetWindowThreadProcessId(hwnd, C.byref(pid))
        if pid.value == process.pid:
            text, cls = C.create_unicode_buffer(500), C.create_unicode_buffer(256)
            u.GetWindowTextW(hwnd, text, len(text))
            u.GetClassNameW(hwnd, cls, len(cls))
            result.append({'hwnd': hwnd, 'text': text.value, 'class': cls.value})
        return True

    if parent is None: u.EnumWindows(each, 0)
    else: u.EnumChildWindows(parent, each, 0)
    return result


def start():
    env = os.environ.copy()
    env['MPVNET_HOME'] = str(config)
    info = subprocess.STARTUPINFO()
    info.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    info.wShowWindow = 0
    proc = subprocess.Popen([str(exe), '--pause=yes', '--mute=yes', '--input-ipc-server=' + pipe_path, str(fixture)], env=env, startupinfo=info)
    deadline = time.monotonic() + 15
    while time.monotonic() < deadline:
        if proc.poll() is not None: raise RuntimeError('Test player exited')
        try: return proc, open(pipe_path, 'r+b', buffering=0)
        except OSError: time.sleep(.1)
    proc.terminate()
    raise TimeoutError('Player startup')


def mode(name):
    request(['script-message-to', 'mpvnet', 'modern-window', name])
    expected = {'restore': 'normal', 'exit-fullscreen': 'normal', 'maximize': 'maximized'}.get(name, name)
    return wait(lambda: (s := snapshot()) and s.get('mode') == expected and s, name)


def reset():
    request(['script-message-to', 'mpvnet', 'modern-tool', 'zoom-reset'])
    wait(lambda: abs(get('video-zoom')) < .0001 and abs(get('video-pan-x')) < .0001 and abs(get('video-pan-y')) < .0001, 'zoom reset')
    time.sleep(.1)


def wheel(target, delta, x, y, control=True):
    r = rect(surface)
    send(target, 0x20a, ((delta & 0xffff) << 16) | (8 if control else 0), r.left + x, r.top + y)


def image_rectangle():
    d = get('osd-dimensions')
    return d['ml'], d['mt'], d['w'] - d['ml'] - d['mr'], d['h'] - d['mt'] - d['mb']


process, pipe = start()
try:
    wait(lambda: get('path') == str(fixture), 'video loaded')
    wait(lambda: bool(snapshot().get('controls')), 'modern controls state')
    form = next(w['hwnd'] for w in windows() if w['class'].startswith('WindowsForms10.Window'))
    children = windows(form)
    native = next(w['hwnd'] for w in children if w['class'] == 'mpv')
    surface = u.GetParent(native)
    play = next(w['hwnd'] for w in children if w['text'] == '播放 / 暂停')
    normal = snapshot()
    check(normal['controls']['footerVisible'] and normal['controls']['headerVisible'], 'windowed controls remain visible')

    mode('fullscreen')
    wait(lambda: get('osd-dimensions')['h'] == snapshot()['video']['height'], 'fullscreen video layout')
    full = snapshot()
    check(full['video']['height'] == full['bounds']['height'], 'video fills full monitor height behind visible control overlay', full)
    send(surface, 0x200, x=full['video']['width']//2, y=full['video']['height']//2)
    wait(lambda: not snapshot()['controls']['footerVisible'], 'auto-hide while paused')
    hidden = snapshot()
    check(hidden['video'] == full['video'] and get('pause') is True, 'paused fullscreen auto-hides without resizing video')
    send(surface, 0x200, x=400, y=full['video']['height']-2)
    wait(lambda: snapshot()['controls']['footerVisible'], 'bottom edge reveals controls')
    check(snapshot()['video'] == full['video'], 'bottom reveal keeps exact video size')
    send(play, 0x200, x=15, y=15)
    time.sleep(2.9)
    check(snapshot()['controls']['footerVisible'], 'hovering a toolbar button prevents auto-hide')

    seek = next(w['hwnd'] for w in children if w['class'].startswith('WindowsForms10.Window')
                and u.GetParent(u.GetParent(u.GetParent(w['hwnd']))) == form
                and (r := rect(w['hwnd'])).bottom-r.top <= 40 and r.right-r.left > 400)
    send(seek, 0x201, 1, x=40, y=12)
    send(surface, 0x200, x=500, y=500)
    time.sleep(2.9)
    check(snapshot()['controls']['footerVisible'], 'seeking capture prevents disappearance even with remote pointer outside toolbar')
    send(seek, 0x202, x=300, y=12)
    wait(lambda: not snapshot()['controls']['footerVisible'], 'auto-hide resumes after releasing seek')

    send(surface, 0x200, x=400, y=full['video']['height']-2)
    wait(lambda: snapshot()['controls']['footerVisible'], 'reveal before speed popup')
    speed = next(w['hwnd'] for w in children if w['text']=='1.00 ×')
    send(speed, 0x201, 1); send(speed, 0x202)
    popup = wait(lambda: next((w['hwnd'] for w in windows() if w['text']=='播放速度'), None), 'speed popup opens')
    send(surface, 0x200, x=500, y=500)
    time.sleep(2.9)
    check(snapshot()['controls']['footerVisible'], 'owned speed popup keeps fullscreen controls available')
    send(popup, 0x10)
    wait(lambda: not snapshot()['controls']['footerVisible'], 'auto-hide resumes after popup closes')

    send(surface, 0x200, x=400, y=2)
    wait(lambda: snapshot()['controls']['headerVisible'], 'top edge reveals title bar')
    check(True, 'fullscreen title bar is reachable')
    mode('exit-fullscreen')
    restored = snapshot()
    check(restored['bounds'] == normal['bounds'] and restored['video'] == normal['video'], 'exit fullscreen restores docked bars and original video layout', restored)
    check(restored['controls']['headerVisible'] and restored['controls']['footerVisible'], 'both windowed bars return')

    send(surface, 0x203, 1, x=300, y=240)
    wait(lambda: get('pause') is False, 'default double click plays')
    send(surface, 0x203, 1, x=300, y=240)
    wait(lambda: get('pause') is True, 'default double click pauses')
    check(get('fullscreen') is False, 'default double click still toggles playback without entering fullscreen')

    request(['set_property', 'pause', True])
    request(['seek', 10, 'absolute+exact'])
    wait(lambda: abs(get('time-pos')-10) < .05, 'test time')
    request(['set_property', 'volume', 40])
    reset()
    d = get('osd-dimensions')
    x, y = int(d['w']*.67), int(d['h']*.43)
    before = image_rectangle()
    anchor = ((x-before[0])/before[2], (y-before[1])/before[3])
    wheel(surface, 120, x, y)
    wait(lambda: abs(get('video-zoom')-.1) < .0001, 'Ctrl-wheel zoom')
    wait(lambda: image_rectangle()[2] > before[2]*1.04, 'rendered zoom')
    after = image_rectangle()
    anchored_error = [abs(x-(after[0]+anchor[0]*after[2])), abs(y-(after[1]+anchor[1]*after[3]))]
    check(max(anchored_error) < 3, 'Ctrl-wheel keeps cursor on the same image detail', anchored_error)
    check(abs(get('time-pos')-10)<.05 and abs(get('volume')-40)<.001 and get('pause') is True,
          'Ctrl-wheel changes neither playback position nor volume nor pause')
    wheel(form, 120, x, y)
    wait(lambda: abs(get('video-zoom')-.2)<.0001, 'top-level Ctrl-wheel zoom')
    check(True, 'top-level Ctrl-wheel is handled exactly once')
    wheel(surface, -120, x, y)
    wait(lambda: abs(get('video-zoom')-.1)<.0001, 'negative wheel')
    check(True, 'signed downward wheel reduces zoom')
    wheel(surface, 60, x, y)
    wait(lambda: abs(get('video-zoom')-.15)<.0001, 'high-resolution wheel')
    check(True, 'half wheel notch supports fine smooth zoom')
    wheel(surface, 120, x, y, control=False)
    wait(lambda: abs(get('volume')-41)<.001, 'ordinary wheel volume')
    check(abs(get('video-zoom')-.15)<.0001, 'ordinary wheel keeps original volume binding and applies only once')

    time.sleep(.15)
    initial_pan = get('video-pan-x'), get('video-pan-y')
    size = image_rectangle()[2:]
    bounds = snapshot()['bounds']
    send(surface, 0x201, 9, x=x, y=y)
    send(surface, 0x200, 9, x=x+120, y=y-40)
    wait(lambda: abs(get('video-pan-x')-initial_pan[0]-120/size[0]) < .003, 'Ctrl drag pan')
    send(surface, 0x202, 8, x=x+120, y=y-40)
    check(abs(get('video-pan-y')-initial_pan[1]+40/size[1])<.003 and snapshot()['bounds']==bounds and get('pause') is True,
          'Ctrl drag pans horizontally and vertically without moving the window or toggling pause')
    reset()
    check(True, 'shared reset restores zoom and both pan axes')
    wheel(surface, 120, x, y)
    wait(lambda: get('video-zoom')>.09, 'zoom before shortcut')
    request(['keypress', 'Ctrl+0'])
    wait(lambda: abs(get('video-zoom'))<.0001 and abs(get('video-pan-x'))<.0001, 'Ctrl-0 reset shortcut')
    check(True, 'default Ctrl-0 shortcut invokes shared reset')

    mode('maximize')
    reset()
    wheel(surface, 120, x, y)
    wait(lambda: abs(get('video-zoom')-.1)<.0001, 'maximized zoom')
    check(snapshot()['mode']=='maximized', 'zoom in maximized mode does not restore the window')
    mode('restore')
    mode('fullscreen')
    reset()
    wait(lambda: get('osd-dimensions')['h']==snapshot()['video']['height'], 'full view dimensions')
    d = get('osd-dimensions')
    send(surface, 0x200, x=400, y=d['h']-2)
    wait(lambda: snapshot()['controls']['footerVisible'], 'toolbar visible for wheel boundary check')
    wheel(surface, 120, 400, d['h']-10)
    time.sleep(.1)
    check(abs(get('video-zoom'))<.0001, 'Ctrl-wheel over the visible overlay does not zoom the video behind it')
    fx, fy = int(d['w']*.72), int(d['h']*.34)
    wheel(surface, 120, fx, fy)
    wait(lambda: abs(get('video-zoom')-.1)<.0001, 'fullscreen zoom')
    check(snapshot()['mode']=='fullscreen', 'Ctrl-wheel also works in fullscreen')
    wheel(surface, 12000, fx, fy)
    wait(lambda: abs(get('video-zoom')-4)<.0001, 'maximum zoom')
    wheel(surface, -12000, fx, fy)
    wait(lambda: abs(get('video-zoom')+2)<.0001, 'minimum zoom')
    check(True, 'wheel zoom clamps safely to 25 percent through 1600 percent')
    request(['loadfile', str(second), 'replace'])
    wait(lambda: get('path')==str(second) and abs(get('video-zoom'))<.0001 and abs(get('video-pan-x'))<.0001 and abs(get('video-pan-y'))<.0001, 'new video resets view')
    check(True, 'switching videos clears previous mouse zoom and pan')
    request(['quit']); pipe.close(); pipe = None; process.wait(timeout=15)

    saved = json.loads((config/'modern-ui.json').read_text(encoding='utf8'))
    saved.update(AutoHideFullscreenControls=False, CtrlMouseVideoView=False)
    (config/'modern-ui.json').write_text(json.dumps(saved), encoding='utf8')
    process, pipe = start()
    wait(lambda: get('path')==str(fixture), 'restarted video')
    form = next(w['hwnd'] for w in windows() if w['class'].startswith('WindowsForms10.Window'))
    native = next(w['hwnd'] for w in windows(form) if w['class']=='mpv')
    surface = u.GetParent(native)
    mode('fullscreen')
    full = snapshot()
    send(surface, 0x200, x=full['video']['width']//2, y=full['video']['height']//2)
    time.sleep(2.9)
    check(snapshot()['controls']['footerVisible'] and not snapshot()['controls']['autoHideFullscreen'], 'disabled auto-hide keeps fullscreen controls visible after restart')
    wheel(surface, 120, 400, 400)
    time.sleep(.15)
    check(abs(get('video-zoom'))<.0001, 'disabled Ctrl mouse feature does not intercept wheel for zoom')
    request(['set_property','pause',True]); request(['seek',10,'absolute+exact'])
    wait(lambda: abs(get('time-pos')-10)<.05, 'custom seek start')
    request(['keypress','Ctrl+WHEEL_UP'])
    wait(lambda: abs(get('time-pos')-17)<.05, 'retained custom binding')
    check(True, 'original custom Ctrl-wheel binding remains available')
    request(['quit']); process.wait(timeout=15)
    (root/'test-results/native-view.json').write_text(json.dumps(reports, ensure_ascii=False, indent=2), encoding='utf8')
    print(f'All {len(reports)} checks passed.', flush=True)
finally:
    if pipe is not None:
        try: request(['quit'])
        except Exception: pass
        pipe.close()
    if process is not None and process.poll() is None:
        try: process.wait(timeout=10)
        except subprocess.TimeoutExpired: process.terminate(); process.wait(timeout=5)
