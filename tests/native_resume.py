"""Verify resume precedence against real libmpv playback, startup and IPC seeks."""
import json
from pathlib import Path
import shutil
import time
from pipe_probe import ROOT, Probe

MEDIA = ROOT / 'test-results/marktext-v11/source/video.mp4'
SECOND = ROOT / 'test-results/resume-second.mp4'
shutil.copy2(ROOT / 'test-results/frame-test.mp4', SECOND)
reports = []


def passed(title, data=None):
    reports.append({'check': title, 'result': 'passed', 'data': data})
    print('PASS:', title, data if data is not None else '', flush=True)


def seek(p, target, seconds=.8, paused=True):
    p.request(['seek', target, 'absolute+exact'])
    return p.settle(target, seconds, paused)


saved = {'RememberPosition': True, 'Positions': {str(MEDIA): 1037.2896666666668, str(SECOND): 3}}
for name, args, target in (
    ('ordinary', (), 1037.2896666666668),
    ('no-resume', ('--resume-playback=no',), 0),
    ('start-zero', ('--start=0',), 0),
    ('start-timestamp', ('--start=461.850',), 461.85),
):
    p = Probe(name, settings=saved, args=args, file=MEDIA)
    try:
        passed(name + ': first open honors resume/start priority', p.settle(target, 1))
        if name == 'no-resume':
            passed('cold external paused seek is stable for 3 seconds', seek(p, 461.85, 3))
            passed('warm external seek on the same video stays at the new timestamp', seek(p, 654.98, 1.2))
            p.load(SECOND)
            passed('switching videos with resume disabled starts at zero', p.settle(0))
            passed('external seek after switching videos wins', seek(p, 2))
            p.load(MEDIA)
            passed('reopening the first video with resume disabled starts at zero', p.settle(0))
            for t in (220, 555, 461.85): p.request(['seek', t, 'absolute+exact'])
            passed('rapid successive timestamps keep the final requested position', p.settle(461.85, 1.2))
            p.request(['set_property', 'pause', False])
            passed('playing external seek advances normally without jumping to resume', seek(p, 100, 1.2, False))
            p.request(['set_property', 'pause', True])
        elif name == 'ordinary':
            passed('external seek overrides an already restored resume position', seek(p, 461.85, 3))
            p.load(SECOND)
            passed('different video restores its own saved position', p.settle(3))
            p.load(MEDIA)
            passed('switching back restores the final outgoing position', p.settle(461.85))
            p.load(MEDIA, {'start': '123.45'})
            passed('per-load explicit start outranks the saved position', p.settle(123.45))
            p.load(MEDIA, {'start': '0'})
            passed('per-load explicit zero is also respected', p.settle(0))
            p.load(MEDIA, {'resume-playback': 'no'})
            passed('per-load resume=no disables common resume for that load', p.settle(0))
    finally:
        p.close()

config = ROOT / 'test-results/resume-persist'
p = Probe('persist-first', settings=saved, file=MEDIA, config=config)
try:
    p.settle(1037.2896666666668)
    seek(p, 600.25)
finally:
    p.close()
stored = json.loads((config/'modern-ui.json').read_text(encoding='utf8'))
assert abs(stored['Positions'][str(MEDIA)] - 600.25) < .2, stored['Positions']
passed('exit saves the actual current position', stored['Positions'][str(MEDIA)])
p = Probe('persist-reopen', file=MEDIA, config=config)
try:
    passed('ordinary reopen still resumes at the saved position', p.settle(600.25, 1.2))
finally:
    p.close()

p = Probe('remember-off', settings={'RememberPosition': False, 'Positions': saved['Positions']}, file=MEDIA)
try:
    passed('disabling remember-position skips custom resume', p.settle(0))
finally:
    p.close()

config = ROOT / 'test-results/resume-watch-later'
p = Probe('watch-save', settings=saved, file=MEDIA, config=config)
try:
    p.settle(1037.2896666666668)
    seek(p, 777.7)
    p.request(['write-watch-later-config'])
finally:
    p.close()
stored = json.loads((config/'modern-ui.json').read_text(encoding='utf8'))
stored['Positions'][str(MEDIA)] = 1037.2896666666668
(config/'modern-ui.json').write_text(json.dumps(stored), encoding='utf8')
p = Probe('watch-reopen', file=MEDIA, config=config)
try:
    passed('common progress outranks old native watch-later start', p.settle(1037.2896666666668))
finally:
    p.close()

stored = json.loads((config/'modern-ui.json').read_text(encoding='utf8'))
stored['Positions'].pop(str(MEDIA))
(config/'modern-ui.json').write_text(json.dumps(stored), encoding='utf8')
p = Probe('legacy-only', file=MEDIA, config=config, configure=False)
try:
    passed('native-only progress is imported once into the common record', p.settle(777.7))
finally:
    p.close()
stored = json.loads((config/'modern-ui.json').read_text(encoding='utf8'))
assert abs(stored['Positions'][str(MEDIA)] - 777.7) < .2, stored['Positions']
passed('legacy migration writes the common progress record', stored['Positions'][str(MEDIA)])

(ROOT/'test-results/native-resume-current.json').write_text(json.dumps(reports, ensure_ascii=False, indent=2), encoding='utf8')
print('All', len(reports), 'native resume checks passed.', flush=True)
