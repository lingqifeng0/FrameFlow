"""Reproduce mixed entry points against an isolated real player; no UI injection."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import time
from pipe_probe import ROOT, EXE, Probe

parser=argparse.ArgumentParser()
parser.add_argument('--label',default='v11')
parser.add_argument('--expect-fixed',action='store_true')
args=parser.parse_args()
folder=ROOT/'test-results'/('resume-audit-'+args.label)
folder.mkdir(parents=True,exist_ok=True)
media=ROOT/'test-results/marktext-v11/source/video.mp4'
second=ROOT/'test-results/frame-test.mp4'
results=[]

def record(name,expected,actual,**evidence):
    match=abs(expected-actual)<.2 if isinstance(expected,(int,float)) and isinstance(actual,(int,float)) else expected==actual
    row={'check':name,'expected':expected,'actual':actual,'result':'passed' if match else 'reproduced_failure','evidence':evidence}
    results.append(row)
    print(json.dumps(row,ensure_ascii=False),flush=True)
    (folder/'results.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf-8')

def cfg(name): return folder/name
def stored(config): return json.loads((config/'modern-ui.json').read_text(encoding='utf-8'))
seed={'RememberPosition':True,'Positions':{str(media):1037.29,str(second):3}}

# Two saves use the exact same configuration and the actual GUI/model timers.
c=cfg('stale-different-files')
a=Probe('audit-a-'+args.label,settings=seed,file=media,config=c)
b=None
try:
    a.settle(1037.29,.2)
    b=Probe('audit-b-'+args.label,args=('--process-instance=multi',),file=second,config=c,configure=False)
    b.settle(3,.2)
    a.request(['seek',600.25,'absolute+exact']);a.settle(600.25,.2)
    a.close();a=None
    after_a=stored(c)['Positions'][str(media)]
    b.close();b=None
    after_b=stored(c)['Positions'][str(media)]
    record('closing another video cannot overwrite the first video progress',600.25,after_b,afterFirstExit=after_a)
finally:
    if a:a.close()
    if b:b.close()

c=cfg('stale-same-file')
a=Probe('audit-old-'+args.label,settings=seed,file=media,config=c)
b=None
try:
    a.settle(1037.29,.2)
    a.request(['seek',200,'absolute+exact']);a.settle(200,.2)
    b=Probe('audit-new-'+args.label,args=('--process-instance=multi','--resume-playback=no'),file=media,config=c,configure=False)
    b.settle(0,.2)
    b.request(['seek',461.85,'absolute+exact']);b.settle(461.85,.5)
    b.close();b=None
    after_new=stored(c)['Positions'][str(media)]
    a.close();a=None
    record('old paused window cannot replace the newer timestamp on exit',461.85,stored(c)['Positions'][str(media)],afterNewExit=after_new)
finally:
    if a:a.close()
    if b:b.close()

# Keep the old native watch-later file, then update progress through plugin flags.
c=cfg('native-and-custom')
a=Probe('audit-native-'+args.label,settings=seed,file=media,config=c,
        native_options={'save-position-on-quit':'yes','watch-later-options':'start'})
try:
    a.settle(1037.29,.2)
    a.request(['seek',594.19,'absolute+exact']);a.settle(594.19,.2)
    a.request(['write-watch-later-config'])
finally:a.close()
b=Probe('audit-plugin-'+args.label,file=media,config=c,configure=False,
        args=('--resume-playback=no','--save-position-on-quit=no'))
try:
    b.settle(0,.2)
    b.request(['seek',461.85,'absolute+exact']);b.settle(461.85,.5)
finally:b.close()
custom=stored(c)['Positions'][str(media)]
a=Probe('audit-reopen-'+args.label,file=media,config=c,configure=False)
try:
    a.wait(lambda:a.get('time-pos') is not None,'reopened playback')
    record('ordinary reopen uses latest plugin progress instead of old native resume',461.85,a.get('time-pos'),customSaved=custom)
finally:a.close()

# Never exercise the known unsafe V11 routing algorithm with unrelated processes.
# The secondary program itself performs normal single-instance forwarding.
if not args.expect_fixed:
    guard=subprocess.run(['powershell','-NoProfile','-Command',
        'Get-Process -Name mpvnet -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Path'],
        capture_output=True,text=True)
    if guard.stdout.strip():
        raise RuntimeError('Legacy routing test requires no unrelated mpvnet process: '+guard.stdout)
ca,cb=cfg('route-a'),cfg('route-b')
a=Probe('audit-route-a-'+args.label,settings=seed,config=ca,file=media,instance='single',args=('--process-instance=multi',))
b=None
try:
    a.settle(1037.29,.2)
    b=Probe('audit-route-b-'+args.label,settings=seed,config=cb,file=media,instance='single',args=('--process-instance=multi',))
    b.settle(1037.29,.2)
    env=os.environ.copy();env['MPVNET_HOME']=str(cb)
    command=subprocess.Popen([str(EXE),'--pause=yes',str(second)],env=env)
    command.wait(timeout=20)
    until=time.monotonic()+4
    while time.monotonic()<until and a.get('path')!=str(second) and b.get('path')!=str(second):time.sleep(.03)
    record('single-instance open reaches the matching configuration only',str(second),b.get('path'),
           otherWindow=a.get('path'),otherPid=a.process.pid,targetPid=b.process.pid)
    # Explicit start must survive command-line forwarding too.
    command=subprocess.Popen([str(EXE),'--pause=yes','--start=654.98',str(media)],env=env)
    command.wait(timeout=20)
    b.wait(lambda:b.get('path')==str(media),'forwarded target media')
    time.sleep(.6)
    record('forwarded explicit start has priority over saved resume',654.98,b.get('time-pos'),otherWindow=a.get('path'))
    command=subprocess.Popen([str(EXE),'--pause=yes','--start=0',str(media)],env=env)
    command.wait(timeout=20)
    b.settle(0,.4)
    record('forwarded explicit zero is preserved',0,b.get('time-pos'),otherWindow=a.get('path'))
    for target in (123.45,220.5,461.85):
        command=subprocess.Popen([str(EXE),'--pause=yes','--start='+str(target),str(media)],env=env)
        command.wait(timeout=20)
    b.settle(461.85,.6)
    record('quick consecutive manual opens replace instead of silently queueing',461.85,b.get('time-pos'),playlistCount=b.get('playlist-count'))
finally:
    a.close()
    if b:b.close()

# Same configuration but another executable must receive its own secondary opens.
if args.expect_fixed:
    copy=folder/'other-player'
    shutil.copytree(EXE.parent,copy,dirs_exist_ok=True,ignore=shutil.ignore_patterns('portable_config','*.pdb'))
    other=copy/'FrameFlow.exe'
    c=cfg('route-different-executables')
    a=Probe('audit-exe-a-'+args.label,settings=seed,file=media,config=c,instance='single',args=('--process-instance=multi',))
    b=None
    try:
        a.settle(1037.29,.2)
        b=Probe('audit-exe-b-'+args.label,file=media,config=c,configure=False,executable=other,args=('--process-instance=multi',))
        b.settle(1037.29,.2)
        env=os.environ.copy();env['MPVNET_HOME']=str(c)
        command=subprocess.Popen([str(other),'--pause=yes',str(second)],env=env)
        command.wait(timeout=20)
        b.wait(lambda:b.get('path')==str(second),'matching executable received open')
        record('same-config different executable cannot receive the other player open',str(second),b.get('path'),otherWindow=a.get('path'))
        assert a.get('path')==str(media)
    finally:
        a.close()
        if b:b.close()

if args.expect_fixed and any(row['result']!='passed' for row in results):
    raise AssertionError('Fix did not pass every previously reproduced failure')
print('Audit complete:',len(results),'cases.',flush=True)
