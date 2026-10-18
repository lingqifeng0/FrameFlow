"""Check the real host window modes and sidebar layout through the player's IPC API."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import time

root = Path(__file__).resolve().parents[1]
exe = Path(os.environ.get("MPVNET_TEST_EXE", root / "dist/FrameFlow/FrameFlow.exe"))
config = root / "test-results/window-config"
config.mkdir(parents=True, exist_ok=True)
pipe_path = r"\\.\pipe\FrameFlow-window-test"
for name in ("mpv.conf", "mpvnet.conf"):
    shutil.copyfile(exe.parent / "portable_config" / name, config / name)
with (config / "mpv.conf").open("a", encoding="utf-8") as file:
    file.write("\ninput-ipc-server=" + pipe_path + "\n")
(config / "modern-ui.json").write_text(json.dumps({
    "Width": 1120, "Height": 700, "PlaylistVisible": True, "PlaylistWidth": 550, "AutoFolderPlaylist": False
}), encoding="utf-8")
environment = os.environ.copy()
environment["MPVNET_HOME"] = str(config)
reports = []
sequence = 0


def start():
    startup = subprocess.STARTUPINFO()
    startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    startup.wShowWindow = 0
    process = subprocess.Popen([str(exe), "--pause=yes", "--mute=yes", str(root / "test-results/frame-test.mp4")],
                               env=environment, startupinfo=startup)
    deadline = time.monotonic() + 15
    while True:
        if process.poll() is not None:
            raise RuntimeError("Player exited during startup")
        try:
            return process, open(pipe_path, "r+b", buffering=0)
        except OSError:
            if time.monotonic() > deadline:
                process.terminate()
                raise
            time.sleep(.1)


def request(command):
    global sequence
    sequence += 1
    pipe.write((json.dumps({"command": command, "request_id": sequence}) + "\n").encode())
    buffer = bytearray()
    while True:
        byte = pipe.read(1)
        if not byte:
            raise EOFError("Player disconnected")
        if byte != b"\n":
            buffer.extend(byte)
            continue
        message = json.loads(buffer)
        buffer.clear()
        if message.get("request_id") != sequence:
            continue
        if message.get("error") in ("property unavailable", "property not found"):
            return None
        if message.get("error") != "success":
            raise RuntimeError(message)
        return message.get("data")


def snapshot(name):
    value = request(["get_property", "user-data/modern/" + name])
    return json.loads(value) if value else {}


def wait(name, predicate):
    deadline = time.monotonic() + 8
    while True:
        value = snapshot(name)
        if predicate(value):
            return value
        if time.monotonic() > deadline:
            raise AssertionError((name, value))
        time.sleep(.1)


def action(mode):
    request(["script-message-to", "mpvnet", "modern-window", mode])


def check(condition, name, data=None):
    if not condition:
        raise AssertionError((name, data))
    reports.append({"check": name, "result": "passed", "data": data})
    print("PASS:", name, flush=True)


process, pipe = start()
try:
    initial = wait("window", lambda v: v.get("mode") == "normal")
    normal = initial["bounds"]
    sidebar = wait("playlist", lambda v: v.get("visible") and v.get("preferredWidth") == 550)
    check(sidebar["width"] < 550, "narrow window temporarily constrains sidebar without losing preferred width", sidebar)
    action("maximize")
    maximized = wait("window", lambda v: v.get("mode") == "maximized")
    check(maximized["bounds"]["width"] > normal["width"], "host maximizes", maximized)
    sidebar = wait("playlist", lambda v: v.get("width") == 550)
    check(True, "larger window restores preferred sidebar width", sidebar)
    request(["set_property", "window-maximized", False])
    time.sleep(1.2)
    check(snapshot("window")["mode"] == "maximized", "embedded video window status cannot restore maximized host")
    request(["observe_property", 90, "user-data/modern/window"])
    action("fullscreen")
    full = wait("window", lambda v: v.get("mode") == "fullscreen")
    check(full["bounds"] == full["screenBounds"], "fullscreen covers the selected monitor", full)
    check(full["normalBounds"] == normal, "fullscreen retains windowed bounds", full)
    request(["set_property", "window-maximized", False])
    time.sleep(1.2)
    check(snapshot("window")["mode"] == "fullscreen", "embedded video window status cannot shrink fullscreen host")
    action("exit-fullscreen")
    restored = wait("window", lambda v: v.get("mode") == "maximized")
    check(restored["normalBounds"] == normal, "fullscreen entered from maximized returns to maximized", restored)
    action("restore")
    restored = wait("window", lambda v: v.get("mode") == "normal")
    check(restored["bounds"] == normal, "restore keeps previous normal bounds", restored)
    check(snapshot("playlist")["preferredWidth"] == 550, "switching modes preserves preferred sidebar width")
    action("fullscreen")
    wait("window", lambda v: v.get("mode") == "fullscreen")
    action("exit-fullscreen")
    restored = wait("window", lambda v: v.get("mode") == "normal")
    check(restored["bounds"] == normal, "fullscreen entered from normal returns to the same window", restored)
    action("minimize")
    wait("window", lambda v: v.get("mode") == "minimized")
    action("restore")
    restored = wait("window", lambda v: v.get("mode") == "normal")
    check(restored["bounds"] == normal, "minimize and restore keep window dimensions", restored)
    action("fullscreen")
    wait("window", lambda v: v.get("mode") == "fullscreen")
    request(["quit"])
    pipe.close()
    process.wait(timeout=15)
    settings = json.loads((config / "modern-ui.json").read_text(encoding="utf-8"))
    check((settings["Width"], settings["Height"]) == (normal["width"], normal["height"]),
          "quitting fullscreen saves normal size, not monitor dimensions", settings)
    process, pipe = start()
    reopened = wait("window", lambda v: v.get("mode") == "normal")
    check((reopened["bounds"]["width"], reopened["bounds"]["height"]) == (normal["width"], normal["height"]),
          "restart restores normal dimensions", reopened)
    check(wait("playlist", lambda v: v.get("preferredWidth") == 550)["visible"], "sidebar width and open state survive restart")
    action("maximize")
    wait("window", lambda v: v.get("mode") == "maximized")
    check(wait("playlist", lambda v: v.get("width") == 550) is not None, "restart and maximize recover unclamped sidebar width")
    request(["quit"])
    process.wait(timeout=15)
    (root / "test-results/native-window.json").write_text(json.dumps(reports, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"All {len(reports)} checks passed.", flush=True)
finally:
    pipe.close()
    if process.poll() is None:
        process.terminate()
