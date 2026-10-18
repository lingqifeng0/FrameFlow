"""Real libmpv/EXE smoke checks, with a separate portable test configuration."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import time
import sys

root = Path(__file__).resolve().parents[1]
exe = Path(os.environ.get("MPVNET_TEST_EXE", root / "dist/FrameFlow/FrameFlow.exe"))
fixture = root / "test-results/frame-test.mp4"
config = Path(os.environ.get("MPVNET_TEST_CONFIG", root / "test-results/native-config"))
config.mkdir(parents=True, exist_ok=True)
for filename in ("mpv.conf", "mpvnet.conf"):
    shutil.copyfile(exe.parent / "portable_config" / filename, config / filename)
pipe_path = os.environ.get("MPVNET_TEST_PIPE", r"\\.\pipe\FrameFlow-test")
with (config / "mpv.conf").open("a", encoding="utf-8") as file:
    file.write("\ninput-ipc-server=" + pipe_path + "\n")
env = os.environ.copy()
env["MPVNET_HOME"] = str(config)
reports = []

def check(ok, name, data=None):
    if not ok:
        raise AssertionError(f"{name}: {data}")
    reports.append({"check": name, "result": "passed", "data": data})
    print("PASS:", name, data if data is not None else "", flush=True)

def start():
    info = subprocess.STARTUPINFO()
    info.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    info.wShowWindow = 1
    process = subprocess.Popen([str(exe), "--pause=yes", "--mute=yes", str(fixture)], env=env, startupinfo=info)
    (root / "test-results/player.pid").write_text(str(process.pid))
    deadline = time.monotonic() + 12
    while True:
        if process.poll() is not None:
            raise RuntimeError("Player exited during startup")
        try:
            pipe = open(pipe_path, "r+b", buffering=0)
            return process, pipe
        except (FileNotFoundError, OSError):
            if time.monotonic() > deadline:
                raise TimeoutError("IPC pipe did not appear")
            time.sleep(.1)

counter = 0
def request(pipe, command):
    global counter
    counter += 1
    pipe.write((json.dumps({"command": command, "request_id": counter}) + "\n").encode())
    buf = bytearray()
    while True:
        char = pipe.read(1)
        if not char:
            raise EOFError("Player disconnected")
        if char != b"\n":
            buf.extend(char)
            continue
        message = json.loads(buf)
        buf.clear()
        if message.get("request_id") == counter:
            if message.get("error") in ("property unavailable", "property not found"):
                return None
            if message.get("error") != "success":
                raise RuntimeError(message)
            return message.get("data")

def get(pipe, name): return request(pipe, ["get_property", name])
def wait_property(pipe, name, predicate):
    deadline = time.monotonic() + 8
    while True:
        value = get(pipe, name)
        if predicate(value): return value
        if time.monotonic() > deadline: raise TimeoutError(f"{name}: {value}")
        time.sleep(.1)

process, pipe = start()
try:
    wait_property(pipe, "path", lambda p: p and Path(p) == fixture)
    wait_property(pipe, "user-data/modern/state", lambda s: s and json.loads(s).get("path") == str(fixture))
    check(get(pipe, "width") == 640 and get(pipe, "height") == 360 and abs(get(pipe, "container-fps") - 30) < .001, "真实视频分辨率和帧率", {"width": 640, "height": 360, "fps": 30})
    request(pipe, ["set_property", "speed", 1.34])
    check(abs(get(pipe, "speed") - 1.34) < .0001, "倍率支持 0.01 精度", get(pipe, "speed"))
    request(pipe, ["seek", 6, "absolute+exact"])
    wait_property(pipe, "time-pos", lambda p: abs(p - 6) < .04)
    wait_property(pipe, "seeking", lambda p: p is False)
    time.sleep(.2)
    before = get(pipe, "time-pos")
    request(pipe, ["frame-step"])
    after = wait_property(pipe, "time-pos", lambda p: p > before + .02)
    check(abs(after - before - 1 / 30) < .004 and get(pipe, "pause"), "逐帧前进一次后暂停", after - before)
    request(pipe, ["frame-back-step"])
    back = wait_property(pipe, "time-pos", lambda p: p < after - .02)
    check(abs(back - before) < .004, "逐帧回退返回前一画面时间", back)
    request(pipe, ["observe_property", 41, "speed"])
    request(pipe, ["set_property", "speed", 1.35])
    check(abs(get(pipe, "speed") - 1.35) < .0001, "属性订阅及外部修改倍率")
    request(pipe, ["set_property", "speed", 1.34])
    request(pipe, ["seek", 9, "absolute+exact"])
    wait_property(pipe, "time-pos", lambda p: abs(p - 9) < .04)
    time.sleep(.5)
    request(pipe, ["quit"])
    pipe.close(); process.wait(timeout=15)
    process, pipe = start()
    wait_property(pipe, "path", lambda p: p and Path(p) == fixture)
    time.sleep(.6)
    check(abs(get(pipe, "speed") - 1.34) < .0001, "退出重开记住倍率", get(pipe, "speed"))
    check(abs(get(pipe, "time-pos") - 9) < .05, "退出重开记住播放断点", get(pipe, "time-pos"))
    print("READY_FOR_UI", process.pid, flush=True)
    (root / "test-results/native-smoke.json").write_text(json.dumps(reports, ensure_ascii=False, indent=2), encoding="utf-8")
    if "--keep-open" not in sys.argv:
        request(pipe, ["quit"])
        process.wait(timeout=15)
finally:
    pipe.close()
    if "--keep-open" not in sys.argv and process.poll() is None:
        process.terminate()
