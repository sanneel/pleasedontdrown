"""Paints the in-world signs (Meshy can't write text, TextMesh looks cheap): python ArtSource/Tools/make_signs.py

Writes PNGs into Assets/_Game/Art/Signs. The scene builder puts them on the sign boards.
Font: Roboto (Apache 2.0, see ArtSource/Fonts/README.txt).
"""
import math
import os
import random

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
FONTS = os.path.join(ROOT, 'ArtSource', 'Fonts')
OUT = os.path.join(ROOT, 'Assets', '_Game', 'Art', 'Signs')

CREAM = (255, 244, 214)
NAVY = (29, 43, 58)
WOOD_DARK = (92, 58, 34)
WOOD_LIGHT = (128, 84, 50)
TEAL = (38, 170, 168)
RED = (226, 59, 46)


def font(name, size):
    return ImageFont.truetype(os.path.join(FONTS, name), size)


def noise(size, seed, scale, low, high, stretch=1, blur=0):
    """Soft value noise in [low, high]: chalk dust, or brush strokes when stretched sideways and blurred."""
    rng = random.Random(seed)
    w, h = max(2, size[0] // (scale * stretch)), max(2, size[1] // scale)
    small = Image.new('L', (w, h))
    small.putdata([rng.randint(low, high) for _ in range(w * h)])
    big = small.resize(size, Image.BICUBIC)
    return big.filter(ImageFilter.GaussianBlur(blur)) if blur else big


def planks(size, seed, base, grain):
    """Horizontal wooden planks with grain lines and dark gaps."""
    img = Image.new('RGB', size, base)
    d = ImageDraw.Draw(img)
    rng = random.Random(seed)
    plank = size[1] // 5 if size[1] > size[0] else max(40, size[1] // 4)
    for y in range(0, size[1], plank):
        shade = rng.randint(-10, 10)
        d.rectangle([0, y, size[0], y + plank], fill=tuple(max(0, min(255, c + shade)) for c in base))
        for _ in range(6):
            gy = y + rng.randint(4, plank - 4)
            d.line([(0, gy), (size[0], gy + rng.randint(-6, 6))], fill=grain, width=2)
        d.line([(0, y), (size[0], y)], fill=tuple(c // 2 for c in base), width=4)
    return img


def rounded_mask(size, inset, radius):
    mask = Image.new('L', size, 0)
    ImageDraw.Draw(mask).rounded_rectangle([inset, inset, size[0] - inset, size[1] - inset], radius, fill=255)
    return mask


def outlined_text(canvas, xy, text, fnt, fill, outline, stroke, shadow=None, anchor='mm', angle=0.0):
    """Big sign lettering: drop shadow, thick outline, fill. Drawn on a layer so it can be rotated."""
    w, h = canvas.size
    layer = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    if shadow:
        d.text((xy[0] + shadow[0], xy[1] + shadow[1]), text, font=fnt, fill=(0, 0, 0, 110), anchor=anchor,
               stroke_width=stroke, stroke_fill=(0, 0, 0, 110))
    d.text(xy, text, font=fnt, fill=fill, anchor=anchor, stroke_width=stroke, stroke_fill=outline)
    if angle:
        layer = layer.rotate(angle, resample=Image.BICUBIC, center=xy)
    canvas.alpha_composite(layer)


def arched(canvas, center_x, base_y, text, fnt, bend, **style):
    """Letters along a gentle upward arch (a painted shop sign)."""
    widths = [fnt.getlength(c) for c in text]
    spacing = fnt.size * 0.02
    total = sum(widths) + spacing * (len(text) - 1)
    x = center_x - total / 2
    for c, cw in zip(text, widths):
        cx = x + cw / 2
        t = (cx - center_x) / (total / 2)  # -1..1
        y = base_y + bend * t * t
        angle = -math.degrees(math.atan(2 * bend * t / (total / 2)))
        if c != ' ':
            outlined_text(canvas, (cx, y), c, fnt, angle=angle, **style)
        x += cw + spacing


def life_ring(canvas, center, radius):
    d = ImageDraw.Draw(canvas)
    cx, cy = center
    r0, r1 = radius, radius * 0.55
    d.ellipse([cx - r0 - 8, cy - r0 - 8, cx + r0 + 8, cy + r0 + 8], fill=NAVY)
    d.ellipse([cx - r0, cy - r0, cx + r0, cy + r0], fill=RED)
    for a in (45, 135, 225, 315):  # four white bands
        band = Image.new('RGBA', canvas.size, (0, 0, 0, 0))
        bd = ImageDraw.Draw(band)
        bd.pieslice([cx - r0, cy - r0, cx + r0, cy + r0], a - 11, a + 11, fill=CREAM)
        canvas.alpha_composite(band)
    d.ellipse([cx - r1 - 8, cy - r1 - 8, cx + r1 + 8, cy + r1 + 8], fill=NAVY)
    d.ellipse([cx - r1, cy - r1, cx + r1, cy + r1], fill=TEAL)
    d.arc([cx - r0 * 0.8, cy - r0 * 0.8, cx + r0 * 0.8, cy + r0 * 0.8], 200, 250, fill=(255, 255, 255), width=10)


def lost_and_found_sign():
    size = (2048, 640)
    img = planks(size, 7, WOOD_DARK, WOOD_LIGHT).convert('RGBA')
    # Painted teal face with brush-stroke variation, inside a wooden frame.
    face = Image.new('RGBA', size, TEAL + (255,))
    strokes = ImageChops.multiply(noise(size, 3, 10, 215, 255, stretch=12, blur=4), noise(size, 4, 60, 225, 255, blur=20))
    face = Image.merge('RGBA', [ImageChops.multiply(ch, strokes) for ch in face.split()[:3]] + [face.split()[3]])
    img.paste(face, (0, 0), rounded_mask(size, 44, 36))
    ImageDraw.Draw(img).rounded_rectangle([60, 60, size[0] - 60, size[1] - 60], 26, outline=CREAM, width=8)
    for x in (110, size[0] - 110):  # nail heads in the frame corners
        for y in (24, size[1] - 24):
            ImageDraw.Draw(img).ellipse([x - 9, y - 9, x + 9, y + 9], fill=(60, 60, 64))
    life_ring(img, (175, 320), 92)
    life_ring(img, (size[0] - 175, 320), 92)
    fnt = font('Roboto-Black.ttf', 186)
    arched(img, size[0] / 2, 318, 'LOST & FOUND', fnt, bend=30,
           fill=CREAM, outline=NAVY, stroke=14, shadow=(10, 12))
    return img.convert('RGB')


def chalk(layer_mask, seed):
    """Erodes lettering so it reads as chalk."""
    grain = noise(layer_mask.size, seed, 2, 0, 255).point(lambda v: 255 if v > 70 else 0)
    return ImageChops.multiply(layer_mask, grain).filter(ImageFilter.GaussianBlur(0.8))


def prices_board():
    size = (1024, 640)
    img = planks(size, 11, WOOD_DARK, WOOD_LIGHT)
    board = Image.new('RGB', size, (34, 53, 44))
    smudge = noise(size, 5, 40, 225, 255, blur=16)
    board = Image.merge('RGB', [ImageChops.multiply(ch, smudge) for ch in board.split()])
    img.paste(board, (0, 0), rounded_mask(size, 34, 18))

    text = Image.new('L', size, 0)
    d = ImageDraw.Draw(text)
    d.text((size[0] / 2, 118), 'WE PAY', font=font('Roboto-Black.ttf', 112), fill=255, anchor='mm')
    d.line([(300, 182), (724, 182)], fill=255, width=6)
    rows = [('Wallet', '$30'), ('Phone', '$40'), ('Watch', '$35'), ('Sunglasses', '$20')]
    row_font = font('Roboto-Bold.ttf', 66)
    for i, (item, price) in enumerate(rows):
        y = 262 + i * 88
        d.text((96, y), item, font=row_font, fill=255, anchor='lm')
        d.text((size[0] - 96, y), price, font=row_font, fill=255, anchor='rm')
        start = 96 + row_font.getlength(item) + 24
        end = size[0] - 96 - row_font.getlength(price) - 24
        x = start
        while x < end:  # dotted leader
            d.ellipse([x - 4, y + 14, x + 4, y + 22], fill=255)
            x += 26
    mask = chalk(text, 9)
    header = Image.new('L', size, 0)
    ImageDraw.Draw(header).rectangle([0, 0, size[0], 200], fill=255)
    yellow = Image.new('RGB', size, (255, 226, 122))
    white = Image.new('RGB', size, (244, 244, 234))
    img.paste(white, (0, 0), ImageChops.multiply(mask, ImageChops.invert(header)))
    img.paste(yellow, (0, 0), ImageChops.multiply(mask, header))
    return img


def main():
    os.makedirs(OUT, exist_ok=True)
    lost_and_found_sign().save(os.path.join(OUT, 'lost_and_found_sign.png'), optimize=True)
    prices_board().save(os.path.join(OUT, 'lost_and_found_prices.png'), optimize=True)
    print('signs written to', OUT)


if __name__ == '__main__':
    main()
