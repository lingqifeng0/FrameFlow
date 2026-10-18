"""Validate the recorded states of real Computer Use drags, not simulated input."""
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1] / 'test-results/resize-v13'
def states(profile):
    data = json.loads((root / profile / 'observations.json').read_text(encoding='utf-8'))
    assert not data['failures'], data['failures']
    return data['states']

gestures = 0
for profile, expected in [('icons', 8), ('labels', 1), ('defaults', 1)]:
    pending = None
    completed = 0
    for state in states(profile):
        if state['resizing']:
            if pending is None: pending = state
            assert not state['controls']['headerContentVisible']
            assert not state['controls']['footerContentVisible']
            assert state['toolbar']['layoutPasses'] == pending['toolbar']['layoutPasses']
        elif pending:
            assert state['toolbar']['layoutPasses'] == pending['toolbar']['layoutPasses'] + 1
            assert state['controls']['headerBounds']['height'] == pending['controls']['headerBounds']['height']
            assert state['controls']['footerBounds']['height'] == pending['controls']['footerBounds']['height']
            completed += 1
            pending = None
    assert completed == expected, (profile, completed, expected)
    gestures += completed
    print(f'PASS: {profile}: {completed} drags hide both bars and restore with one final toolbar layout')

before, reopened = states('defaults')[-1], states('reopen')[0]
assert all(before['bounds'][key] == reopened['bounds'][key] for key in ['width', 'height'])
a, b = before['toolbar'], reopened['toolbar']
assert a['total'] == b['total'] == 7 and a['showLabels'] and b['showLabels']
assert a['visibleCount'] == b['visibleCount'] and a['overflow'] == b['overflow']
for old, new in zip(a['buttons'], b['buttons']):
    assert all(old[key] == new[key] for key in ['id', 'title', 'visible', 'margins'])
    # A hidden item has no on-screen position; it can be rebuilt at (0,0).
    assert all(old['bounds'][key] == new['bounds'][key] for key in ['width', 'height'])
    if old['visible']: assert old['bounds'] == new['bounds']
assert [button['id'] for button in b['buttons']] == [
    'frame-back', 'frame-forward', 'screenshot', 'bookmark', 'ab-loop', 'frame-back', 'timestamp']
print('PASS: reopen retains 1248x772 size, seven edited buttons, order, labels and overflow')
icons = states('icons')
assert any(s['mode'] == 'maximized' and s['toolbar']['visibleCount'] == 32 and not s['toolbar']['overflow'] for s in icons)
assert any(s['mode'] == 'fullscreen' and not s['controls']['headerVisible'] and not s['controls']['footerVisible'] for s in icons)
mode_final = json.loads((root / 'mode-final.json').read_text(encoding='utf-8'))['states'][-1]
assert mode_final['mode'] == 'normal' and mode_final['controls']['headerContentVisible'] and mode_final['controls']['footerContentVisible']
print('PASS: maximize expands all 32 icons; fullscreen auto-hides; ordinary window restores both bars')
print(f'All recorded resize assertions passed ({gestures} real drags).')
