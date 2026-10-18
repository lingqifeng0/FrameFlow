import json
import os
from pathlib import Path
import time
import wave

root = Path(__file__).resolve().parents[1]
sample = root / "test-results/stereo-test.wav"
with wave.open(str(sample), "wb") as writer:
    writer.setnchannels(2)
    writer.setsampwidth(2)
    writer.setframerate(48000)
    for _ in range(60): writer.writeframes(bytes(48000 * 4))
with open(os.environ.get("MPVNET_TEST_PIPE", r"\\.\pipe\FrameFlow"), "r+b", buffering=0) as pipe:
    sequence = 0
    def request(args):
        global sequence
        sequence += 1
        pipe.write((json.dumps({"command": args, "request_id": sequence}) + "\n").encode())
        buf = bytearray()
        while True:
            byte = pipe.read(1)
            if byte != b"\n": buf.extend(byte); continue
            value = json.loads(buf); buf.clear()
            if value.get("request_id") == sequence: return value.get("data")
    request(["audio-add", str(sample), "select"])
    end = time.monotonic() + 8
    while True:
        channels = request(["get_property", "audio-params/channel-count"])
        rate = request(["get_property", "audio-params/samplerate"])
        if channels == 2 and rate == 48000: break
        if time.monotonic() > end: raise TimeoutError((channels, rate))
        time.sleep(.1)
    codec = request(["get_property", "audio-codec-name"])
    print("PASS: actual audio metadata", {"channels": channels, "sampleRate": rate, "codec": codec})
    (root / "test-results/native-audio.json").write_text(json.dumps({"result": "passed", "channels": channels, "sampleRate": rate, "codec": codec}), encoding="utf-8")
