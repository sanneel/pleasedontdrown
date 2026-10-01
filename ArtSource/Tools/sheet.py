"""Joins preview pictures side by side: python ArtSource/Tools/sheet.py <out.png> <in1.png> <in2.png> ..."""
import sys

from PIL import Image

images = [Image.open(p).convert('RGB') for p in sys.argv[2:]]
sheet = Image.new('RGB', (sum(i.width for i in images), max(i.height for i in images)), (140, 150, 165))
x = 0
for i in images:
    sheet.paste(i, (x, 0))
    x += i.width
sheet.save(sys.argv[1])
