"""Check recorded mouse drags and reopen/layout through the player's own IPC.

The reopened probes use SW_SHOWNOACTIVATE and never inject desktop input.
"""
import json
import argparse
from pipe_probe import Probe, ROOT

parser=argparse.ArgumentParser()
parser.add_argument('--version',default='19')
args=parser.parse_args()
folder = ROOT / ('test-results/resize-v'+args.version)
records = json.loads((folder / 'icons/observations.json').read_text(encoding='utf8'))
assert not records['failures'], records['failures']
previous = None
completed = 0
for state in records['states']:
    if state['resizing']:
        if previous is None:
            previous = state
        c = state['controls']
        assert c['headerColor'] == '#000000' and c['headerRenderingSuspended']
        assert state['rounded']
        assert c['headerRegionUpdates'] == previous['controls']['headerRegionUpdates']
        assert state['toolbar']['layoutPasses'] == previous['toolbar']['layoutPasses']
    elif previous:
        c = state['controls']
        assert c['headerColor'] == '#0096FF' and not c['headerRenderingSuspended']
        assert c['headerRegionUpdates'] == previous['controls']['headerRegionUpdates'] + 1
        assert c['resizePixelDiscards'] > previous['controls']['resizePixelDiscards']
        assert state['toolbar']['layoutPasses'] == previous['toolbar']['layoutPasses'] + 1
        assert c['headerBounds']['x'] >= 0 and c['headerBounds']['y'] >= 0
        completed += 1
        previous = None
assert completed >= 3 and previous is None
last_frame = records['states'][-1]['controls']['resizeFrames']
assert last_frame >= completed
assert records['states'][-1]['controls']['resizeRepaints'] >= last_frame + 2*completed
print(f'PASS: {completed} recorded mouse drags clear blue paint, freeze clipping/layout and restore once', flush=True)

checks = []
last = records['states'][-1]
config = folder / 'icons/config'
with (config / 'modern-ui.json').open(encoding='utf8') as f:
    saved = json.load(f)
assert (saved['Width'], saved['Height']) == (last['bounds']['width'], last['bounds']['height'])

def inspect(probe):
    def window():
        return json.loads(probe.get('user-data/modern/window') or '{}')
    probe.wait(lambda: window().get('mode') == 'normal', 'normal startup')
    s = window()
    c = s['controls']
    assert not s['resizing'] and s['rounded']
    assert c['headerColor'] == '#0096FF' and not c['headerRenderingSuspended']
    for name in ('headerBounds', 'footerBounds'):
        rect = c[name]
        assert rect['x'] >= 0 and rect['y'] >= 0
        assert rect['x'] + rect['width'] <= s['bounds']['width']
        assert rect['y'] + rect['height'] <= s['bounds']['height']
    t = s['toolbar']
    assert t['total'] == t['capacity'] == 16
    right = 0
    for button in t['buttons'][:t['visibleCount']]:
        rect = button['bounds']
        assert rect['x'] >= right and rect['x'] + rect['width'] <= t['width']
        assert rect['y'] >= 0 and rect['y'] + rect['height'] <= t['height']
        right = rect['x'] + rect['width']
    if t['overflow']:
        rect = t['moreBounds']
        assert rect['x'] >= right and rect['x'] + rect['width'] <= t['width']
    return s

probe = Probe('resize-v'+args.version+'-reopen', config=config, configure=False, background=True)
try:
    s = inspect(probe)
    assert (s['bounds']['width'],s['bounds']['height']) == (last['bounds']['width'],last['bounds']['height'])
    assert [b['id'] for b in s['toolbar']['buttons']] == [b['id'] for b in last['toolbar']['buttons']]
    checks.append({'check':'last mouse-resized size and all 16 buttons survive reopening','state':s})
    print('PASS: last resized dimensions and ordered 16 buttons survive reopening', flush=True)
finally:
    probe.close()

for labels in (False,True):
    narrow = {**saved, 'Width':820, 'Height':620, 'ShowButtonLabels':labels}
    probe = Probe('resize-v'+args.version+'-narrow-'+str(labels), config=folder/('narrow-labels' if labels else 'narrow-icons'),
                  settings=narrow, background=True)
    try:
        s = inspect(probe)
        assert s['toolbar']['overflow'] and 0 < s['toolbar']['visibleCount'] < 16
        assert s['toolbar']['showLabels'] == labels
        checks.append({'check':'narrow '+('labels' if labels else 'icons')+' preserve 16 buttons with More','state':s})
        print('PASS: narrow '+('labels' if labels else 'icons')+' retain 16 buttons and show More without clipping',flush=True)
    finally:
        probe.close()
(folder/'release-checks.json').write_text(json.dumps({'mouseDrags':completed,'checks':checks},ensure_ascii=False,indent=2),encoding='utf8')
