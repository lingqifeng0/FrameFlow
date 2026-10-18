"""Helpers restricted to the native test player's process, windows and pipe."""
import ctypes as C
from ctypes import wintypes as W
import json
import msvcrt
import os
from pathlib import Path
import subprocess
import time

ROOT = Path(__file__).resolve().parents[1]
EXE = Path(os.environ.get('MPVNET_TEST_EXE', ROOT / 'dist/FrameFlow/FrameFlow.exe'))
FIXTURE = ROOT / 'test-results/frame-test.mp4'
USER32 = C.WinDLL('user32', use_last_error=True)
KERNEL32 = C.WinDLL('kernel32', use_last_error=True)
CALLBACK = C.WINFUNCTYPE(W.BOOL, W.HWND, W.LPARAM)
USER32.SetProcessDpiAwarenessContext.argtypes = [W.HANDLE]
USER32.SetProcessDpiAwarenessContext(C.c_void_p(-4))
USER32.EnumWindows.argtypes = [CALLBACK, W.LPARAM]
USER32.EnumChildWindows.argtypes = [W.HWND, CALLBACK, W.LPARAM]
USER32.GetWindowThreadProcessId.argtypes = [W.HWND, C.POINTER(W.DWORD)]
USER32.GetWindowTextW.argtypes = [W.HWND, W.LPWSTR, C.c_int]
USER32.GetClassNameW.argtypes = [W.HWND, W.LPWSTR, C.c_int]
USER32.GetWindowRect.argtypes = [W.HWND, C.POINTER(W.RECT)]
USER32.GetParent.argtypes = [W.HWND]
USER32.GetParent.restype = W.HWND
USER32.SendMessageTimeoutW.argtypes = [W.HWND, W.UINT, W.WPARAM, W.LPARAM, W.UINT, W.UINT, C.POINTER(C.c_size_t)]
USER32.PostMessageW.argtypes = [W.HWND, W.UINT, W.WPARAM, W.LPARAM]
KERNEL32.PeekNamedPipe.argtypes = [W.HANDLE, W.LPVOID, W.DWORD, W.LPVOID, C.POINTER(W.DWORD), W.LPVOID]


def wait(predicate, title, timeout=8):
    end = time.monotonic() + timeout
    while time.monotonic() < end:
        result = predicate()
        if result: return result
        time.sleep(.025)
    raise AssertionError(title)


class PlayerTest:
    def __init__(self, name, settings=None, config_text='', fixture=None):
        self.config = ROOT / 'test-results' / (name + '-config-' + str(os.getpid()))
        self.config.mkdir(parents=True, exist_ok=True)
        self.pipe_name = rf'\\.\pipe\mpvnet-{name}-{os.getpid()}'
        self.sequence = 0
        self.pipe = self.process = None
        self.reports = []
        self.fixture = Path(fixture) if fixture else FIXTURE
        (self.config / 'mpv.conf').write_text('osc=no\nborder=no\ntitle-bar=no\nkeepaspect-window=no\nkeep-open=yes\nautocreate-playlist=no\n' + config_text, encoding='utf8')
        (self.config / 'mpvnet.conf').write_text('process-instance=multi\nauto-load-folder=no\n', encoding='utf8')
        (self.config / 'modern-ui.json').write_text(json.dumps({'Speed': 1, 'RememberSpeed': False, 'RememberPosition': False, 'AutoFolderPlaylist': False, **(settings or {})}), encoding='utf8')

    def start(self):
        env = os.environ.copy()
        env['MPVNET_HOME'] = str(self.config)
        startup = subprocess.STARTUPINFO()
        startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        startup.wShowWindow = 0
        self.process = subprocess.Popen([str(EXE), '--pause=yes', '--ao=null', '--input-ipc-server=' + self.pipe_name,
                                         str(self.fixture)], env=env, startupinfo=startup)
        end = time.monotonic() + 15
        while time.monotonic() < end:
            if self.process.poll() is not None: raise RuntimeError('Test player exited')
            try:
                self.pipe = open(self.pipe_name, 'r+b', buffering=0)
                wait(lambda: self.get('path') == str(self.fixture), 'test media loaded')
                wait(lambda: self.state('state').get('path') == str(self.fixture), 'modern state ready')
                return self
            except OSError: time.sleep(.1)
        raise TimeoutError('Player startup')

    def request(self, command):
        self.sequence += 1
        self.pipe.write((json.dumps({'command': command, 'request_id': self.sequence}) + '\n').encode())
        end = time.monotonic() + 6
        while time.monotonic() < end:
            available = W.DWORD()
            if not KERNEL32.PeekNamedPipe(W.HANDLE(msvcrt.get_osfhandle(self.pipe.fileno())), None, 0, None, C.byref(available), None):
                raise OSError('Test player disconnected')
            if not available.value:
                time.sleep(.005)
                continue
            reply = json.loads(self.pipe.readline())
            if reply.get('request_id') != self.sequence: continue
            if reply.get('error') in ('property unavailable', 'property not found'): return None
            if reply.get('error') != 'success': raise RuntimeError(reply)
            return reply.get('data')
        raise TimeoutError(command)

    def get(self, name): return self.request(['get_property', name])
    def state(self, name): return json.loads(self.get('user-data/modern/' + name) or '{}')
    def tool(self, name, *args): return self.request(['script-message-to', 'mpvnet', 'modern-tool', name, *map(str, args)])

    def windows(self, parent=None):
        result = []

        @CALLBACK
        def each(hwnd, _):
            pid = W.DWORD()
            USER32.GetWindowThreadProcessId(hwnd, C.byref(pid))
            if pid.value == self.process.pid:
                text, cls = C.create_unicode_buffer(500), C.create_unicode_buffer(256)
                USER32.GetWindowTextW(hwnd, text, len(text))
                USER32.GetClassNameW(hwnd, cls, len(cls))
                result.append({'hwnd': hwnd, 'text': text.value, 'class': cls.value})
            return True

        if parent is None: USER32.EnumWindows(each, 0)
        else: USER32.EnumChildWindows(parent, each, 0)
        return result

    def main(self): return wait(lambda: next((w['hwnd'] for w in self.windows() if w['class'].startswith('WindowsForms10.Window')), None), 'main window')

    @staticmethod
    def send(hwnd, msg, wp=0, lp=0):
        result = C.c_size_t()
        if not USER32.SendMessageTimeoutW(hwnd, msg, wp, lp, 2, 3000, C.byref(result)): raise TimeoutError((hwnd, msg))
        return result.value

    @staticmethod
    def rect(hwnd):
        result = W.RECT()
        if not USER32.GetWindowRect(hwnd, C.byref(result)): raise OSError('Test window missing')
        return result

    def check(self, passed, title, data=None):
        if not passed: raise AssertionError((title, data))
        self.reports.append({'check': title, 'result': 'passed', 'data': data})
        print('PASS:', title, data if data is not None else '', flush=True)

    def save(self, name):
        (ROOT / 'test-results' / name).write_text(json.dumps(self.reports, ensure_ascii=False, indent=2), encoding='utf8')

    def close(self):
        try:
            if self.process and self.process.poll() is None:
                for window in self.windows():
                    if window['text'] == '设置': self.send(window['hwnd'], 0x10)
            if self.process and self.process.poll() is None and self.pipe: self.request(['quit'])
            if self.pipe: self.pipe.close()
            if self.process: self.process.wait(timeout=15)
        finally:
            if self.process and self.process.poll() is None:
                self.process.terminate()
                self.process.wait(timeout=10)
