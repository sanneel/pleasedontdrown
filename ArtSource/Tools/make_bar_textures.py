"""Tileable detail pictures for the coloured Tripo beach bar (retexture_tripo_bar.py puts them on its wood and thatch).

python ArtSource/Tools/make_bar_textures.py [out_dir]   (default ArtSource/Resort/TripoBar/textures)

Each is grey-ish detail around a mean of ~0.8 (the material's colour, divided by that mean, keeps the bar's palette):
- bar_wood_grain.jpg: long grain and growth lines along u, no seams (walls, timbers, shelves: the model has its own
  board grooves). Tile 2 m.
- bar_wood_planks.jpg: 0.2 m boards along u, staggered end joints, each board its own tone. Tile 2 m.
- bar_thatch.jpg: straw along u in courses across it, each course's top in shadow. Tile 1.5 m.
Everything is sums of whole-period sines and wrapped noise, so the pictures repeat without a seam.
"""
import os
import sys

import numpy as np
from PIL import Image

SIZE = 1024
rng = np.random.default_rng(20261019)


def wrapped_noise(h, w, cells_y, cells_x):
    """Smooth noise that tiles: random values on a coarse grid, cubic-interpolated with wrap-around."""
    grid = rng.standard_normal((cells_y, cells_x))
    def interp(a, n, axis):
        cells = a.shape[axis]
        t = np.arange(n) * cells / n
        i = np.floor(t).astype(int)
        f = t - i
        f = f * f * (3 - 2 * f)
        a0 = np.take(a, i % cells, axis=axis)
        a1 = np.take(a, (i + 1) % cells, axis=axis)
        shape = [1, 1]
        shape[axis] = n
        f = f.reshape(shape)
        return a0 * (1 - f) + a1 * f
    return interp(interp(grid, h, 0), w, 1)


def fbm(h, w, base_y, base_x, octaves=4):
    out = np.zeros((h, w))
    amp = 1.0
    for o in range(octaves):
        out += amp * wrapped_noise(h, w, base_y * 2 ** o, base_x * 2 ** o)
        amp *= 0.5
    return out / 1.9


def grain(h, w, rings_per_tile, streak_cells_x=6):
    """Wood grain along x: growth lines bent by slow noise, plus fine streaks stretched along x."""
    y = np.arange(h)[:, None] / h
    warp = fbm(h, w, 3, 2) * 0.06 + fbm(h, w, 8, 3, 2) * 0.012
    rings = 0.5 + 0.5 * np.sin(2 * np.pi * (y + warp) * rings_per_tile)
    rings = rings ** 3                                                     # thin dark late-wood lines
    streaks = wrapped_noise(h, w, h // 3, streak_cells_x) * 0.5 + wrapped_noise(h, w, h // 6, streak_cells_x * 2) * 0.5
    return 0.82 - 0.16 * rings + 0.05 * streaks + 0.04 * fbm(h, w, 4, 4)


def planks(h, w, boards=10):
    g = grain(h, w, 70)
    out = np.empty_like(g)
    edges = np.linspace(0, h, boards + 1).astype(int)
    xs = np.arange(w)
    for b in range(boards):
        y0, y1 = edges[b], edges[b + 1]
        joint = int(rng.integers(0, w))
        tone = 1.0 + rng.normal(0, 0.06, 2)
        seg = np.where(((xs - joint) % w) < w // 2, tone[0], tone[1])        # two lengths per board, ends staggered
        shift = int(rng.integers(0, h))                                       # each board its own piece of grain
        out[y0:y1] = np.roll(g, shift, axis=0)[y0:y1] * seg[None, :]
        out[y0:y0 + 3] *= 0.55                                                # the gap between boards
        out[y0 + 3:y0 + 5] *= 0.85
        for x in (joint, (joint + w // 2) % w):                               # end joints
            out[y0:y1, x % w] *= 0.6
            out[y0:y1, (x + 1) % w] *= 0.8
    return out


def thatch(h, w, courses=6):
    """Straw along x (down the roof's slope): long thin strands, in courses that step across x."""
    strands = wrapped_noise(h, w, h, 6) * 0.5 + wrapped_noise(h, w, h // 2, 4) * 0.3 + wrapped_noise(h, w, h // 8, 3) * 0.2
    strands = 0.8 + 0.2 * strands + 0.05 * fbm(h, w, 6, 3)
    breaks = wrapped_noise(h, w, h // 2, 40) > 1.3                           # a strand ending, another starting
    strands = strands * np.where(breaks, 0.7, 1.0)
    x = (np.arange(w)[None, :] / w * courses) % 1.0
    wobble = fbm(h, w, 16, 2, 3) * 0.09
    x = (x + wobble) % 1.0
    shade = 0.5 + 0.5 * np.clip(x / 0.4, 0, 1) ** 0.7                        # each course's start in the shadow of the one above
    shade = shade + 0.12 * np.clip((x - 0.9) / 0.1, 0, 1)                     # its strands' ends catching the light
    return strands * shade / shade.mean() * 0.8


def save(img, path):
    img = img / img.mean() * 0.8
    rgb = np.stack([img, img * 0.985, img * 0.96], axis=2)                    # a touch warm, so the tint isn't dulled
    Image.fromarray(np.clip(rgb * 255 + 0.5, 0, 255).astype(np.uint8)).save(path, quality=90, subsampling=0)
    print(f'[bar textures] {path}: mean {img.mean():.2f}, {img.min():.2f}..{img.max():.2f}')


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else 'ArtSource/Resort/TripoBar/textures'
    os.makedirs(out, exist_ok=True)
    save(grain(SIZE, SIZE, 46), os.path.join(out, 'bar_wood_grain.jpg'))
    save(planks(SIZE, SIZE), os.path.join(out, 'bar_wood_planks.jpg'))
    save(thatch(SIZE, SIZE), os.path.join(out, 'bar_thatch.jpg'))


if __name__ == '__main__':
    main()
