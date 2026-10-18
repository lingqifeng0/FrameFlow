"""JSON IPC tests; never inject input or automate another application."""
import ctypes as C
from ctypes import wintypes as W
import json
import msvcrt
import os
from pathlib import Path
import subprocess
import shutil
import time

ROOT = Path(__file__).resolve().parents[1]
EXE = Path(os.environ.get('MPVNET_TEST_EXE', ROOT / 'dist/FrameFlow/FrameFlow.exe'))
KERNEL32 = C.WinDLL('kernel32', use_last_error=True)
KERNEL32.PeekNamedPipe.argtypes = [W.HANDLE, W.LPVOID, W.DWORD, W.LPVOID, C.POINTER(W.DWORD), W.LPVOID]


class PipeClient:
    def __init__(self, endpoint, timeout=15):
        self.sequence = 0
        self.events = []
        self.started = time.monotonic()
        until = self.started + timeout
        while True:
            try:
                self.pipe = open(endpoint, 'r+b', buffering=0)
                break
            except OSError:
                if time.monotonic() >= until: raise
                time.sleep(.025)

    def request(self, command, timeout=8):
        self.sequence += 1
        self.pipe.write((json.dumps({'command': command, 'request_id': self.sequence}) + '\n').encode())
        until = time.monotonic() + timeout
        while time.monotonic() < until:
            available = W.DWORD()
            if not KERNEL32.PeekNamedPipe(W.HANDLE(msvcrt.get_osfhandle(self.pipe.fileno())), None, 0, None, C.byref(available), None):
                raise OSError('Player pipe closed')
            if not available.value:
                time.sleep(.002)
                continue
            reply = json.loads(self.pipe.readline())
            if reply.get('request_id') != self.sequence:
                self.events.append({'ms': round(1000*(time.monotonic()-self.started)), **reply})
                continue
            if reply.get('error') in ('property unavailable', 'property not found'): return None
            if reply.get('error') != 'success': raise RuntimeError(reply)
            return reply.get('data')
        raise TimeoutError(command)

    def get(self, name): return self.request(['get_property', name])
    def close(self): self.pipe.close()


class Probe(PipeClient):
    def __init__(self, name, settings=None, args=(), file=None, config=None, native_options=None,
                 instance='multi', configure=True, executable=None, background=False):
        self.config = config or ROOT / 'test-results' / ('resume-' + name)
        self.config.mkdir(parents=True, exist_ok=True)
        if configure:
            options = {'osc':'no', 'border':'no', 'title-bar':'no', 'keepaspect-window':'no', 'keep-open':'yes',
                       'autocreate-playlist':'no', 'save-position-on-quit':'no', 'resume-playback':'yes',
                       'watch-later-dir':str(self.config/'watch_later')}
            options.update(native_options or {})
            (self.config/'mpv.conf').write_text(''.join(f'{k}={v}\n' for k,v in options.items()), encoding='utf8')
            (self.config/'mpvnet.conf').write_text(f'process-instance={instance}\nauto-load-folder=no\n', encoding='utf8')
        if settings is not None:
            cache = (self.config/'watch_later').resolve()
            if not cache.is_relative_to((ROOT/'test-results').resolve()):
                raise ValueError('Test configuration must stay inside test-results')
            if cache.exists(): shutil.rmtree(cache)
            (self.config/'modern-ui.json').write_text(json.dumps({'AutoFolderPlaylist': False, 'RememberSpeed': False, **settings}), encoding='utf8')
        endpoint = rf'\\.\pipe\mpvnet-resume-{name}-{os.getpid()}'
        env = os.environ.copy()
        env['MPVNET_HOME'] = str(self.config)
        self.log = (self.config/'player.log').open('wb')
        startup = None
        if background:
            startup = subprocess.STARTUPINFO()
            startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
            startup.wShowWindow = 4  # SW_SHOWNOACTIVATE: keep the user's current work focused.
        self.process = subprocess.Popen([str(executable or EXE), '--pause=yes', '--vo=null', '--ao=null', '--force-window=no',
                                         '--input-ipc-server=' + endpoint, *args, *([str(file)] if file else [])],
                                        env=env, stdout=self.log, stderr=self.log, startupinfo=startup)
        try:
            super().__init__(endpoint)
        except BaseException:
            if self.process.poll() is None:
                self.process.terminate()
                self.process.wait(timeout=10)
            self.log.close()
            raise

    def wait(self, predicate, title, timeout=10):
        until = time.monotonic() + timeout
        while time.monotonic() < until:
            if predicate(): return
            time.sleep(.025)
        raise AssertionError(title)

    def load(self, file, options=None):
        loaded = sum(e.get('event') == 'file-loaded' for e in self.events)
        command = ['loadfile', str(file), 'replace']
        if options is not None: command += [-1, options]
        self.request(command)
        self.wait(lambda: self.get('path') == str(file) and
                  sum(e.get('event') == 'file-loaded' for e in self.events) > loaded, 'file-loaded event')

    def settle(self, target, seconds=.8, paused=True):
        self.wait(lambda: (t := self.get('time-pos')) is not None and abs(t-target) < .2, 'target position reached')
        samples = []
        until = time.monotonic() + seconds
        while time.monotonic() < until:
            t = self.get('time-pos')
            samples.append(t)
            if paused:
                assert abs(t-target) < .2, ('target overwritten', target, t)
            else:
                assert target-.2 <= t <= target+seconds+.5, ('playing seek overwritten', target, t)
            time.sleep(.04)
        return {'target': target, 'first': samples[0], 'last': samples[-1], 'samples': len(samples)}

    def close(self):
        try:
            if self.process.poll() is None:
                try: self.request(['quit'])
                except OSError: pass
            self.pipe.close()
            self.process.wait(timeout=15)
        finally:
            if self.process.poll() is None:
                self.process.terminate()
                self.process.wait(timeout=10)
            self.log.close()


if __name__ == '__main__':
    import sys
    client = PipeClient(sys.argv[1])
    try: print(json.dumps(client.request(json.loads(sys.argv[2])), ensure_ascii=False))
    finally: client.close()
