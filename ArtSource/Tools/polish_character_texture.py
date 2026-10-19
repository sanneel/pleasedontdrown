"""The game textures from polish_tripo_character.py's bakes, and the polished GLB pointed at its colour.

python ArtSource/Tools/polish_character_texture.py <polished.glb | -> <bake.png> <colour.jpg> <normal.png> [--size 2048]
    [--uri Textures/<model>_color3.jpg] [--colour-from <the model's own colour>] [--sharpen 0.4]

- colour: the bake taken down to --size (each texel the mean of the baked texels under it), the gutters between the
  islands filled from the islands outwards (pull-push), so the smaller mipmaps fetch the island's own colour at its
  edges, not black. JPEG, quality 92, no colour subsampling.
- normal: the same for the normal bake (vectors averaged and made unit again). PNG. MeshyCharacters takes it up as
  Art/Characters/Textures/<model>_baked_normal.png.
- --colour-from (models that kept their UVs: polish_tripo_character.py --keep-uvs 1): the model's own colour instead
  of the bake, its gutters filled the same way. Where the islands are comes from --coverage-glb (the game model's own
  UV triangles, 4 texels round them): Blender's import welds away the odd face, and the bake's own coverage then
  painted over a pocket the thief's mesh still uses. GLB: pass -.
- --sharpen a: a light unsharp mask on the front of the head (the bake's _face.png), for soft painted faces.
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


def glb_coverage(path, size):
    """The texels a model's UV triangles cover, and 4 round them (1.0), the rest 0. The triangles' edges count too:
    Meshy's thief has 770 triangles (5% of his skin, a hip) squashed to one point of the UV map, which cover no texel
    but still show the one under that point."""
    from texel_map import rasterize
    g = Glb(path)
    prim = g.primitive()
    uv = g.accessor(prim['attributes']['TEXCOORD_0']).astype(np.float64)
    pos = g.accessor(prim['attributes']['POSITION']).astype(np.float64)
    tris = g.accessor(prim['indices']).reshape(-1, 3)
    covered, _, _, _ = rasterize(uv, pos, pos, tris, size, size)
    for i, j in ((0, 1), (1, 2), (2, 0)):
        a, b = uv[tris[:, i]] * size, uv[tris[:, j]] * size
        for t in np.linspace(0.0, 1.0, 17):
            p = a + (b - a) * t
            x = np.clip(p[:, 0].astype(int), 0, size - 1)
            y = np.clip(p[:, 1].astype(int), 0, size - 1)
            covered[y, x] = True
    for _ in range(4):
        grown = covered.copy()
        grown[1:] |= covered[:-1]; grown[:-1] |= covered[1:]
        grown[:, 1:] |= covered[:, :-1]; grown[:, :-1] |= covered[:, 1:]
        covered = grown
    return covered.astype(np.float32)


def glb_collapsed(path, size):
    """The texels under triangles whose UVs are squashed to (nearly) a point or a line: the tangent there is
    nonsense, so the normal map is kept flat on them."""
    g = Glb(path)
    prim = g.primitive()
    uv = g.accessor(prim['attributes']['TEXCOORD_0']).astype(np.float64) * size
    tris = g.accessor(prim['indices']).reshape(-1, 3)
    a = uv[tris]
    e1, e2 = a[:, 1] - a[:, 0], a[:, 2] - a[:, 0]
    bad = np.abs(e1[:, 0] * e2[:, 1] - e1[:, 1] * e2[:, 0]) / 2 < 0.5
    out = np.zeros((size, size), bool)
    for p in a[bad].reshape(-1, 2):
        x, y = int(np.clip(p[0], 0, size - 1)), int(np.clip(p[1], 0, size - 1))
        out[max(0, y - 1):y + 2, max(0, x - 1):x + 2] = True
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
    ap.add_argument('--colour-from', default='')
    ap.add_argument('--sharpen', type=float, default=0.0)
    ap.add_argument('--normal-size', type=int, default=0)  # (default: --size)
    ap.add_argument('--coverage-glb', default='')
    a = ap.parse_args()

    bake = load(a.bake)
    colour, covered = shrink(bake, a.size)
    covered = np.clip(covered, 0, 1)
    if a.colour_from:
        own = Image.open(a.colour_from).convert('RGB')
        if own.size != (a.size, a.size):
            own = own.resize((a.size, a.size), Image.LANCZOS)
        colour = np.asarray(own, np.float32) / 255.0
        covered = (covered > 0).astype(np.float32)
        if a.coverage_glb:
            covered = glb_coverage(a.coverage_glb, a.size)
    colour = fill(colour, covered)
    if a.sharpen > 0:
        from PIL import ImageFilter
        face = load(a.bake.replace('.png', '_face.png'))
        face = np.asarray(Image.fromarray((face[..., 0] * 255).astype(np.uint8)).resize((a.size, a.size), Image.BILINEAR)
                          .filter(ImageFilter.GaussianBlur(3)), np.float32) / 255.0
        img = Image.fromarray(np.clip(colour * 255 + 0.5, 0, 255).astype(np.uint8))
        soft = np.asarray(img.filter(ImageFilter.GaussianBlur(1.2)), np.float32) / 255.0
        colour = np.clip(colour + a.sharpen * (colour - soft) * (face * covered)[..., None], 0, 1)
    Image.fromarray(np.clip(colour * 255 + 0.5, 0, 255).astype(np.uint8)).save(a.colour, quality=92, subsampling=0)
    print(f'[texture] colour {a.colour}: {covered.mean():.1%} baked, the rest filled')

    nb = load(a.bake.replace('.png', '_normal.png'))
    n = nb[..., :3] * 2 - 1
    nb = np.concatenate([n, nb[..., 3:]], 2)
    vec, ncov = shrink(nb, a.normal_size or a.size)
    vec = fill(vec, np.clip(ncov, 0, 1))
    vec[np.linalg.norm(vec, axis=2) < 1e-3] = (0, 0, 1)
    if a.coverage_glb:
        vec[glb_collapsed(a.coverage_glb, vec.shape[0])] = (0, 0, 1)
    vec /= np.linalg.norm(vec, axis=2, keepdims=True)
    Image.fromarray(np.clip((vec * 0.5 + 0.5) * 255 + 0.5, 0, 255).astype(np.uint8)).save(a.normal)
    print(f'[texture] normal {a.normal}')

    if a.glb == '-':
        return
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
