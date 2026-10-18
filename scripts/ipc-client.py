"""One-shot mpv named-pipe client: python ipc-client.py '{"command":[...]}'"""
import argparse
import json

parser = argparse.ArgumentParser()
parser.add_argument("request", help="JSON object containing a command array")
parser.add_argument("--pipe", default=r"\\.\pipe\FrameFlow")
args = parser.parse_args()
request = json.loads(args.request)
request.setdefault("request_id", 1)
with open(args.pipe, "r+b", buffering=0) as pipe:
    pipe.write((json.dumps(request, ensure_ascii=False) + "\n").encode("utf-8"))
    buffer = b""
    while True:
        byte = pipe.read(1)
        if not byte:
            raise SystemExit("播放器断开连接")
        if byte != b"\n":
            buffer += byte
            continue
        result = json.loads(buffer)
        print(json.dumps(result, ensure_ascii=False))
        if result.get("request_id") == request["request_id"]:
            break
        buffer = b""
