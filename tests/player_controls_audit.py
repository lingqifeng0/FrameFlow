"""Observe an isolated graphical player while Computer Use checks its controls.

Only the public JSON IPC interface is used here. This never injects UI input.
"""
import argparse
import json
import time
from pipe_probe import Probe, ROOT

parser = argparse.ArgumentParser()
parser.add_argument("profile", choices=["ui", "reopen"])
parser.add_argument("--seconds", type=int, default=1800)
args = parser.parse_args()
folder = ROOT / "test-results/features-v15/ui"
folder.mkdir(parents=True, exist_ok=True)
stop = folder / "stop"
stop.unlink(missing_ok=True)
config = folder / "config"
settings = None if args.profile == "reopen" else {
    "Width": 1450, "Height": 880, "WindowDpi": 168,
    "MouseWheelVolume": True, "VolumeBoost": False,
    "AutoFolderPlaylist": False, "PlaylistVisible": True,
    "PlaylistMode": "sequence", "RememberSpeed": True, "Speed": 1.25,
}
probe = Probe("controls-v15", settings=settings, config=config,
    file=ROOT / "test-results/marktext-v11/source/video.mp4",
    args=["--vo=gpu", "--force-window=yes", "--hwdec=no", "--start=400"])
probe.wait(lambda: probe.get("user-data/modern/window") and probe.get("time-pos") is not None, "player frontend ready")
probe.request(["loadfile", str(ROOT / "test-results/frame-test.mp4"), "append"])
(folder / "actor.json").write_text(json.dumps({"pid": probe.process.pid,
    "endpoint": probe.pipe.name, "config": str(config)}, indent=2), encoding="utf8")
history = []
previous = None

def diagnostic(name):
    raw = probe.get("user-data/modern/" + name)
    return json.loads(raw) if raw else None

try:
    print(json.dumps({"pid": probe.process.pid, "profile": args.profile, "stop": str(stop)}), flush=True)
    until = time.monotonic() + args.seconds
    while not stop.exists() and time.monotonic() < until and probe.process.poll() is None:
        state = {name: diagnostic(name) for name in ("window", "volume", "speed", "playlist", "navigation")}
        state["native"] = {name: probe.get(name) for name in ("volume", "volume-max", "mute", "ontop", "speed",
            "video-zoom", "video-pan-x", "video-pan-y", "keep-open", "keep-open-pause", "loop-file", "loop-playlist")}
        if state != previous:
            previous = state
            history.append(state)
            (folder / (args.profile + "-state.json")).write_text(json.dumps(state, ensure_ascii=False, indent=2), encoding="utf8")
            (folder / (args.profile + "-history.json")).write_text(json.dumps(history, ensure_ascii=False, indent=2), encoding="utf8")
        time.sleep(.12)
finally:
    probe.close()
    # Reading preferences only after exit leaves native atomic replacement free
    # of the sharing locks held by ordinary Python reads on Windows.
    (folder / (args.profile + "-preferences.json")).write_text(
        (config / "modern-ui.json").read_text(encoding="utf8"), encoding="utf8")
    print(json.dumps({"profile": args.profile, "snapshots": len(history)}, ensure_ascii=False), flush=True)
