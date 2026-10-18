"""Read the supplied bug recording; save sampled frames for visual inspection."""
import json
from pathlib import Path
import sys
import cv2
import numpy as np

source=Path(sys.argv[1])
output=Path(__file__).resolve().parents[1]/'test-results/resize-v22/recording'
output.mkdir(parents=True,exist_ok=True)
capture=cv2.VideoCapture(str(source))
fps=capture.get(cv2.CAP_PROP_FPS)
count=int(capture.get(cv2.CAP_PROP_FRAME_COUNT))
duration=count/fps
info={'source':str(source),'fps':fps,'frames':count,'duration':duration,
      'width':capture.get(cv2.CAP_PROP_FRAME_WIDTH),'height':capture.get(cv2.CAP_PROP_FRAME_HEIGHT)}
print(json.dumps(info,ensure_ascii=False))
(output/'info.json').write_text(json.dumps(info,ensure_ascii=False,indent=2),encoding='utf8')
frames=[]
for time in np.arange(0,duration,.75):
    capture.set(cv2.CAP_PROP_POS_MSEC,time*1000)
    valid,frame=capture.read()
    if not valid:continue
    name=output/f'{time:06.2f}.png'
    cv2.imwrite(str(name),frame)
    thumb=cv2.resize(frame,(640,360))
    cv2.rectangle(thumb,(0,0),(130,28),(0,0,0),-1)
    cv2.putText(thumb,f'{time:.2f}s',(8,21),cv2.FONT_HERSHEY_SIMPLEX,.6,(255,255,255),1,cv2.LINE_AA)
    frames.append(thumb)
for index in range(0,len(frames),8):
    sheet=np.zeros((4*360,2*640,3),np.uint8)
    for offset,thumb in enumerate(frames[index:index+8]):
        row,col=divmod(offset,2)
        sheet[row*360:(row+1)*360,col*640:(col+1)*640]=thumb
    cv2.imwrite(str(output/f'sheet-{index//8}.jpg'),sheet)
capture.release()
print(f'Saved {len(frames)} frames and {(len(frames)+7)//8} sheets to {output}')
