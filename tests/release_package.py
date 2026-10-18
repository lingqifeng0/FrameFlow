"""Validate release roots, dependencies, privacy exclusions, source and hashes."""
from pathlib import Path
import hashlib
import json
import sys
import zipfile

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'scripts'))
from version import read_version

version=read_version()
dist=ROOT/'dist'
runtime=dist/'FrameFlow'
binary=dist/f'FrameFlow-V{version}-win-x64.zip'
source=dist/f'FrameFlow-V{version}-source.zip'
checks=[]
with zipfile.ZipFile(binary) as archive:
    assert archive.testzip() is None
    names=archive.namelist()
    assert {n.split('/')[0] for n in names}=={'FrameFlow'}
    for name in ('FrameFlow.exe','libmpv-2.dll','MediaInfo.dll','License.txt','使用说明.md','第三方组件.md','开源声明.md'):
        payload=archive.read('FrameFlow/'+name)
        assert payload==(runtime/name).read_bytes(),name
    for name in ('mpv-Copyright.txt','mpv-GPL-2.txt','FFmpeg-LICENSE.md','FFmpeg-GPL-2.txt',
                 'MediaInfo-LICENSE.html','HandyControl-LICENSE.txt','NGettext.Wpf-LICENSE.txt','GPL-3.txt'):
        assert archive.read('FrameFlow/licenses/'+name)==(ROOT/'docs/licenses'/name).read_bytes(),name
    configs=[n for n in names if '/portable_config/' in n]
    assert sorted(configs)==['FrameFlow/portable_config/mpv.conf']
    assert f'V{version}' in archive.read('FrameFlow/使用说明.md').decode('utf-8-sig')
    assert not any(n.endswith('.pdb') for n in names)
    checks.append({'package':'runtime','files':len(names),'bytes':binary.stat().st_size,'privateConfigs':False})
with zipfile.ZipFile(source) as archive:
    assert archive.testzip() is None
    names=archive.namelist()
    assert {n.split('/')[0] for n in names}=={'FrameFlow-source'}
    required=('License.txt','src/MpvNet.Windows/Modern/WindowResizeRendering.cs',
              'src/MpvNet.Windows/FileAssociation.cs','src/MpvNet.Windows/mpv-icon.ico',
              'src/MpvNet.Windows/WinForms/MainForm.resx','scripts/build-icon.py',
              'tests/MpvNet.Resize.Integration/Program.cs','src/NGettext.Wpf/CompositionRoot.cs',
              'docs/open-source-notices.md',f'docs/resize-audit-V{version}.md')
    for name in required:
        assert archive.read('FrameFlow-source/'+name)==(ROOT/name).read_bytes(),name
    assert not any('/test-results/' in n or '/.tools/' in n for n in names)
    checks.append({'package':'source','files':len(names),'bytes':source.stat().st_size})
hashes=[]
for line in (dist/f'FrameFlow-V{version}-SHA256.txt').read_text(encoding='utf8').splitlines():
    expected,name=line.split('  ',1)
    file=dist/name
    with file.open('rb') as stream:actual=hashlib.file_digest(stream,'sha256').hexdigest()
    assert actual==expected,name
    hashes.append({'file':name,'sha256':actual})
assert len(hashes)==3
output=ROOT/f'test-results/resize-v{version}/package-check.json'
output.parent.mkdir(parents=True,exist_ok=True)
output.write_text(json.dumps({'version':version,'checks':checks,'hashes':hashes},ensure_ascii=False,indent=2),encoding='utf8')
print(f'PASS: V{version} runtime/source CRC, dependencies, FrameFlow names and open-source notices, private-data exclusions and 3 SHA256 hashes')
