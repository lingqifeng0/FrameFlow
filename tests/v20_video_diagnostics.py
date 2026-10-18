"""Inspect only the own visual probe through mpv's public renderer API."""
import json
from pipe_probe import PipeClient,ROOT

folder=ROOT/'test-results/appearance-v20/default'
actor=json.loads((folder/'actor.json').read_text(encoding='utf8'))
client=PipeClient(actor['endpoint'])
try:
    state={name:client.get(name) for name in ('current-vo','gpu-context','hwdec-current','video-params','video-out-params','vf','osd-level','osd-playing-msg','user-data/modern/state','options/config-dir')}
    client.request(['screenshot-to-file',str(folder/'gpu-output.png'),'window'])
    (folder/'video-diagnostics.json').write_text(json.dumps(state,ensure_ascii=False,indent=2),encoding='utf8')
    print(json.dumps(state,ensure_ascii=False,indent=2))
finally:client.close()
