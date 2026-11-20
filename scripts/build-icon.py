"""Build the approved #0096FF play icon and synchronize native/managed resources."""
from pathlib import Path
import base64
import re

from PIL import Image, ImageDraw


def draw_icon(size):
    scale = max(4, 1024 // size)
    image = Image.new('RGBA', (size * scale, size * scale), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    factor = image.width / 256
    draw.ellipse((12 * factor, 12 * factor, 244 * factor, 244 * factor), fill='#0096FF')
    points = [(104, 77), (179, 121)]

    def add_curve(start, control, end):
        for index in range(1, 49):
            t = index / 48
            points.append((
                (1 - t) ** 2 * start[0] + 2 * (1 - t) * t * control[0] + t * t * end[0],
                (1 - t) ** 2 * start[1] + 2 * (1 - t) * t * control[1] + t * t * end[1],
            ))

    add_curve((179, 121), (190, 128), (179, 135))
    points.append((104, 179))
    add_curve((104, 179), (96, 184), (96, 174))
    points.append((96, 82))
    add_curve((96, 82), (96, 72), (104, 77))
    draw.polygon([(x * factor, y * factor) for x, y in points], fill='white')
    return image.resize((size, size), Image.Resampling.LANCZOS)


ROOT = Path(__file__).resolve().parents[1]
sizes = (16, 24, 32, 48, 64, 128, 256)
frames = [draw_icon(size) for size in sizes]
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
