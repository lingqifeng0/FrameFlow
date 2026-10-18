"""Build the approved #0096FF play icon and synchronize native/managed resources."""
from pathlib import Path
import base64
import importlib.util
import re

ROOT = Path(__file__).resolve().parents[1]
proposal = ROOT / 'previews/blue-play-proposal/render.py'
spec = importlib.util.spec_from_file_location('blue_play_source', proposal)
source = importlib.util.module_from_spec(spec)
spec.loader.exec_module(source)
sizes = (16, 24, 32, 48, 64, 128, 256)
frames = [source.icon(size) for size in sizes]
icon = ROOT / 'src/MpvNet.Windows/mpv-icon.ico'
# Provide each size explicitly so tiny play glyphs use their own supersampling.
frames[-1].save(icon, format='ICO', sizes=[(n, n) for n in sizes], append_images=frames[:-1])
resource = ROOT / 'src/MpvNet.Windows/WinForms/MainForm.resx'
encoded = base64.b64encode(icon.read_bytes()).decode('ascii')
lines = '\n'.join('        ' + encoded[i:i+80] for i in range(0, len(encoded), 80))
text = resource.read_text(encoding='utf-8-sig')
text, count = re.subn(r'(<data name="\$this.Icon"[^>]*>\s*<value>)[\s\S]*?(</value>)',
                     lambda m: m[1] + '\n' + lines + '\n    ' + m[2], text, count=1)
if count != 1:
    raise ValueError('MainForm icon resource was not found')
resource.write_text(text, encoding='utf-8', newline='\n')
print(f'Synchronized {icon.name}: {len(icon.read_bytes())} bytes, sizes {sizes}')
