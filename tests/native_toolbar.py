"""Exercise the actual toolbar executor, playlist state and upgrade migration in the EXE."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import time

root = Path(__file__).resolve().parents[1]
exe = Path(os.environ.get("MPVNET_TEST_EXE", root / "dist/FrameFlow/FrameFlow.exe"))
config = root / "test-results/toolbar-config"
config.mkdir(parents=True, exist_ok=True)
pipe_path = r"\\.\pipe\FrameFlow-toolbar-test"
for filename in ("mpv.conf", "mpvnet.conf"):
    shutil.copyfile(exe.parent / "portable_config" / filename, config / filename)
with (config / "mpv.conf").open("a", encoding="utf-8") as file:
    file.write("\ninput-ipc-server=" + pipe_path + "\n")
fixture = root / "test-results/frame-test.mp4"
second = root / "test-results/frame-test-2.mp4"
third = root / "test-results/frame-test-3.mp4"
shutil.copyfile(fixture, second)
shutil.copyfile(fixture, third)
(config / "modern-ui.json").write_text(json.dumps({
    "Speed": 1.25, "SpeedStep": .05, "PlaylistVisible": False, "AutoFolderPlaylist": False,
    "Toolbar": [{"Id": "frame-back", "Title": "上一帧", "Value": 0},
                {"Id": "seek-forward", "Title": "前进 7 秒", "Value": 7},
                {"Id": "plugin:study-notes:bookmark", "Title": "旧插件按钮", "Value": 0}],
    "Bookmarks": [{"File": str(fixture), "Time": 10, "Title": "原有书签"}]
}), encoding="utf-8")
environment = os.environ.copy()
environment["MPVNET_HOME"] = str(config)
reports = []
sequence = 0

def start():
    startup = subprocess.STARTUPINFO()
    startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    startup.wShowWindow = 0
    process = subprocess.Popen([str(exe), "--pause=yes", "--mute=yes", str(fixture)], env=environment, startupinfo=startup)
    deadline = time.monotonic() + 15
    while True:
        if process.poll() is not None: raise RuntimeError("Player exited during startup")
        try: return process, open(pipe_path, "r+b", buffering=0)
        except OSError:
            if time.monotonic() > deadline: process.terminate(); raise
            time.sleep(.1)

def request(command):
    global sequence
    sequence += 1
    pipe.write((json.dumps({"command": command, "request_id": sequence}) + "\n").encode())
    buffer = bytearray()
    while True:
        byte = pipe.read(1)
        if not byte: raise EOFError("Player disconnected")
        if byte != b"\n": buffer.extend(byte); continue
        message = json.loads(buffer); buffer.clear()
        if message.get("request_id") != sequence: continue
        if message.get("error") in ("property unavailable", "property not found"): return None
        if message.get("error") != "success": raise RuntimeError(message)
        return message.get("data")

def get(name): return request(["get_property", name])
def wait(name, predicate):
    deadline = time.monotonic() + 7
    while True:
        value = get(name)
        if predicate(value): return value
        if time.monotonic() > deadline: raise AssertionError((name, value))
        time.sleep(.05)

def check(condition, name, data=None):
    if not condition: raise AssertionError((name, data))
    reports.append({"check": name, "result": "passed", "data": data})
    print("PASS:", name, flush=True)

def action(name, value=None):
    args = ["script-message-to", "mpvnet", "modern-tool", name]
    if value is not None: args.append(str(value))
    request(args)

def action_number(action_name, property_name, expected, value=None, tolerance=.001):
    action(action_name, value)
    actual = wait(property_name, lambda v: isinstance(v, (int, float)) and abs(v - expected) < tolerance)
    check(True, action_name + " changes " + property_name, actual)

def playlist():
    value = get("user-data/modern/playlist")
    return json.loads(value) if value else {}

process, pipe = start()
try:
    wait("user-data/modern/state", lambda v: v and json.loads(v).get("path") == str(fixture))
    tools = json.loads(get("user-data/modern/tools"))
    check(len(tools) == 65 and len({t["id"] for t in tools}) == 65 and any(t['id'] == 'window-reset' for t in tools),
          "65 distinct toolbar functions including restoring initial window size", len(tools))
    check(get("user-data/modern/plugins") is None, "plugin manager API removed")
    action_number("speed", "speed", 1.34, 1.34)
    action_number("speed-up", "speed", 1.39)
    action_number("speed-down", "speed", 1.34)
    action_number("speed-reset", "speed", 1)
    request(["seek", 6, "absolute+exact"])
    wait("time-pos", lambda v: abs(v - 6) < .04)
    wait("seeking", lambda v: v is False)
    action_number("seek-forward", "time-pos", 8.25, 2.25, .04)
    wait("seeking", lambda v: v is False)
    action_number("seek-back", "time-pos", 6, 2.25, .04)
    action_number("sub-later", "sub-delay", .17, .17)
    action_number("sub-earlier", "sub-delay", 0, .17)
    action_number("sub-size-up", "sub-scale", 1.1)
    action_number("sub-size-reset", "sub-scale", 1)
    action_number("sub-up", "sub-pos", 95)
    action_number("sub-down", "sub-pos", 100)
    visible = get("sub-visibility"); action("sub-toggle")
    check(wait("sub-visibility", lambda v: v != visible) is not None, "subtitle visibility toggle")
    action_number("audio-later", "audio-delay", .12, .12)
    action_number("audio-delay-reset", "audio-delay", 0)
    request(["set_property", "volume", 40])
    action_number("volume-up", "volume", 45)
    action_number("volume-down", "volume", 37, 8)
    muted = get("mute"); action("mute")
    check(wait("mute", lambda v: v != muted) is not None, "mute toggle")
    action_number("zoom-in", "video-zoom", .25, .25)
    action_number("zoom-out", "video-zoom", .15, .1)
    action_number("pan-left", "video-pan-x", -.05)
    action_number("pan-up", "video-pan-y", -.05)
    action_number("rotate", "video-rotate", 90)
    action_number("aspect-4-3", "video-aspect-override", 4 / 3)
    action_number("brightness-up", "brightness", 5)
    action_number("contrast-up", "contrast", 5)
    action_number("saturation-down", "saturation", -5)
    action("picture-reset")
    wait("video-aspect-override", lambda v: v == -1)
    restored = {p: get(p) for p in ["video-zoom", "video-pan-x", "video-pan-y", "video-rotate", "brightness", "contrast", "saturation"]}
    check(all(value == 0 for value in restored.values()), "reset all picture adjustments", restored)
    action("loop-file"); check(wait("loop-file", lambda v: v == "inf") == "inf", "single-file loop on")
    action("loop-file"); check(wait("loop-file", lambda v: v in ("no", False, 0)) is not None, "single-file loop off")
    action("loop-playlist"); check(wait("loop-playlist", lambda v: v == "inf") == "inf", "playlist loop on")
    action("loop-playlist")
    request(["loadfile", str(second), "append"])
    request(["loadfile", str(third), "append"])
    action("playlist")
    wait("user-data/modern/playlist", lambda v: v and json.loads(v).get("visible") and json.loads(v).get("count") == 3)
    check(playlist()["count"] == 3, "right playlist expands with three items")
    request(["playlist-move", "2", "1"])
    wait("user-data/modern/playlist", lambda v: v and json.loads(v)["items"][1]["path"] == str(third))
    check(True, "right playlist reflects reordering")
    request(["set_property", "playlist-pos", 1])
    wait("path", lambda v: v == str(third))
    wait("user-data/modern/playlist", lambda v: v and json.loads(v)["current"] == 1)
    check(True, "right playlist highlights the current video")
    request(["playlist-remove", "2"])
    wait("user-data/modern/playlist", lambda v: v and json.loads(v)["count"] == 2)
    check(True, "right playlist reflects removal")
    action("playlist")
    wait("user-data/modern/playlist", lambda v: v and json.loads(v)["visible"] is False)
    check(True, "right playlist collapses")
    action("playlist")
    wait("user-data/modern/playlist", lambda v: v and json.loads(v)["visible"])
    request(["quit"]); pipe.close(); process.wait(timeout=15)
    settings = json.loads((config / "modern-ui.json").read_text(encoding="utf-8"))
    check([t["Id"] for t in settings["Toolbar"]] == ["frame-back", "seek-forward"] and settings["Toolbar"][1]["Value"] == 7, "upgrade removes old plugin buttons and keeps customized buttons")
    check(settings["Bookmarks"][0]["Title"] == "原有书签", "upgrade preserves existing bookmarks")
    process, pipe = start()
    wait("user-data/modern/playlist", lambda v: v and json.loads(v)["visible"])
    check(True, "right playlist open state survives restart")
    request(["quit"]); process.wait(timeout=15)
    (root / "test-results/native-toolbar.json").write_text(json.dumps(reports, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"All {len(reports)} checks passed.", flush=True)
finally:
    pipe.close()
    if process.poll() is None: process.terminate()
