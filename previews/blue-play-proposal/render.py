"""Draw this proposal from its geometric source, without editing product assets."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

OUT = Path(__file__).resolve().parent
BLUE = '#0096FF'
TEXT = '#EAF1FA'
MUTED = '#A2B0C2'
BG = '#101319'
PANEL = '#1B2029'

def font(size, bold=False):
    return ImageFont.truetype('C:/Windows/Fonts/msyhbd.ttc' if bold else 'C:/Windows/Fonts/msyh.ttc', size)

def icon(size):
    scale = max(4, 1024 // size)
    im = Image.new('RGBA', (size*scale, size*scale), (0,0,0,0))
    d = ImageDraw.Draw(im)
    k = im.width / 256
    d.ellipse((12*k,12*k,244*k,244*k),fill=BLUE)
    pts=[(104,77),(179,121)]
    def quad(start, control, end):
        for i in range(1,49):
            t=i/48
            pts.append(((1-t)**2*start[0]+2*(1-t)*t*control[0]+t*t*end[0],
                        (1-t)**2*start[1]+2*(1-t)*t*control[1]+t*t*end[1]))
    quad((179,121),(190,128),(179,135))
    pts.append((104,179))
    quad((104,179),(96,184),(96,174))
    pts.append((96,82))
    quad((96,82),(96,72),(104,77))
    d.polygon([(x*k,y*k) for x,y in pts],fill='white')
    return im.resize((size,size),Image.Resampling.LANCZOS)

for size in (16,24,32,48,64,128,256):
    icon(size).save(OUT/f'icon-{size}.png')

board=Image.new('RGB',(900,970),BG)
d=ImageDraw.Draw(board)
def text(x,y,value,size=22,color=TEXT,bold=False):
    d.text((x,y),value,font=font(size,bold),fill=color)
def paste(size,x,y):
    board.paste(icon(size),(x,y),icon(size))

text(36,24,'蓝色播放图标 · 方案预览',30,bold=True)
text(36,69,'圆形蓝底  +  白色播放三角',20,MUTED)
paste(164,66,121)
text(280,135,'#0096FF',34,BLUE,True)
text(280,191,'透明外缘，简洁平面样式',24)
text(280,233,'与播放器顶部蓝色保持一致',20,MUTED)
d.line((36,322,864,322),fill='#313946',width=1)

text(36,342,'小尺寸预览',24,bold=True)
text(36,380,'以下按标注像素尺寸显示',18,MUTED)
for size,x in zip((16,24,32,48,64),(90,215,350,500,665)):
    paste(size,x+(64-size)//2,420+(64-size)//2)
    text(x+6,499,f'{size}px',18,MUTED)

text(36,545,'设置窗口中的效果',24,bold=True)
d.rounded_rectangle((36,586,864,682),radius=10,fill=PANEL)
paste(24,55,598)
text(92,596,'设置',21)
d.line((36,636,864,636),fill='#313946',width=1)
text(56,647,'窗口标题栏、任务栏使用同一图标',18,MUTED)
d.line((789,611,801,611),fill=MUTED,width=1)
d.rectangle((819,605,830,616),outline=MUTED,width=1)
d.line((846,605,857,616),fill=MUTED,width=1)
d.line((846,616,857,605),fill=MUTED,width=1)

text(36,710,'程序文件与视频缩略图角标',24,bold=True)
d.rounded_rectangle((36,754,434,932),radius=10,fill=PANEL)
paste(32,60,786)
text(108,786,'mpvnet.exe',23)
text(59,850,'程序文件 / 快捷方式',19,MUTED)

d.rounded_rectangle((458,754,864,932),radius=10,fill=PANEL)
d.rectangle((483,779,620,864),fill='#10151D')
d.rectangle((495,791,608,852),fill='#2A3A50')
d.line((507,805,580,805),fill='#718CA8',width=3)
d.line((507,818,591,818),fill='#4A637E',width=3)
d.line((507,831,562,831),fill='#4A637E',width=3)
for y in (786,803,820,837,854):
    d.rounded_rectangle((485,y,491,y+8),radius=1,fill='#D9E2ED')
    d.rounded_rectangle((612,y,618,y+8),radius=1,fill='#D9E2ED')
paste(32,601,840)
text(651,800,'video.mp4',21)
text(483,891,'缩略图右下角',19,MUTED)
board.save(OUT/'blue-play-preview.png')
print(OUT/'blue-play-preview.png')
