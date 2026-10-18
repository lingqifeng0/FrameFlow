"""Regression against the packaged EXE using message-based remote mouse input.

Uses an isolated configuration and IPC pipe, and never moves the desktop cursor.
Run after tests/create_fixture.py and scripts/build-portable.ps1.
"""
import ctypes as C
from ctypes import wintypes as W
import json
import msvcrt
import os
from pathlib import Path
import subprocess
import time

root = Path(__file__).resolve().parents[1]
exe = root / 'dist/FrameFlow/FrameFlow.exe'
fixture = root / 'test-results/frame-test.mp4'
config = root / 'test-results/native-input-config'
config.mkdir(parents=True, exist_ok=True)
(config / 'mpv.conf').write_text('osc=no\nborder=no\ntitle-bar=no\nkeepaspect-window=no\nkeep-open=yes\n', encoding='utf8')
(config / 'mpvnet.conf').write_text('process-instance=multi\nauto-load-folder=no\n', encoding='utf8')
(config / 'modern-ui.json').write_text(json.dumps({'RememberPosition': False, 'RememberSpeed': False, 'VideoDoubleClickAction': 'input-conf', 'AutoFolderPlaylist': False}), encoding='utf8')
(config / 'input.conf').write_text('MBTN_LEFT cycle pause\nMBTN_MID cycle mute\nMBTN_LEFT_DBL cycle fullscreen\nWHEEL_UP add volume 1\nWHEEL_DOWN add volume -1\nSPACE cycle pause\n', encoding='utf8')
pipepath = rf'\\.\pipe\mpvnet-input-test-{os.getpid()}'
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
u.SendMessageTimeoutW.argtypes = [W.HWND, W.UINT, W.WPARAM, W.LPARAM, W.UINT, W.UINT, C.POINTER(C.c_size_t)]
u.SendMessageTimeoutW.restype = W.LPARAM
k.PeekNamedPipe.argtypes = [W.HANDLE, W.LPVOID, W.DWORD, W.LPVOID, C.POINTER(W.DWORD), W.LPVOID]
reports = []
counter = 0
pipe = None


def request(command):
    global counter
    counter += 1
    pipe.write((json.dumps({'command': command, 'request_id': counter}) + '\n').encode())
    deadline = time.monotonic() + 5
    while time.monotonic() < deadline:
        available = W.DWORD()
        if not k.PeekNamedPipe(W.HANDLE(msvcrt.get_osfhandle(pipe.fileno())), None, 0, None, C.byref(available), None):
            raise OSError('Player pipe disconnected')
        if not available.value:
            time.sleep(.02)
            continue
        reply = json.loads(pipe.readline())
        if reply.get('request_id') == counter:
            if reply.get('error') == 'property unavailable' and command[0] == 'get_property':
                return None
            if reply.get('error') != 'success':
                raise RuntimeError(reply)
            return reply.get('data')
    raise TimeoutError('Player IPC response')


def get(name):
    return request(['get_property', name])


def wait(name, predicate):
    deadline = time.monotonic() + 5
    while time.monotonic() < deadline:
        value = get(name)
        if predicate(value):
            return value
        time.sleep(.04)
    raise AssertionError((name, value))


def check(ok, name, data=None):
    if not ok:
        raise AssertionError((name, data))
    reports.append({'check': name, 'result': 'passed', 'data': data})
    print('PASS:', name, data if data is not None else '', flush=True)


def send(hwnd, message, wp=0, x=20, y=18, lp=None):
    result = C.c_size_t()
    if lp is None:
        lp = (x & 0xffff) | ((y & 0xffff) << 16)
    if not u.SendMessageTimeoutW(hwnd, message, wp, lp, 2, 1000, C.byref(result)):
        raise TimeoutError(f'Window {hwnd}: message {message:x}')
    return result.value


def rectangle(hwnd):
    rect = W.RECT()
    if not u.GetWindowRect(hwnd, C.byref(rect)):
        raise OSError('Test window not found')
    return rect


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

    if parent is None:
        u.EnumWindows(each, 0)
    else:
        u.EnumChildWindows(parent, each, 0)
    return result


env = os.environ.copy()
env['MPVNET_HOME'] = str(config)
info = subprocess.STARTUPINFO()
info.dwFlags |= subprocess.STARTF_USESHOWWINDOW
info.wShowWindow = 0
process = subprocess.Popen([str(exe), '--pause=yes', '--mute=yes', '--input-ipc-server=' + pipepath, str(fixture)], env=env, startupinfo=info)
try:
    deadline = time.monotonic() + 15
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise RuntimeError('Player exited at startup')
        try:
            pipe = open(pipepath, 'r+b', buffering=0)
            break
        except OSError:
            time.sleep(.1)
    if pipe is None:
        raise TimeoutError('Player startup pipe')
    wait('path', lambda value: value and Path(value) == fixture)
    time.sleep(.4)
    form = next(w['hwnd'] for w in windows() if w['class'].startswith('WindowsForms10.Window'))
    children = windows(form)
    native = next(w['hwnd'] for w in children if w['class'] == 'mpv')
    surface = u.GetParent(native)
    play = next(w['hwnd'] for w in children if w['text'] == '播放 / 暂停')
    check(get('cursor-autohide') == 0, '旧配置升级后默认保持光标可见', get('cursor-autohide'))
    for target in (form, surface, play):
        activation = send(target, 0x21, form, lp=(0x201 << 16) | 1)
        check(activation == 1, '鼠标激活保留第一下点击', {'window': target, 'result': activation})

    request(['set_property', 'pause', True])
    send(play, 0x200); send(play, 0x201, 1); send(play, 0x202)
    wait('pause', lambda value: value is False)
    time.sleep(.15)
    check(get('pause') is False, '底部播放按钮点击生效且不重复')
    send(play, 0x201, 1); send(play, 0x202)
    wait('pause', lambda value: value is True)
    check(get('pause') is True, '底部暂停按钮再次点击生效')

    send(surface, 0x200, x=280, y=240)
    position = wait('mouse-pos', lambda value: value['x'] == 280 and value['y'] == 240)
    check(True, '视频鼠标坐标来自消息，支持远程指针', position)
    sr, fr = rectangle(surface), rectangle(form)
    send(form, 0x200, x=280 + sr.left - fr.left, y=240 + sr.top - fr.top)
    check(get('mouse-pos')['x'] == 280 and get('mouse-pos')['y'] == 240, '顶层坐标正确换算为视频坐标')
    send(surface, 0x200, x=-5, y=-7)
    position = wait('mouse-pos', lambda value: value['x'] == -5 and value['y'] == -7)
    check(True, '捕获期间负坐标正确解析', position)
    send(surface, 0x200, x=280, y=240)
    send(surface, 0x201, 1, 280, 240); send(surface, 0x202, 0, 280, 240)
    wait('pause', lambda value: value is False)
    check(True, '视频区域左键传到 mpv 输入绑定')
    request(['set_property', 'pause', True])
    request(['set_property', 'mute', True])
    send(surface, 0x207, 0x10, 280, 240); send(surface, 0x208, 0, 280, 240)
    wait('mute', lambda value: value is False)
    check(True, '视频区域中键传到 mpv 输入绑定')

    request(['set_property', 'volume', 40])
    send(surface, 0x20a, 120 << 16, sr.left + 280, sr.top + 240)
    wait('volume', lambda value: value > 40)
    time.sleep(.15)
    check(abs(get('volume') - 41) < .001, '滚轮只执行一步，不由父窗口重复转发', get('volume'))

    send(surface, 0x100, 0x20, lp=1)
    send(surface, 0x101, 0x20, lp=0xc0000001)
    wait('pause', lambda value: value is False)
    check(True, '视频获得焦点后空格快捷键生效')
    request(['set_property', 'pause', True])
    request(['seek', 0, 'absolute+exact'])
    wait('time-pos', lambda value: abs(value) < .05)
    seek = next(w['hwnd'] for w in children
                if w['class'].startswith('WindowsForms10.Window')
                and u.GetParent(u.GetParent(u.GetParent(w['hwnd']))) == form
                and (r := rectangle(w['hwnd'])).bottom - r.top <= 35 and r.right - r.left > 400)
    r = rectangle(seek)
    width = r.right - r.left
    final_x = round((width - 12) * .75 + 6)
    send(seek, 0x201, 1, 40, 12)
    send(seek, 0x200, 1, 120, 12)
    send(seek, 0x202, 0, final_x, 12)
    expected = (final_x - 6) / (width - 12) * get('duration')
    timepos = wait('time-pos', lambda value: abs(value - expected) < .07)
    check(True, '进度条松开时跳到最后位置', {'expected': expected, 'actual': timepos})

    send(surface, 0x203, 1, 280, 240)
    wait('fullscreen', lambda value: value is True)
    check(True, '选择跟随快捷键配置后，视频双击使用自定义全屏绑定')
    request(['set_property', 'fullscreen', False])
    wait('fullscreen', lambda value: value is False)
    (root / 'test-results/native-input.json').write_text(json.dumps(reports, ensure_ascii=False, indent=2), encoding='utf8')
finally:
    if pipe is not None:
        try:
            request(['quit'])
        except Exception:
            pass
        pipe.close()
    try:
        process.wait(timeout=15)
    except subprocess.TimeoutExpired:
        process.terminate()
        process.wait(timeout=5)
