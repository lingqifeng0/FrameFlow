"""Observe real drags through the isolated player's public IPC; no input injection."""
import argparse
import json
import shutil
from pathlib import Path
import time
from pipe_probe import Probe, ROOT

parser=argparse.ArgumentParser()
parser.add_argument('profile', choices=['icons','labels','reopen','playing'])
parser.add_argument('--version', default='19')
parser.add_argument('--file',type=Path,default=ROOT/'test-results/marktext-v11/source/video.mp4')
args=parser.parse_args()
audit_root=ROOT/('test-results/resize-v'+args.version)
folder=audit_root/args.profile
folder.mkdir(parents=True,exist_ok=True)
stop=folder/'stop'
stop.unlink(missing_ok=True)
config=audit_root/('icons' if args.profile=='reopen' else args.profile)/'config'
if args.profile=='playing':
    # Playback drags must not overwrite the saved dimensions of the earlier
    # icon-only fixture, which release_checks verifies against its recording.
    config.mkdir(parents=True,exist_ok=True)
    for name in ('modern-ui.json','mpv.conf','mpvnet.conf'):
        shutil.copy2(audit_root/'icons/config'/name,config/name)
definitions=[('frame-back','上一帧'),('frame-forward','下一帧'),('screenshot','截图'),('bookmark','书签'),('ab-loop','AB 循环'),('timestamp','复制时间戳'),
             ('zoom-reset','还原画面'),('mute','静音'),('speed-up','增加倍速'),('speed-down','降低倍速'),('speed-reset','恢复倍速'),
             ('sub-toggle','字幕'),('loop-file','单个循环'),('loop-playlist','列表循环'),('media-info','视频信息'),('playlist','播放列表')]
settings=None if args.profile in ('reopen','playing') else {
    'Width':1450,'Height':880,'WindowDpi':168,'AutoFolderPlaylist':False,'PlaylistVisible':False,
    'Speed':1.25,'RememberSpeed':True,'ShowButtonLabels':args.profile=='labels',
    'Toolbar':[{'Id':item[0],'Title':item[1],'Value':0} for item in definitions]}
probe=Probe('resize-v'+args.version+'-'+args.profile,settings=settings,config=config,background=True,
    file=args.file,
    args=['--vo=gpu','--hwdec=no','--force-window=yes','--start=400','--pause='+('no' if args.profile=='playing' else 'yes')])
(folder/'actor.json').write_text(json.dumps({'pid':probe.process.pid,'endpoint':probe.pipe.name,'config':str(config)}),encoding='utf8')
observations=[]
seen=set()
event_index=0
failures=[]
pending=None

def inspect(raw):
    global pending
    if not raw:return
    state=json.loads(raw)
    if 'toolbar' not in state:return
    key=json.dumps(state,sort_keys=True)
    if key in seen:return
    seen.add(key)
    controls=state['controls']
    if state['resizing']:
        if pending is None:pending=state
        if controls['headerColor']!='#000000' or not controls['headerRenderingSuspended']:
            failures.append('Blue or rounded title-bar rendering remained active during resize')
        if controls['headerContentVisible'] or controls['footerContentVisible']:
            failures.append('Toolbar content remained visible during resize')
        if not state['rounded']:
            failures.append('The outer contour lost its rounded corners during resize')
        if controls['headerRegionUpdates']!=pending['controls']['headerRegionUpdates']:
            failures.append('Header clipping was rebuilt during live resize')
        if state['toolbar']['layoutPasses']!=pending['toolbar']['layoutPasses']:
            failures.append('Editable toolbar was repeatedly rearranged during resize')
    elif pending:
        if controls['headerColor']!='#0096FF' or controls['headerRenderingSuspended'] or not state['rounded']:
            failures.append('Blue rounded header was not restored after resize')
        if controls['headerRegionUpdates']!=pending['controls']['headerRegionUpdates']+1:
            failures.append('Header clipping did not restore exactly once')
        if controls['resizePixelDiscards']<=pending['controls']['resizePixelDiscards']:
            failures.append('Native resizing did not discard preserved old pixels')
        if state['toolbar']['layoutPasses']!=pending['toolbar']['layoutPasses']+1:
            failures.append('Final toolbar layout did not happen exactly once')
        for name in ('headerBounds','footerBounds'):
            if controls[name]['height']!=pending['controls'][name]['height']:
                failures.append('Resize changed the reserved bar height')
        pending=None
    if args.profile=='playing':
        state['playback']={'pause':probe.get('pause'),'time':probe.get('time-pos')}
        if state['playback']['pause']:failures.append('Resize paused playback')
    if controls.get('resizeNativeLayoutFailures',0):
        failures.append('Native child coordinates disagreed with their layout during a resize frame')
    if state['mode']=='normal':
        for name in ('headerBounds','footerBounds'):
            rect=controls[name]
            if rect['x']<0 or rect['y']<0 or rect['x']+rect['width']>state['bounds']['width'] or rect['y']+rect['height']>state['bounds']['height']:
                failures.append('A restored bar moved outside the client area')
    if state['resizing'] and controls.get('resizeFrames',0)>pending['controls'].get('resizeFrames',0):
        if controls['resizeRepaints']-pending['controls']['resizeRepaints'] < controls['resizeFrames']-pending['controls']['resizeFrames']:
            failures.append('A resize frame finished without a full native repaint')
        header,footer=controls['headerBounds'],controls['footerBounds']
        if header['x']!=pending['controls']['headerBounds']['x'] or header['y']!=pending['controls']['headerBounds']['y']:
            failures.append('A live title bar lost its reserved top/left position')
        if header['height']!=pending['controls']['headerBounds']['height'] or footer['height']!=pending['controls']['footerBounds']['height']:
            failures.append('A live bar lost its reserved height')
    if not state['resizing'] and state['mode']=='normal':
        toolbar=state['toolbar']
        if toolbar['total']!=16 or toolbar['capacity']!=16:failures.append('Custom slots changed during resize')
        right=0
        for item in toolbar['buttons'][:toolbar['visibleCount']]:
            rect=item['bounds']
            if rect['x']<right or rect['x']+rect['width']>toolbar['width'] or rect['y']+rect['height']>toolbar['height']:
                failures.append('A custom toolbar button overlapped or clipped')
            right=rect['x']+rect['width']
        if toolbar['overflow'] and (toolbar['moreBounds']['x']<right or toolbar['moreBounds']['x']+toolbar['moreBounds']['width']>toolbar['width']):
            failures.append('More overlapped or clipped')
    observations.append(state)
    (folder/'observations.json').write_text(json.dumps({'profile':args.profile,'states':observations,'failures':failures},ensure_ascii=False,indent=2),encoding='utf8')

try:
    probe.request(['observe_property',1881,'user-data/modern/window'])
    print(json.dumps({'pid':probe.process.pid,'profile':args.profile,'stop':str(stop)}),flush=True)
    until=time.monotonic()+1800
    while not stop.exists() and time.monotonic()<until and probe.process.poll() is None:
        try:
            raw=probe.get('user-data/modern/window')
        except OSError:
            # A normal close can race the last IPC read. An exit with a failure
            # code remains an error; closing this test window is not a failure.
            if probe.process.wait(timeout=3)==0:break
            raise
        for event in probe.events[event_index:]:
            if event.get('event')=='property-change' and event.get('name')=='user-data/modern/window':inspect(event.get('data'))
        event_index=len(probe.events)
        inspect(raw)
        time.sleep(.04)
    print(json.dumps({'profile':args.profile,'states':len(observations),'resizePhases':sum(s['resizing'] for s in observations),'failures':sorted(set(failures))}),flush=True)
    assert not failures,failures
finally:
    probe.close()
