"""Verify all native EXE icon frames and the WinForms resource match the blue source."""
import base64
import io
import json
from pathlib import Path
import struct
import xml.etree.ElementTree as ET
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
icon = ROOT / 'src/MpvNet.Windows/mpv-icon.ico'
expected = Image.open(icon)
sizes = {(n,n) for n in (16,24,32,48,64,128,256)}
assert expected.ico.sizes() == sizes
resource = ET.parse(ROOT/'src/MpvNet.Windows/WinForms/MainForm.resx').getroot()
value = next(d.findtext('value') for d in resource.findall('data') if d.attrib['name']=='$this.Icon')
assert base64.b64decode(value) == icon.read_bytes(), 'WinForms icon diverges from native EXE icon source'
exe = (ROOT/'dist/FrameFlow/FrameFlow.exe').read_bytes()
def u16(offset): return struct.unpack_from('<H',exe,offset)[0]
def u32(offset): return struct.unpack_from('<I',exe,offset)[0]
nt = u32(0x3c)
assert exe[nt:nt+4] == b'PE\0\0'
optional = nt+24
assert u16(optional)==0x20b
section = optional+u16(nt+20)
sections=[]
for index in range(u16(nt+6)):
    entry=section+index*40
    sections.append((u32(entry+12),max(u32(entry+8),u32(entry+16)),u32(entry+20)))
def offset(rva):
    for address,size,raw in sections:
        if address<=rva<address+size:return raw+rva-address
    raise ValueError(f'Unmapped RVA {rva}')
start=offset(u32(optional+112+16))
def directory(relative):
    pos=start+relative
    result={}
    for index in range(u16(pos+12)+u16(pos+14)):
        entry=pos+16+index*8
        name,target=u32(entry),u32(entry+4)
        if name & 0x80000000:
            text=start+(name & 0x7fffffff)
            name=exe[text+2:text+2+u16(text)*2].decode('utf-16-le')
        if target & 0x80000000:result[name]=directory(target & 0x7fffffff)
        else:
            data=start+target
            raw=offset(u32(data))
            result[name]=exe[raw:raw+u32(data+4)]
    return result
resources=directory(0)
checks=[]
assert 3 in resources and 14 in resources, 'Native icon resource missing'
for group,languages in resources[14].items():
    for language,payload in languages.items():
        assert struct.unpack_from('<HH',payload)==(0,1)
        found=set()
        for index in range(struct.unpack_from('<H',payload,4)[0]):
            width,height,_,_,_,_,length,identifier=struct.unpack_from('<BBBBHHIH',payload,6+index*14)
            size=(width or 256,height or 256)
            image_data=resources[3][identifier][language]
            assert len(image_data)==length
            frame=Image.open(io.BytesIO(image_data)).convert('RGBA')
            master=expected.ico.getimage(size).convert('RGBA')
            assert frame.tobytes()==master.tobytes(), ('Wrong compiled icon pixels',size)
            assert frame.getpixel((0,0))[3]==0
            assert frame.getpixel((size[0]//2,size[1]//2))[:3]==(255,255,255)
            # Tiny supersampled frames can shift a nearby blue pixel by 1–2.
            assert all(abs(a-b)<=3 for a,b in zip(frame.getpixel((size[0]//4,size[1]//2))[:3],(0,150,255)))
            found.add(size)
            checks.append({'group':group,'language':language,'size':size,'bytes':length})
        assert found==sizes, ('Compiled icon sizes missing',found)
import sys
sys.path.insert(0,str(ROOT/'scripts'))
from version import read_version
folder=ROOT/f'test-results/resize-v{read_version()}'
folder.mkdir(parents=True,exist_ok=True)
(folder/'icons-compiled.json').write_text(json.dumps({'color':'#0096FF','nativeFrames':checks,'formsResourceMatches':True},indent=2),encoding='utf8')
print(f'PASS: {len(checks)} compiled native icon sizes match #0096FF blue play pixels and the WinForms resource')
