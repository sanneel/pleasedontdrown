"""The game textures from polish_tripo_character.py's bakes, and the polished GLB pointed at its colour.

python ArtSource/Tools/polish_character_texture.py <polished.glb> <bake.png> <colour.jpg> <normal.png> [--size 2048]
    [--uri Textures/<model>_color3.jpg]

- colour: the bake taken down to --size (each texel the mean of the baked texels under it), the gutters between the
  islands filled from the islands outwards (pull-push), so the smaller mipmaps fetch the island's own colour at its
  edges, not black. JPEG, quality 92, no colour subsampling.
- normal: the same for the normal bake (vectors averaged and made unit again). PNG. MeshyCharacters takes it up as
  Art/Characters/Textures/<model>_baked_normal.png.
- the GLB: Blender wrote no picture; its material is pointed at --uri (default Textures/<colour file name>, as
  GlbTextureSlim leaves character models: name the colour <model>_color3.jpg or pass --uri) and its tint set back to
  white.
"""
import argparse
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from glb_io import Glb  # noqa: E402


def shrink(rgba, size):
    """Down to size x size: the mean of the covered texels under each new one, and how much of it was covered."""
    h = rgba.shape[0]
    f = h // size
    a = rgba[..., 3]
    c = rgba[..., :3] * a[..., None]
    c = c.reshape(size, f, size, f, 3).sum((1, 3))
    a = a.reshape(size, f, size, f).sum((1, 3))
    return c / np.maximum(a, 1e-8)[..., None], a / (f * f)


def fill(colour, weight):
    """Pull-push: covered texels stay; the rest take the islands' colours spread outwards, smoothly."""
    levels = []
    c, a = colour * weight[..., None], weight.copy()
    while c.shape[0] > 1:
        levels.append((c, a))
        c = c[0::2, 0::2] + c[1::2, 0::2] + c[0::2, 1::2] + c[1::2, 1::2]
        a = a[0::2, 0::2] + a[1::2, 0::2] + a[0::2, 1::2] + a[1::2, 1::2]
        keep = np.minimum(a, 1.0) / np.maximum(a, 1e-8)
        c, a = c * keep[..., None], np.minimum(a, 1.0)
    out = c / np.maximum(a, 1e-8)[..., None]
    for c, a in reversed(levels):
        up = np.repeat(np.repeat(out, 2, 0), 2, 1)
        out = c + up * (1.0 - a)[..., None]
    return out


def load(path):
    return np.asarray(Image.open(path).convert('RGBA'), np.float32) / 255.0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('glb')
    ap.add_argument('bake')
    ap.add_argument('colour')
    ap.add_argument('normal')
    ap.add_argument('--size', type=int, default=2048)
    ap.add_argument('--uri', default='')
    a = ap.parse_args()

    bake = load(a.bake)
    colour, covered = shrink(bake, a.size)
    covered = np.clip(covered, 0, 1)
    colour = fill(colour, covered)
    Image.fromarray(np.clip(colour * 255 + 0.5, 0, 255).astype(np.uint8)).save(a.colour, quality=92, subsampling=0)
    print(f'[texture] colour {a.colour}: {covered.mean():.1%} baked, the rest filled')

    nb = load(a.bake.replace('.png', '_normal.png'))
    n = nb[..., :3] * 2 - 1
    nb = np.concatenate([n, nb[..., 3:]], 2)
    vec, ncov = shrink(nb, a.size)
    vec = fill(vec, np.clip(ncov, 0, 1))
    vec[np.linalg.norm(vec, axis=2) < 1e-3] = (0, 0, 1)
    vec /= np.linalg.norm(vec, axis=2, keepdims=True)
    Image.fromarray(np.clip((vec * 0.5 + 0.5) * 255 + 0.5, 0, 255).astype(np.uint8)).save(a.normal)
    print(f'[texture] normal {a.normal}')

    g = Glb(a.glb)
    j = g.json
    if any('bufferView' in im for im in j.get('images', [])):
        raise SystemExit('[texture] the GLB carries its own picture: export it with export_image_format NONE')
    j['images'] = [{'name': 'baked_color', 'uri': a.uri or 'Textures/' + os.path.basename(a.colour)}]
    j['textures'] = [{'sampler': 0, 'source': 0}]
    j['samplers'] = [{'magFilter': 9729, 'minFilter': 9987}]
    pbr = j['materials'][0].setdefault('pbrMetallicRoughness', {})
    pbr.pop('baseColorFactor', None)
    pbr['baseColorTexture'] = {'index': 0}
    g.save(a.glb)
    print(f'[texture] {a.glb} -> {j["images"][0]["uri"]}')


if __name__ == '__main__':
    main()
