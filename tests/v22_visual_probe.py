"""Isolated V22 appearance probe; Windows UI input is performed through sky.

Records public IPC only. Private frame/config artifacts remain in test-results.
"""
import json
from pathlib import Path
import time
from pipe_probe import Probe, ROOT

folder = ROOT / 'test-results/appearance-v22/default'
folder.mkdir(parents=True, exist_ok=True)
stop = folder / 'stop'
stop.unlink(missing_ok=True)
media = Path('E:/Jameson/code/SmartScreenT113/trent硬件基础/01-电阻与电容/硬件工程师培训05-电阻器件的选型_ev.mp4')
settings = {'Width':1400, 'Height':900, 'WindowDpi':168, 'AutoFolderPlaylist':False,
            'PlaylistVisible':False, 'RememberSpeed':True, 'Speed':1.25,
            'ArrowNavigationSpeed':4, 'ArrowDefaultSpeed':4,
            'TapCalculationMilliseconds':100, 'TapDefaultMilliseconds':30,
            'TemporaryFastForwardSpeed':3}
probe = Probe('appearance-v22-default', settings=settings, config=folder/'config',
              background=True, file=media, args=['--vo=gpu','--hwdec=auto-safe','--force-window=yes','--start=0'])
(folder/'actor.json').write_text(json.dumps({'pid':probe.process.pid,'endpoint':probe.pipe.name,'config':str(probe.config)}),encoding='utf8')
records, seen, failures = [], set(), []
try:
    print(json.dumps({'pid':probe.process.pid,'stop':str(stop)}), flush=True)
    probe.wait(lambda:probe.get('video-params') is not None, 'video loaded')
    while not stop.exists() and probe.process.poll() is None:
        raw = probe.get('user-data/modern/window')
        if raw and raw not in seen:
            seen.add(raw); state=json.loads(raw); controls=state['controls']
            if state['mode']=='normal' and not state['rounded']: failures.append('outer corners lost rounding')
            if state['resizing'] and (not controls['previewVisible'] or not controls['previewHasFrame'] or
                controls['videoWidth']!=controls['frozenVideoWidth'] or controls['videoHeight']!=controls['frozenVideoHeight']):
                failures.append('live resize lost its complete frozen preview')
            ids = [item['id'] for item in state['toolbar']['buttons']]
            if ids != ['frame-back','frame-forward','screenshot','bookmark','ab-loop','timestamp']:
                failures.append('normal toolbar has changed or duplicate tools')
            if controls['previewFailures'] or controls['previewRevealTimeouts']: failures.append('capture or final render timeout')
            records.append(state)
            (folder/'observations.json').write_text(json.dumps({'states':records,'failures':failures},ensure_ascii=False,indent=2),encoding='utf8')
        time.sleep(.04)
    print(json.dumps({'states':len(records),'liveResizeStates':sum(s['resizing'] for s in records),'failures':failures}),flush=True)
    assert not failures, failures
finally:
    probe.close()
