"""Observe a dedicated graphical player while Computer Use performs real drags.

This harness uses only the public JSON IPC interface; it never injects input.
Its stop file ends only its own player and configuration.
"""
import argparse
import json
from pathlib import Path
import time
from pipe_probe import Probe, ROOT

parser = argparse.ArgumentParser()
parser.add_argument('profile', choices=['defaults', 'icons', 'labels', 'reopen', 'playing'])
parser.add_argument('--seconds', type=int, default=600)
args = parser.parse_args()
folder = ROOT / 'test-results/resize-v13' / args.profile
folder.mkdir(parents=True, exist_ok=True)
stop = folder / 'stop'
stop.unlink(missing_ok=True)
settings = None if args.profile in ('reopen', 'playing') else {
    'Width': 1300, 'Height': 780, 'WindowDpi': 144, 'RememberPosition': True,
    'Speed': 1.34, 'RememberSpeed': True, 'ShowButtonLabels': args.profile == 'labels',
}
if args.profile in ('icons', 'labels'):
    definitions = [('frame-back', '上一帧'), ('frame-forward', '下一帧'),
                   ('screenshot', '截图'), ('bookmark', '书签'),
                   ('ab-loop', 'AB 循环'), ('timestamp', '复制时间戳')]
    settings['Toolbar'] = [{'Id': definitions[i % 6][0],
        'Title': definitions[i % 6][1] + (' · 自定义操作 ' + str(i + 1) if i % 4 else ''),
        'Value': 0} for i in range(32)]
config = ROOT / 'test-results/resize-v13' / ('defaults' if args.profile in ('reopen', 'playing') else args.profile) / 'config'
video = ROOT / 'test-results/marktext-v11/source/video.mp4'
probe = Probe('resize-v13-' + args.profile, settings=settings, config=config,
    file=video, args=['--vo=gpu', '--hwdec=no', '--force-window=yes', '--start=400'] if settings else
    ['--vo=gpu', '--hwdec=no', '--force-window=yes', '--pause=' + ('no' if args.profile == 'playing' else 'yes')])
(folder / 'actor.json').write_text(json.dumps({'pid': probe.process.pid,
    'endpoint': probe.pipe.name, 'config': str(config)}, indent=2), encoding='utf-8')
observations = []
seen = set()
failures = []
event_index = 0

def inspect(state):
    if not isinstance(state, dict) or 'toolbar' not in state: return
    key = json.dumps(state, sort_keys=True)
    if key in seen: return
    seen.add(key)
    if args.profile == 'playing':
        state['playback'] = {'time': probe.get('time-pos'), 'pause': probe.get('pause')}
        if state['playback']['pause']:
            failures.append('Resize paused active playback')
    observations.append(state)
    toolbar, controls = state['toolbar'], state['controls']
    if state['resizing']:
        if controls['headerContentVisible'] or controls['footerContentVisible']:
            failures.append('Toolbar contents remained visible during live resize')
    elif state['mode'] == 'normal':
        if not controls['headerContentVisible'] or not controls['footerContentVisible']:
            failures.append('Windowed toolbar contents were not restored')
        count = toolbar['visibleCount']
        if any(b['visible'] != (i < count) for i, b in enumerate(toolbar['buttons'])):
            failures.append('Custom button order changed in overflow')
        right = 0
        for button in toolbar['buttons'][:count]:
            rect = button['bounds']
            if rect['x'] < right or rect['x'] + rect['width'] > toolbar['width'] or rect['y'] + rect['height'] > toolbar['height']:
                failures.append('A visible custom button overlapped or clipped')
            right = rect['x'] + rect['width']
        if toolbar['overflow']:
            rect = toolbar['moreBounds']
            if rect['x'] < right or rect['x'] + rect['width'] > toolbar['width']:
                failures.append('More overlapped or clipped')
    (folder / 'observations.json').write_text(json.dumps({'profile': args.profile,
        'pid': probe.process.pid, 'states': observations, 'failures': failures}, ensure_ascii=False, indent=2), encoding='utf-8')

try:
    probe.request(['observe_property', 991, 'user-data/modern/window'])
    print(json.dumps({'pid': probe.process.pid, 'config': str(config), 'stop': str(stop)}, ensure_ascii=False), flush=True)
    until = time.monotonic() + args.seconds
    while not stop.exists() and time.monotonic() < until and probe.process.poll() is None:
        raw = probe.get('user-data/modern/window')
        for event in probe.events[event_index:]:
            if event.get('event') == 'property-change' and event.get('name') == 'user-data/modern/window' and event.get('data'):
                inspect(json.loads(event['data']))
        event_index = len(probe.events)
        if raw: inspect(json.loads(raw))
        time.sleep(.08)
    print(json.dumps({'profile': args.profile, 'states': len(observations),
        'resizePhases': sum(s['resizing'] for s in observations), 'failures': sorted(set(failures))}, ensure_ascii=False), flush=True)
    if failures: raise AssertionError(sorted(set(failures)))
finally:
    probe.close()
