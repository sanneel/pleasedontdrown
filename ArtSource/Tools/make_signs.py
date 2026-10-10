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


def bar_menu():
    """The hotel island bar's chalkboard: what you can order sitting at a stool, and for how much."""
    size = (1024, 768)
    img = planks(size, 13, WOOD_DARK, WOOD_LIGHT)
    board = Image.new('RGB', size, (34, 53, 44))
    smudge = noise(size, 7, 40, 225, 255, blur=16)
    board = Image.merge('RGB', [ImageChops.multiply(ch, smudge) for ch in board.split()])
    img.paste(board, (0, 0), rounded_mask(size, 34, 18))

    text = Image.new('L', size, 0)
    d = ImageDraw.Draw(text)
    d.text((size[0] / 2, 118), 'MENU', font=font('Roboto-Black.ttf', 120), fill=255, anchor='mm')
    d.line([(330, 186), (694, 186)], fill=255, width=6)
    rows = [('1  Fries', '$3'), ('2  Cola', '$2'), ('3  Beer', '$4'), ('4  Cocktail', '$8')]
    row_font = font('Roboto-Bold.ttf', 66)
    for i, (item, price) in enumerate(rows):
        y = 262 + i * 92
        d.text((96, y), item, font=row_font, fill=255, anchor='lm')
        d.text((size[0] - 96, y), price, font=row_font, fill=255, anchor='rm')
        x, end = 96 + row_font.getlength(item) + 24, size[0] - 96 - row_font.getlength(price) - 24
        while x < end:  # dotted leader
            d.ellipse([x - 4, y + 14, x + 4, y + 22], fill=255)
            x += 26
    d.text((size[0] / 2, 676), 'Take a stool to order', font=font('Roboto-Bold.ttf', 44), fill=255, anchor='mm')
    mask = chalk(text, 13)
    header = Image.new('L', size, 0)
    ImageDraw.Draw(header).rectangle([0, 0, size[0], 200], fill=255)
    footer = Image.new('L', size, 0)
    ImageDraw.Draw(footer).rectangle([0, 630, size[0], size[1]], fill=255)
    yellow = Image.new('RGB', size, (255, 226, 122))
    white = Image.new('RGB', size, (244, 244, 234))
    pink = Image.new('RGB', size, (255, 150, 170))
    img.paste(white, (0, 0), ImageChops.multiply(mask, ImageChops.invert(ImageChops.lighter(header, footer))))
    img.paste(yellow, (0, 0), ImageChops.multiply(mask, header))
    img.paste(pink, (0, 0), ImageChops.multiply(mask, footer))
    return img


PURPLE = (108, 78, 190)
SAND = (243, 228, 190)


def board_sign(size, face_color, title, title_size, subtitle=None, seed=21, text_fill=CREAM, outline=NAVY):
    """A painted board in a wooden frame with one big line (and an optional smaller one under it)."""
    img = planks(size, seed, WOOD_DARK, WOOD_LIGHT).convert('RGBA')
    face = Image.new('RGBA', size, face_color + (255,))
    strokes = ImageChops.multiply(noise(size, seed + 1, 10, 215, 255, stretch=12, blur=4), noise(size, seed + 2, 60, 225, 255, blur=20))
    face = Image.merge('RGBA', [ImageChops.multiply(ch, strokes) for ch in face.split()[:3]] + [face.split()[3]])
    inset = max(16, size[1] // 14)
    img.paste(face, (0, 0), rounded_mask(size, inset, inset * 0.8))
    ImageDraw.Draw(img).rounded_rectangle([inset * 1.35, inset * 1.35, size[0] - inset * 1.35, size[1] - inset * 1.35],
                                          inset * 0.6, outline=text_fill, width=max(3, inset // 6))
    while title_size > 20 and font('Roboto-Black.ttf', title_size).getlength(title) > size[0] * 0.82:
        title_size -= 4  # shrink long lines to fit the board
    cy = size[1] / 2 - (title_size * 0.28 if subtitle else 0)
    outlined_text(img, (size[0] / 2, cy), title, font('Roboto-Black.ttf', title_size), fill=text_fill, outline=outline,
                  stroke=max(4, title_size // 14), shadow=(title_size // 22, title_size // 18))
    if subtitle:
        outlined_text(img, (size[0] / 2, cy + title_size * 0.78), subtitle, font('Roboto-Bold.ttf', int(title_size * 0.34)),
                      fill=text_fill, outline=outline, stroke=max(2, title_size // 40))
    return img.convert('RGB')


def dev_signs():
    """The dev island: its name board, the zone boards, the range's distance plates, the teleport pads."""
    yield 'dev_island', board_sign((2048, 640), PURPLE, 'DEV ISLAND', 250, 'guns  ·  shooting range  ·  rescue tests  ·  models', seed=31)
    for name, text, colour in (('dev_armory', 'ARMORY', TEAL), ('dev_range', 'SHOOTING RANGE', TEAL),
                               ('dev_gallery', 'MODEL GALLERY', TEAL), ('dev_rescue', 'RESCUE TESTS', RED),
                               ('dev_items', 'ITEMS', TEAL), ('dev_to_island', 'TO DEV ISLAND', PURPLE),
                               ('dev_to_beach', 'BACK TO THE BEACH', PURPLE), ('dev_travel', 'TRAVEL', PURPLE)):
        yield name, board_sign((1024, 320), colour, text, 150 if len(text) < 10 else 104, seed=40 + len(name))
    for metres in (10, 25, 50, 100, 150):
        yield f'dev_range_{metres}', board_sign((512, 256), SAND, f'{metres} m', 130, seed=60 + metres, text_fill=NAVY, outline=CREAM)


def painted_face(size, colour, seed):
    """A flat painted board (no frame: the model has one) with brush-stroke variation."""
    face = Image.new('RGBA', size, colour + (255,))
    strokes = ImageChops.multiply(noise(size, seed, 10, 215, 255, stretch=12, blur=4), noise(size, seed + 1, 60, 225, 255, blur=20))
    return Image.merge('RGBA', [ImageChops.multiply(ch, strokes) for ch in face.split()[:3]] + [face.split()[3]])


def welcome_sign():
    """The station's sign by the path: the game's name, what the place is, a wave along the bottom."""
    size = (1840, 840)
    img = painted_face(size, CREAM, 81)
    d = ImageDraw.Draw(img)
    # A band of sea along the bottom, with a second lighter wave behind it.
    for colour, base, amp, phase in (((120, 205, 205), 668, 16, 0.0), (TEAL, 700, 20, 1.3)):
        pts = [(x, base + amp * math.sin(x / 70.0 + phase)) for x in range(0, size[0] + 20, 20)]
        d.polygon(pts + [(size[0], size[1]), (0, size[1])], fill=colour)
    life_ring(img, (190, 300), 120)
    life_ring(img, (size[0] - 190, 300), 120)
    outlined_text(img, (size[0] / 2, 190), "PLEASE DON'T", font('Roboto-Black.ttf', 190), fill=NAVY, outline=CREAM, stroke=6)
    outlined_text(img, (size[0] / 2, 390), 'DROWN', font('Roboto-Black.ttf', 250), fill=RED, outline=NAVY, stroke=12, shadow=(10, 12))
    outlined_text(img, (size[0] / 2, 568), 'LIFEGUARD STATION', font('Roboto-Bold.ttf', 84), fill=NAVY, outline=CREAM, stroke=4)
    outlined_text(img, (size[0] / 2, 770), 'est. yesterday', font('Roboto-Bold.ttf', 56), fill=CREAM, outline=NAVY, stroke=4)
    return img.convert('RGB')


def drill_board():
    size = (1500, 800)
    img = painted_face(size, RED, 91)
    ImageDraw.Draw(img).rounded_rectangle([40, 40, size[0] - 40, size[1] - 40], 30, outline=CREAM, width=10)
    life_ring(img, (size[0] / 2, 190), 105)
    outlined_text(img, (size[0] / 2, 440), 'RESCUE DRILL', font('Roboto-Black.ttf', 190), fill=CREAM, outline=NAVY, stroke=12, shadow=(8, 10))
    outlined_text(img, (size[0] / 2, 620), 'throws a tourist in the sea', font('Roboto-Bold.ttf', 78), fill=CREAM, outline=NAVY, stroke=5)
    return img.convert('RGB')


def roof_sign():
    """Hand-painted on bare planks: the big word neat, the small one added later by someone honest."""
    size = (2400, 500)
    img = planks(size, 51, (196, 152, 98), (160, 118, 72)).convert('RGBA')
    outlined_text(img, (size[0] * 0.4, 250), 'LIFEGUARD', font('Roboto-Black.ttf', 330), fill=RED, outline=NAVY, stroke=12, shadow=(8, 10))
    outlined_text(img, (size[0] * 0.85, 330), '(probably)', font('Roboto-Bold.ttf', 120), fill=NAVY, outline=CREAM, stroke=4, angle=7)
    return img.convert('RGB')


def main():
    os.makedirs(OUT, exist_ok=True)
    welcome_sign().save(os.path.join(OUT, 'welcome_sign.png'), optimize=True)
    drill_board().save(os.path.join(OUT, 'drill_board.png'), optimize=True)
    roof_sign().save(os.path.join(OUT, 'roof_sign.png'), optimize=True)
    lost_and_found_sign().save(os.path.join(OUT, 'lost_and_found_sign.png'), optimize=True)
    prices_board().save(os.path.join(OUT, 'lost_and_found_prices.png'), optimize=True)
    bar_menu().save(os.path.join(OUT, 'bar_menu.png'), optimize=True)
    for name, img in dev_signs():
        img.save(os.path.join(OUT, name + '.png'), optimize=True)
    print('signs written to', OUT)


if __name__ == '__main__':
    main()
