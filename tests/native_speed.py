"""Exercise the actual speed popup, restart persistence and rendered high-speed audio.

Uses this test's windows, pipe and config only. PCM audio output is written to
test-results instead of speakers; no desktop mouse or keyboard state is changed.
"""
import ctypes as C
from ctypes import wintypes as W
import json
import msvcrt
import os
from pathlib import Path
import struct
import subprocess
import time
import wave

import numpy as np

root = Path(__file__).resolve().parents[1]
exe = Path(os.environ.get('MPVNET_TEST_EXE', root / 'dist/FrameFlow/FrameFlow.exe'))
output = root / 'test-results'
fixture = output / 'frame-test.mp4'
base = output / 'speed-config'
pipe_path = rf'\\.\pipe\mpvnet-speed-test-{os.getpid()}'
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
u.GetParent.argtypes = [W.HWND]
u.GetParent.restype = W.HWND
u.GetWindowRect.argtypes = [W.HWND, C.POINTER(W.RECT)]
u.GetDlgCtrlID.argtypes = [W.HWND]
u.SendMessageTimeoutW.argtypes = [W.HWND, W.UINT, W.WPARAM, W.LPARAM, W.UINT, W.UINT, C.POINTER(C.c_size_t)]
k.PeekNamedPipe.argtypes = [W.HANDLE, W.LPVOID, W.DWORD, W.LPVOID, C.POINTER(W.DWORD), W.LPVOID]
reports = []
sequence = 0
process = None
pipe = None


def check(ok, title, data=None):
    if not ok: raise AssertionError((title, data))
    reports.append({'check': title, 'result': 'passed', 'data': data})
    print('PASS:', title, data if data is not None else '', flush=True)


def wait(predicate, title, timeout=8):
    end = time.monotonic() + timeout
    while time.monotonic() < end:
        result = predicate()
        if result: return result
        time.sleep(.04)
    raise AssertionError(title)


def request(command):
    global sequence
    sequence += 1
    pipe.write((json.dumps({'command': command, 'request_id': sequence}) + '\n').encode())
    end = time.monotonic() + 6
    while time.monotonic() < end:
        available = W.DWORD()
        if not k.PeekNamedPipe(W.HANDLE(msvcrt.get_osfhandle(pipe.fileno())), None, 0, None, C.byref(available), None):
            raise OSError('Test player disconnected')
        if not available.value:
            time.sleep(.02)
            continue
        reply = json.loads(pipe.readline())
        if reply.get('request_id') != sequence: continue
        if reply.get('error') in ('property unavailable', 'property not found'): return None
        if reply.get('error') != 'success': raise RuntimeError(reply)
        return reply.get('data')
    raise TimeoutError(command)


def get(name): return request(['get_property', name])
def send(hwnd, msg, wp=0, lp=0):
    result = C.c_size_t()
    if not u.SendMessageTimeoutW(hwnd, msg, wp, lp, 2, 2000, C.byref(result)):
        raise TimeoutError((hwnd, msg))
    return result.value


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


def rect(hwnd):
    r = W.RECT()
    if not u.GetWindowRect(hwnd, C.byref(r)): raise OSError('Own test control not found')
    return r


def prepare(config, speed=12.34, filters=''):
    config.mkdir(parents=True, exist_ok=True)
    (config / 'mpv.conf').write_text('osc=no\nborder=no\ntitle-bar=no\nkeepaspect-window=no\nkeep-open=yes\nautocreate-playlist=no\n' +
                                     ('af=' + filters + '\n' if filters else ''), encoding='utf8')
    (config / 'mpvnet.conf').write_text('process-instance=multi\nauto-load-folder=no\n', encoding='utf8')
    (config / 'modern-ui.json').write_text(json.dumps({'Speed': speed, 'SpeedStep': .05, 'RememberSpeed': True,
                                                     'RememberPosition': False, 'AutoFolderPlaylist': False}), encoding='utf8')


def start(config, media=fixture, extra=()):
    global process, pipe
    env = os.environ.copy()
    env['MPVNET_HOME'] = str(config)
    info = subprocess.STARTUPINFO()
    info.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    info.wShowWindow = 0
    process = subprocess.Popen([str(exe), '--pause=yes', '--ao=null', '--input-ipc-server=' + pipe_path,
                                *extra, str(media)], env=env, startupinfo=info)
    end = time.monotonic() + 15
    while time.monotonic() < end:
        if process.poll() is not None: raise RuntimeError('Test player exited')
        try:
            pipe = open(pipe_path, 'r+b', buffering=0)
            wait(lambda: get('path') == str(media), 'test media loaded')
            wait(lambda: (s := get('user-data/modern/state')) and json.loads(s).get('path') == str(media), 'modern state ready')
            return
        except OSError: time.sleep(.1)
    process.terminate()
    process.wait(timeout=10)
    raise TimeoutError('Player startup')


def stop():
    global pipe
    request(['quit'])
    pipe.close()
    pipe = None
    process.wait(timeout=15)


def speed_is(value): return abs(get('speed') - value) < .0001
def set_tool(value):
    request(['script-message-to', 'mpvnet', 'modern-tool', 'speed', str(value)])
    wait(lambda: speed_is(value), 'toolbar speed ' + str(value))


def managed(): return [f for f in get('af') if f.get('label') == 'FrameFlow-speed']
def open_popup():
    form = next(w['hwnd'] for w in windows() if w['class'].startswith('WindowsForms10.Window'))
    button = next(w['hwnd'] for w in windows(form) if w['text'] == f"{get('speed'):.2f} ×")
    point = (18 << 16) | 20
    send(button, 0x201, 1, point)
    send(button, 0x202, 0, point)
    return wait(lambda: next((w['hwnd'] for w in windows() if w['text'] == '播放速度'), None), 'speed popup opens')


def pcm_info(path):
    data = path.read_bytes()
    assert data[:4] == b'RIFF' and data[8:12] == b'WAVE'
    pos, fmt, payload = 12, None, None
    while pos + 8 <= len(data):
        tag, size = data[pos:pos + 4], struct.unpack_from('<I', data, pos + 4)[0]
        chunk = data[pos + 8:pos + 8 + size]
        if tag == b'fmt ': fmt = chunk
        if tag == b'data': payload = chunk
        pos += 8 + size + (size % 2)
    assert fmt and payload
    kind, channels, samplerate, _, align, bits = struct.unpack_from('<HHIIHH', fmt)
    if kind == 65534: kind = struct.unpack_from('<H', fmt, 24)[0]
    if kind == 3 and bits == 32: samples = np.frombuffer(payload, dtype='<f4').astype(np.float64)
    elif kind == 1 and bits in (16, 32):
        samples = np.frombuffer(payload, dtype=f'<i{bits // 8}').astype(np.float64) / (2 ** (bits - 1))
    else: raise AssertionError(('Unexpected PCM format', kind, bits))
    return {'duration': len(payload) / align / samplerate, 'rms': float(np.sqrt(np.mean(samples ** 2))),
            'channels': channels, 'samplerate': samplerate, 'finite': bool(np.isfinite(samples).all())}


prepare(base)
try:
    start(base)
    check(speed_is(12.34), 'startup restores 12.34x instead of clamping to 4x')
    check(len(managed()) == 1 and float(managed()[0]['params']['max-speed']) == 16,
          'one native tempo filter supports audio through 16x', get('af'))
    tools = json.loads(get('user-data/modern/tools'))
    param = next(t['parameter'] for t in tools if t['id'] == 'speed')
    check(param['Minimum'] == .25 and param['Maximum'] == 16, 'external toolbar catalog exposes the full range')

    popup = open_popup()
    def ready_controls():
        controls = windows(popup)
        return controls if any('combobox' in w['class'].lower() for w in controls) else None
    controls = wait(ready_controls, 'all speed popup controls created')
    slider = next(w['hwnd'] for w in controls if 'msctls_trackbar32' in w['class'])
    check(send(slider, 0x401) == 25 and send(slider, 0x402) == 1600, 'actual slider spans 0.25 to 16.00x')
    presets = {w['text']: w['hwnd'] for w in controls if w['text'] in ('4×', '8×', '12×', '16×')}
    check(len(presets) == 4, '4x/8x/12x/16x preset buttons are present')
    preset_rect = rect(presets['16×'])
    step = next(w['hwnd'] for w in controls if 'combobox' in w['class'].lower())
    popup_rect, step_rect = rect(popup), rect(step)
    check(preset_rect.bottom <= step_rect.top and preset_rect.right <= popup_rect.right,
          'second preset row fits above the step selector')
    for text, value in [('4×', 4), ('8×', 8), ('12×', 12), ('16×', 16)]:
        send(presets[text], 0xF5)
        wait(lambda: speed_is(value), text + ' preset applies')
        check(True, text + ' preset changes the actual player')

    edit = next(w['hwnd'] for w in controls if '.edit.' in w['class'].lower())
    text = C.create_unicode_buffer('12.34')
    send(edit, 0xC, 0, C.addressof(text))
    send(edit, 0x100, 13)
    send(edit, 0x101, 13)
    wait(lambda: speed_is(12.34), 'numeric field accepts 12.34')
    check(True, 'direct numeric input keeps 0.01 precision above 4x')
    send(slider, 0x405, 1, 1234)
    send(u.GetParent(slider), 0x114, (1234 << 16) | 5, slider)
    wait(lambda: speed_is(12.34), 'slider changes applied')
    check(send(slider, 0x400) == 1234, 'slider synchronizes to two-decimal numeric input')
    send(step, 0x14E, 0)
    send(u.GetParent(step), 0x111, (1 << 16) | (u.GetDlgCtrlID(step) & 0xffff), step)
    request(['script-message-to', 'mpvnet', 'modern-tool', 'speed-up'])
    wait(lambda: speed_is(12.35), '0.01 increment at high speed')
    check(True, '0.01 step applies to high-speed toolbar controls')
    request(['script-message-to', 'mpvnet', 'modern-tool', 'speed-down'])
    wait(lambda: speed_is(12.34), '0.01 decrement at high speed')
    check(True, '0.01 decrement returns to 12.34x')
    send(popup, 0x10)
    set_tool(16)
    popup = open_popup()
    check(any(w['text'] == '16.00' for w in windows(popup)), 'popup reopens safely at the 16x upper bound')
    send(popup, 0x10)
    request(['script-message-to', 'mpvnet', 'modern-tool', 'speed-up'])
    time.sleep(.15)
    check(speed_is(16), 'speed-up stops at 16x')
    request(['script-message-to', 'mpvnet', 'modern-tool', 'speed-reset'])
    wait(lambda: speed_is(1), 'reset to 1x')
    check(True, 'reset button restores 1.00x')
    set_tool(12.34)
    stop()
    saved = json.loads((base / 'modern-ui.json').read_text(encoding='utf8'))
    check(saved['Speed'] == 12.34 and saved['SpeedStep'] == .01, 'exit saves high speed and precision step')
    start(base)
    check(speed_is(12.34), '12.34x survives an actual EXE restart')
    check(len(managed()) == 1, 'restart does not duplicate the native tempo filter')
    request(['set_property', 'speed', 12.56])
    wait(lambda: any(w['text'] == '12.56 ×' for w in windows(next(w['hwnd'] for w in windows()
          if w['class'].startswith('WindowsForms10.Window')))), 'external speed reflected in control bar')
    check(True, 'external 12.56x updates the displayed rate')
    request(['af', 'add', '@test-gain:lavfi=[volume=0.8]'])
    request(['set_property', 'audio-pitch-correction', False])
    wait(lambda: not managed(), 'pitch correction disabled')
    check(any(f.get('label') == 'test-gain' for f in get('af')), 'disabling pitch correction keeps the custom audio filter')
    request(['set_property', 'audio-pitch-correction', True])
    wait(lambda: bool(managed()), 'pitch correction reenabled')
    check(len(managed()) == 1 and any(f.get('label') == 'test-gain' for f in get('af')),
          'reenabling pitch correction preserves the custom audio filter')
    stop()
    saved = json.loads((base / 'modern-ui.json').read_text(encoding='utf8'))
    check(saved['Speed'] == 12.56, 'high speed set through IPC is also remembered')

    explicit = output / 'speed-explicit-config'
    prepare(explicit, speed=1, filters='scaletempo2=max-speed=10')
    start(explicit)
    check(not managed() and len(get('af')) == 1 and float(get('af')[0]['params']['max-speed']) == 10,
          'explicit advanced tempo parameters are preserved')
    stop()

    tone = output / 'speed-tone.wav'
    t = np.arange(12 * 48000) / 48000
    mono = (np.sin(2 * np.pi * 440 * t) * 8000).astype('<i2')
    stereo = np.column_stack([mono, mono]).tobytes()
    with wave.open(str(tone), 'wb') as writer:
        writer.setnchannels(2); writer.setsampwidth(2); writer.setframerate(48000)
        writer.writeframes(stereo)
    for rate in (12, 16):
        config = output / f'speed-audio-{rate}-config'
        prepare(config, speed=rate)
        rendered = output / f'speed-audio-{rate}.wav'
        start(config, tone, ('--ao=pcm', '--ao-pcm-file=' + str(rendered), '--ao-pcm-waveheader=yes'))
        check(speed_is(rate), f'audio renders at an actual {rate}x speed')
        request(['set_property', 'pause', False])
        try:
            wait(lambda: get('eof-reached') is True or get('idle-active') is True, 'audio playback finished', timeout=12)
        except AssertionError:
            print('AUDIO STATE:', {p: get(p) for p in ('time-pos', 'duration', 'speed', 'pause', 'eof-reached',
                  'idle-active', 'ao', 'audio-params', 'af', 'audio-speed-correction', 'video-speed-correction')}, flush=True)
            raise
        stop()
        info = pcm_info(rendered)
        check(info['finite'] and info['rms'] > .01, f'{rate}x PCM output contains sound instead of automatic silence', info)
        check(abs(info['duration'] - 12 / rate) < .25, f'{rate}x audio duration follows the selected speed', info['duration'])
    (output / 'native-speed.json').write_text(json.dumps(reports, ensure_ascii=False, indent=2), encoding='utf8')
    print(f'All {len(reports)} checks passed.', flush=True)
finally:
    if pipe:
        pipe.close()
    if process and process.poll() is None:
        process.terminate()
        process.wait(timeout=10)
