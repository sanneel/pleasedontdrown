"""Lays out the clip review pictures (Editor/ClipCheck.Review.cs) as contact sheets.

  python clip_sheets.py sheets <Screenshots/ClipSheets> <out folder>
      one sheet per clip: a row per body, its eight moments left to right
  python clip_sheets.py states <Screenshots/ClipStates> <out folder> [every]
      one sheet per state and body (every n-th frame, in rows of 8) and an animated GIF of all its frames
"""
import os
import sys

from PIL import Image, ImageDraw


def label(img, text):
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, 8 * len(text) + 8, 16], fill=(0, 0, 0))
    d.text((4, 2), text, fill=(255, 255, 255))


def sheets(src, out):
    os.makedirs(out, exist_ok=True)
    for clip in sorted(os.listdir(src)):
        folder = os.path.join(src, clip)
        if not os.path.isdir(folder):
            continue
        files = sorted(f for f in os.listdir(folder) if f.endswith('.jpg'))
        bodies = sorted({f.split('_')[0] for f in files})
        w, h = 180, 210
        sheet = Image.new('RGB', (w * 8, h * len(bodies)), (40, 40, 40))
        for r, body in enumerate(bodies):
            for c in range(8):
                p = os.path.join(folder, f'{body}_{c}.jpg')
                if os.path.exists(p):
                    sheet.paste(Image.open(p).resize((w, h)), (c * w, r * h))
        label(sheet, clip)
        sheet.save(os.path.join(out, f'{clip}.jpg'), quality=85)


def states(src, out, every):
    os.makedirs(out, exist_ok=True)
    for state in sorted(os.listdir(src)):
        folder = os.path.join(src, state)
        if not os.path.isdir(folder):
            continue
        frames = [Image.open(os.path.join(folder, f)) for f in sorted(os.listdir(folder)) if f.endswith('.jpg')]
        if not frames:
            continue
        picked = frames[::every]
        w, h = 240, 240
        cols = 8
        rows = (len(picked) + cols - 1) // cols
        sheet = Image.new('RGB', (w * cols, h * rows), (40, 40, 40))
        for i, f in enumerate(picked):
            tile = f.resize((w, h))
            label(tile, f'{i * every / 10:.1f}s')
            sheet.paste(tile, ((i % cols) * w, (i // cols) * h))
        label(sheet, state)
        sheet.save(os.path.join(out, f'{state}.jpg'), quality=85)
        small = [f.resize((320, 320)) for f in frames]
        small[0].save(os.path.join(out, f'{state}.gif'), save_all=True, append_images=small[1:], duration=100, loop=0)


if __name__ == '__main__':
    if sys.argv[1] == 'sheets':
        sheets(sys.argv[2], sys.argv[3])
    else:
        states(sys.argv[2], sys.argv[3], int(sys.argv[4]) if len(sys.argv) > 4 else 3)
