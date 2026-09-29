"""Paints lettering for models (stylize_prop.py --decal projects it onto a hull): python ArtSource/Tools/make_decals.py

Writes PNGs into ArtSource/Meshy/decals: white letters with a dark outline on a transparent background.
Font: Roboto (Apache 2.0, see ArtSource/Fonts/README.txt).
"""
import os

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT = os.path.join(ROOT, 'ArtSource', 'Meshy', 'decals')
CREAM = (250, 246, 236, 255)
NAVY = (29, 43, 58, 255)


def lettering(text, size, height_share=0.6, width_share=0.62):
    img = Image.new('RGBA', size, (0, 0, 0, 0))
    px = int(size[1] * height_share)
    fnt = ImageFont.truetype(os.path.join(ROOT, 'ArtSource', 'Fonts', 'Roboto-Black.ttf'), px)
    while fnt.getlength(text) > size[0] * width_share and px > 10:
        px -= 2
        fnt = ImageFont.truetype(os.path.join(ROOT, 'ArtSource', 'Fonts', 'Roboto-Black.ttf'), px)
    ImageDraw.Draw(img).text((size[0] / 2, size[1] / 2), text, font=fnt, fill=CREAM, anchor='mm',
                             stroke_width=max(3, px // 12), stroke_fill=NAVY)
    return img


def main():
    os.makedirs(OUT, exist_ok=True)
    lettering('LIFEGUARD', (1024, 160)).save(os.path.join(OUT, 'lifeguard.png'))
    print('decals written to', OUT)


if __name__ == '__main__':
    main()
