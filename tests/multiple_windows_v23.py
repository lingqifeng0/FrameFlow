"""Two ordinary player launches using multi in the same isolated config."""
import json
import time
from pipe_probe import ROOT, Probe

folder=ROOT/'test-results/multiple-v23'
config=folder/'config'
folder.mkdir(parents=True,exist_ok=True)
a=b=None
try:
    a=Probe('multiple-a-v23',settings={'Width':1000,'Height':700,'WindowDpi':168,'AutoFolderPlaylist':False},
        file=ROOT/'test-results/frame-test.mp4',config=config,instance='multi',background=True,
        args=['--vo=gpu','--hwdec=auto-safe','--force-window=yes'])
    a.wait(lambda:a.get('video-params') is not None,'first video loaded')
    a.request(['set_property','pause',False])
    first_time=a.get('time-pos')
    b=Probe('multiple-b-v23',file=ROOT/'test-results/frame-test-2.mp4',config=config,configure=False,background=True,
        args=['--vo=gpu','--hwdec=auto-safe','--force-window=yes'])
    b.wait(lambda:b.get('video-params') is not None,'second video loaded')
    b.request(['set_property','pause',False])
    second_time=b.get('time-pos')
    time.sleep(1)
    evidence={'configuration':(config/'mpvnet.conf').read_text(encoding='utf8'),
        'first':{'pid':a.process.pid,'file':a.get('path'),'before':first_time,'after':a.get('time-pos'),'playing':not a.get('pause'),'vo':a.get('current-vo')},
        'second':{'pid':b.process.pid,'file':b.get('path'),'before':second_time,'after':b.get('time-pos'),'playing':not b.get('pause'),'vo':b.get('current-vo')}}
    assert a.process.poll() is None and b.process.poll() is None
    assert evidence['first']['file']!=evidence['second']['file']
    assert all(evidence[name]['playing'] and evidence[name]['vo']=='gpu' and evidence[name]['after']>evidence[name]['before']+.5 for name in ['first','second'])
    (folder/'results.json').write_text(json.dumps(evidence,ensure_ascii=False,indent=2),encoding='utf8')
    print('PASS: two ordinary launches, same multi config, distinct live GPU video windows, both videos continue playing',flush=True)
finally:
    if b:b.close()
    if a:a.close()
