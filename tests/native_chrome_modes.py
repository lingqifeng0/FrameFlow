"""Final chrome mode transitions through the player API; no input injection."""
import json
from pipe_probe import Probe, ROOT

folder = ROOT / 'test-results/resize-v13'
probe = Probe('resize-v13-modes', config=folder / 'icons/config', configure=False,
    file=ROOT / 'test-results/marktext-v11/source/video.mp4',
    args=['--vo=gpu', '--hwdec=no', '--force-window=yes'])
states = []
def snapshot():
    raw = probe.get('user-data/modern/window')
    return json.loads(raw) if raw else {}

def mode(action, expected):
    probe.request(['script-message-to', 'mpvnet', 'modern-window', action])
    # A script-message reply confirms dispatch; wait for the observed outcome.
    probe.wait(lambda: snapshot().get('mode') == expected, action + ' completed')
    state = snapshot()
    assert not state['resizing']
    if expected in ('normal', 'maximized'):
        assert state['controls']['headerContentVisible'] and state['controls']['footerContentVisible']
    states.append(state)
    print('PASS:', action, '->', expected)

try:
    probe.wait(lambda: snapshot().get('mode') == 'normal', 'normal startup')
    original = snapshot()['normalBounds']
    mode('maximize', 'maximized')
    mode('fullscreen', 'fullscreen')
    mode('exit-fullscreen', 'maximized')
    mode('restore', 'normal')
    mode('fullscreen', 'fullscreen')
    mode('exit-fullscreen', 'normal')
    final = states[-1]
    assert all(final['normalBounds'][key] == original[key] for key in ('width', 'height'))
    assert final['toolbar']['total'] == 32
    (folder / 'mode-final.json').write_text(json.dumps({'states': states}, ensure_ascii=False, indent=2), encoding='utf-8')
    print('PASS: final normal size and all 32 configured buttons retained')
finally:
    probe.close()
