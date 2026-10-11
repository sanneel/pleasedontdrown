"""Island 3 surface textures from Poly Haven (CC0) scans.

Each layer gets two seamless 1024 maps for Assets/_Game/Data/Shaders/Ground.shader:
  <name>_D.png  RGB: the scan's detail as a ratio to its own average colour (0.5 = average, linear, x2 in the shader),
                with the big light/dark blotches taken out so it doesn't show the tiling; A: height (for blending).
  <name>_N.png  RGB: OpenGL normal map; A: ambient occlusion.
The colour itself comes from the material (and the ground's vertex colours), so the island keeps its palette and the
scan only adds the grain: blades, pebbles, cracks, stones.

    python ArtSource/Tools/make_island3_ground_textures.py [--downloads <dir>]

Downloads the 2K JPGs from polyhaven.com into --downloads (default: %TEMP%/polyhaven) if they aren't there yet.
"""
import argparse
import json
import os
import tempfile
import urllib.request

import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "_Game", "Art", "Island3Ground")
SIZE = 1024

# Our layer name -> Poly Haven asset id, how much of the scan's big blotches to keep (0 = none), how much of its own
# hue changes to keep (0 = grey grain only: the moss on a scan would turn purple under a grey tint).
LAYERS = {
    "Sand": ("aerial_beach_01", 0.15, 1.0),
    "Grass": ("leafy_grass", 0.2, 1.0),
    "Jungle": ("forest_ground_04", 0.25, 1.0),
    "Dirt": ("rocky_trail", 0.25, 1.0),
    "Rock": ("rock_face_03", 0.35, 0.8),
    "Cliff": ("cliff_side", 0.35, 0.5),
    "CastleStone": ("castle_wall_slates", 0.5, 0.6),
}


def fetch(asset, downloads):
    os.makedirs(downloads, exist_ok=True)
    files = None
    paths = {}
    for kind, key in (("diff", "Diffuse"), ("nor_gl", "nor_gl"), ("arm", "arm")):
        path = os.path.join(downloads, f"{asset}_{kind}_2k.jpg")
        if not os.path.exists(path):
            if files is None:
                with urllib.request.urlopen(f"https://api.polyhaven.com/files/{asset}") as r:
                    files = json.load(r)
            urllib.request.urlretrieve(files[key]["2k"]["jpg"]["url"], path)
        paths[kind] = path
    return paths


def load(path, mode="RGB"):
    """Float image resized to SIZE, seamless: resized as a 3 x 3 tile and cut out of the middle."""
    im = Image.open(path).convert(mode)
    w, h = im.size
    tiled = Image.new(mode, (w * 3, h * 3))
    for i in range(3):
        for j in range(3):
            tiled.paste(im, (i * w, j * h))
    tiled = tiled.resize((SIZE * 3, SIZE * 3), Image.LANCZOS)
    return np.asarray(tiled.crop((SIZE, SIZE, SIZE * 2, SIZE * 2)), dtype=np.float32) / 255.0


def wrap_blur(a, sigma):
    return gaussian_filter(a, sigma=(sigma, sigma) + (0,) * (a.ndim - 2), mode="wrap")


def srgb_to_linear(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def make(name, asset, keep, hue, downloads):
    paths = fetch(asset, downloads)
    colour = srgb_to_linear(load(paths["diff"]))
    mean = colour.reshape(-1, 3).mean(axis=0)
    # Ratio to the local average (blotches out), with a little of the blotches kept so it isn't flat.
    local = wrap_blur(colour, SIZE / 12) + 1e-4
    ratio = colour / local * (local / mean) ** keep
    grey = ratio @ (mean / mean.sum())
    ratio = grey[..., None] + (ratio - grey[..., None]) * hue
    detail = np.clip(ratio * 0.5, 0.0, 1.0)

    arm = load(paths["arm"])
    ao = arm[..., 0]
    luminance = colour @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
    height = 0.6 * ao + 0.4 * luminance / max(luminance.max(), 1e-4)
    height = (height - height.min()) / max(height.max() - height.min(), 1e-4)

    normal = load(paths["nor_gl"])
    os.makedirs(OUT, exist_ok=True)
    d = np.dstack([detail, height]) * 255.0
    n = np.dstack([normal, ao]) * 255.0
    Image.fromarray(np.round(d).astype(np.uint8), "RGBA").save(os.path.join(OUT, f"{name}_D.png"), optimize=True)
    Image.fromarray(np.round(n).astype(np.uint8), "RGBA").save(os.path.join(OUT, f"{name}_N.png"), optimize=True)
    print(f"{name}: {asset}, mean colour {np.round(mean ** (1 / 2.2), 3)}, ratio {ratio.min():.2f}..{ratio.max():.2f}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--downloads", default=os.path.join(tempfile.gettempdir(), "polyhaven"))
    parser.add_argument("--only", nargs="*")
    args = parser.parse_args()
    for name, (asset, keep, hue) in LAYERS.items():
        if not args.only or name in args.only:
            make(name, asset, keep, hue, args.downloads)


if __name__ == "__main__":
    main()
