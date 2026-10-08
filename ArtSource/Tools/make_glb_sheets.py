"""Make readable 4x4 pages from Blender's GLB review renders."""
import json
from collections import defaultdict
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Screenshots/Review/GLB'
items = json.loads((OUT / 'render_results.json').read_text())
groups = defaultdict(list)
for item in items:
    if 'image' in item:
        groups[Path(item['path']).parent.name].append(item)
font = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 18)
for category, entries in groups.items():
    for page in range(0, len(entries), 16):
        chunk = entries[page:page + 16]
        sheet = Image.new('RGB', (4 * 300, 4 * 340), '#d8e0e5')
        draw = ImageDraw.Draw(sheet)
        for i, item in enumerate(chunk):
            col, row = i % 4, i // 4
            image = Image.open(ROOT / item['image']).convert('RGB')
            image.thumbnail((292, 292))
            x = col * 300 + (300 - image.width) // 2
            y = row * 340
            sheet.paste(image, (x, y))
            name = Path(item['path']).stem
            draw.text((col * 300 + 6, y + 296), name, font=font, fill='#172a39')
            size = ' × '.join(f'{v:g}' for v in item['world_size'])
            draw.text((col * 300 + 6, y + 317), size, font=font, fill='#465866')
        sheet.save(OUT / f'{category}_{page // 16 + 1}.jpg', quality=90)
print('sheets', len(list(OUT.glob('*_*.jpg'))))
