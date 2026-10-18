"""Check control preferences against real libmpv and the public app commands."""
import json
from pathlib import Path
import shutil
import time
from pipe_probe import Probe, ROOT

folder = ROOT / "test-results/features-v15/native"
folder.mkdir(parents=True, exist_ok=True)
first, second = folder / "first.mp4", folder / "second.mp4"
for target in (first, second):
    shutil.copy2(ROOT / "test-results/frame-test.mp4", target)
checks = []

def passed(title, data=None):
    checks.append({"check": title, "result": "passed", "data": data})
    (folder / "report.json").write_text(json.dumps(checks, ensure_ascii=False, indent=2), encoding="utf8")
    print("PASS:", title, data if data is not None else "", flush=True)

def loaded(probe):
    probe.wait(lambda: probe.get("time-pos") is not None and probe.get("duration") is not None,
        "startup media loaded")
    # Startup command-line processing must finish before appending the second file.
    time.sleep(.4)

for extended, maximum in ((False, 100), (True, 300)):
    probe = Probe("v15-volume-" + str(maximum), config=folder / ("volume-" + str(maximum)),
        settings={"VolumeBoost": extended, "MouseWheelVolume": False}, file=first)
    try:
        loaded(probe)
        assert probe.get("volume-max") == maximum
        probe.request(["add", "volume", 1000])
        assert probe.get("volume") == maximum
        probe.request(["add", "volume", -1000])
        assert probe.get("volume") == 0
        passed(str(maximum) + "% native volume cap is enforced for increase and decrease")
        probe.request(["set_property", "video-zoom", 1.2])
        probe.request(["set_property", "video-pan-x", .45])
        probe.request(["set_property", "video-pan-y", -.25])
        probe.request(["script-message", "modern-tool", "zoom-reset"])
        probe.wait(lambda: all(probe.get(name) == 0 for name in ("video-zoom", "video-pan-x", "video-pan-y")),
            "view reset restores zoom and pan together")
        passed("view reset restores fitted size and centered position with volume extension " + str(extended))
    finally:
        probe.close()

for mode in ("stop-current", "sequence", "single-loop", "list-loop", "random"):
    config = folder / mode
    probe = Probe("v15-mode-" + mode, config=config,
        settings={"PlaylistMode": mode, "RememberPosition": True}, file=first)
    try:
        loaded(probe)
        probe.request(["loadfile", str(second), "append"])
        probe.wait(lambda: probe.get("playlist-count") == 2, "queue appended")
        native_file, native_list = probe.get("loop-file"), probe.get("loop-playlist")
        assert native_file == "inf" if mode == "single-loop" else native_file in (False, 0, "no")
        assert native_list == "inf" if mode in ("list-loop", "random") else native_list in (False, 0, "no")
        native_keep_open = probe.get("keep-open")
        assert native_keep_open == "always" if mode == "stop-current" else native_keep_open in (True, "yes")
        passed(mode + ": native options match the selected mode; both entries are retained")
        duration = probe.get("duration")
        probe.request(["seek", duration - .3, "absolute+exact"])
        probe.request(["set_property", "pause", False])
        if mode == "stop-current":
            probe.wait(lambda: probe.get("eof-reached") and probe.get("pause"), "stop-current pauses at EOF")
            time.sleep(.6)
            assert probe.get("path") == str(first) and probe.get("playlist-pos") == 0 and probe.get("playlist-count") == 2
            passed("stop-current reaches EOF, pauses and holds current media without advancing")
            probe.request(["script-message", "modern-playlist", "mode", "sequence"])
            probe.wait(lambda: probe.get("keep-open") in (True, "yes"), "leaving stop-current clears keep-open=always")
            passed("switching from stop-current to sequence restores automatic advancing")
            probe.request(["script-message", "modern-playlist", "mode", "stop-current"])
            probe.wait(lambda: probe.get("keep-open") == "always", "stop-current is accepted by the public mode command")
            passed("public playlist mode command accepts stop-current immediately")
            probe.request(["playlist-next", "weak"])
            probe.wait(lambda: probe.get("path") == str(second), "manual next still works")
            passed("stop-current still permits an explicit Next command")
        elif mode == "single-loop":
            probe.wait(lambda: probe.get("path") == str(first) and (probe.get("time-pos") or 0) < 2,
                "single-loop restarts the current media")
            passed("single-loop restarts current media instead of advancing")
        else:
            probe.wait(lambda: probe.get("path") == str(second), "automatic next media")
            passed(mode + ": playback advances to the next media")
            if mode == "list-loop":
                probe.wait(lambda: probe.get("duration") is not None, "second media ready")
                probe.request(["seek", probe.get("duration") - .3, "absolute+exact"])
                probe.request(["set_property", "pause", False])
                probe.wait(lambda: probe.get("path") == str(first), "list wraps to first media")
                passed("list-loop wraps from the last entry to the first")
    finally:
        probe.close()
    if mode == "stop-current":
        reopened = Probe("v15-stop-reopen", config=config, configure=False, file=first)
        try:
            loaded(reopened)
            assert reopened.get("keep-open") == "always" and reopened.get("keep-open-pause") in (True, "yes")
            passed("saved stop-current mode is restored on the next launch")
        finally:
            reopened.close()
print("All", len(checks), "native control checks passed.")
