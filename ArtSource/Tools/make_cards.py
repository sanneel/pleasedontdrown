# Playing cards for the casino's blackjack table (Assets/_Game/Resources/Cards/cards.png): one atlas, 13 columns (A 2 3
# ... 10 J Q K) by 5 rows (spades, hearts, diamonds, clubs, then the back in column 0). Each card is 200 x 280 px:
# the rank and a small suit in two corners, the rank big over a big suit in the middle (read from across the table);
# the court cards' letter in a frame; the back in the hotel's
# teal and gold. Fun.BlackjackCards maps a card's id (suit * 13 + rank, as Fun.BlackjackTable deals them) to its cell.
# blender -b -P make_cards.py
from pathlib import Path
import bpy, math

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/_Game/Resources/Cards/cards.png'
W, H, COLS, ROWS = 200, 280, 13, 5
RANKS = ['A', '2', '3', '4', '5', '6', '7', '8', '9', '10', 'J', 'Q', 'K']
SUITS = ['\u2660', '\u2665', '\u2666', '\u2663']
RANK_FONT = 'C:/Windows/Fonts/georgiab.ttf'
SUIT_FONT = 'C:/Windows/Fonts/seguisym.ttf'

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
rank_font = bpy.data.fonts.load(RANK_FONT)
suit_font = bpy.data.fonts.load(SUIT_FONT)


def mat(name, rgb):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*rgb, 1)
    return m


def lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def srgb(rgb):
    return tuple(lin(c) for c in rgb)


paper = mat('paper', srgb((0.98, 0.97, 0.94)))
edge = mat('edge', srgb((0.78, 0.76, 0.72)))
red = mat('red', srgb((0.80, 0.08, 0.12)))
black = mat('black', srgb((0.08, 0.08, 0.10)))
teal = mat('teal', srgb((0.05, 0.40, 0.42)))
teal_dark = mat('teal_dark', srgb((0.03, 0.26, 0.28)))
gold = mat('gold', srgb((0.86, 0.68, 0.30)))

# One unit = one pixel. Card (col, row) has its top-left corner at (col*W, -row*H).
def rect(x, y, w, h, m, z=0.0, radius=0.0):
    bpy.ops.mesh.primitive_plane_add(size=1, location=(x + w / 2, y - h / 2, z))
    o = bpy.context.object
    o.scale = (w, h, 1)
    bpy.ops.object.transform_apply(scale=True)
    if radius > 0:
        mod = o.modifiers.new('round', 'BEVEL')
        mod.affect = 'VERTICES'
        mod.width = radius
        mod.segments = 6
    o.data.materials.append(m)
    return o


def text(body, font, size, x, y, m, z=0.2, rot=0.0, align='CENTER'):
    bpy.ops.object.text_add(location=(x, y, z))
    o = bpy.context.object
    o.data.body = body
    o.data.font = font
    o.data.size = size
    o.data.align_x = align
    o.data.align_y = 'CENTER'
    o.rotation_euler = (0, 0, rot)
    o.data.materials.append(m)
    return o


for row in range(4):
    ink = red if row in (1, 2) else black
    for col in range(13):
        x0, y0 = col * W, -row * H
        rect(x0 + 2, y0 - 2, W - 4, H - 4, edge, 0.0, 16)
        rect(x0 + 5, y0 - 5, W - 10, H - 10, paper, 0.1, 13)
        rank, suit = RANKS[col], SUITS[row]
        # The corners: rank over a small suit (top left), the same upside down (bottom right).
        for flip in (False, True):
            cx, cy = (x0 + 30, y0 - 36) if not flip else (x0 + W - 30, y0 - H + 36)
            sgn = -1 if flip else 1
            rot = math.pi if flip else 0.0
            text(rank, rank_font, 44 if len(rank) == 1 else 36, cx, cy, ink, rot=rot)
            text(suit, suit_font, 50, cx, cy - 44 * sgn, ink, rot=rot)
        # The middle: a big suit; court cards a big letter over a suit, in a thin frame.
        mx, my = x0 + W / 2, y0 - H / 2
        if col >= 10:
            rect(x0 + 44, y0 - 56, W - 88, H - 112, ink, 0.12, 6)
            rect(x0 + 47, y0 - 59, W - 94, H - 118, paper, 0.14, 5)
            text(rank, rank_font, 96, mx, my + 26, ink)
            text(suit, suit_font, 80, mx, my - 50, ink)
        else:
            # (The rank big over the suit: it's read from a step back across the table.)
            text(rank, rank_font, 92 if len(rank) == 1 else 78, mx, my + 40, ink)
            text(suit, suit_font, 130, mx, my - 50, ink)

# The back: teal with a gold frame and a diamond pattern, "PDD" in the middle.
x0, y0 = 0, -4 * H
rect(x0 + 2, y0 - 2, W - 4, H - 4, edge, 0.0, 16)
rect(x0 + 5, y0 - 5, W - 10, H - 10, paper, 0.1, 13)
rect(x0 + 14, y0 - 14, W - 28, H - 28, teal, 0.12, 8)
rect(x0 + 20, y0 - 20, W - 40, H - 40, gold, 0.13, 6)
rect(x0 + 24, y0 - 24, W - 48, H - 48, teal_dark, 0.14, 5)
for i in range(4):
    for j in range(6):
        cx, cy = x0 + 48 + i * 35, y0 - 52 + -j * 35
        bpy.ops.mesh.primitive_plane_add(size=1, location=(cx, cy, 0.16))
        d = bpy.context.object
        d.scale = (11, 11, 1)
        d.rotation_euler = (0, 0, math.pi / 4)
        d.data.materials.append(teal)
text('PDD', rank_font, 40, x0 + W / 2, y0 - H / 2, gold, z=0.3)

# Flat colours, seen straight down, one pixel per unit.
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'FLAT'
scene.display.shading.color_type = 'MATERIAL'
scene.display.render_aa = '16'
scene.view_settings.view_transform = 'Standard'
scene.render.film_transparent = True
scene.render.resolution_x, scene.render.resolution_y = COLS * W, ROWS * H
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
bpy.ops.object.camera_add(location=(COLS * W / 2, -ROWS * H / 2, 100))
cam = bpy.context.object
cam.data.type = 'ORTHO'
cam.data.ortho_scale = COLS * W
scene.camera = cam
OUT.parent.mkdir(parents=True, exist_ok=True)
scene.render.filepath = str(OUT)
bpy.ops.render.render(write_still=True)
print('CARDS', OUT)
