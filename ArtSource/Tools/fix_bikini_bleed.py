"""Cleans the bikini bottom's edge on the Tripo tourist girls' hips and bum.

What was wrong. The red-bikini girl (tourist_girl_red) wears a thong, with a faint red blush painted over her bum
cheeks. The other Tripo girls are her mesh with another texture (vertex for vertex and UV for UV the same,
MeshyCharacters.SameMeshAs), and their texturing turned that blush into a wide blurry smear of their bikini colour
(teal, magenta, green...) over the cheeks and hips. In the game it read as stains on the skin.

What this does, in the hip band only (texels whose spot on the body is between the thighs and the waist,
texel_map.rasterize):
 1. The red girl: her skin and bikini colours are found and each texel is read as a mix of the two. Half or more
    red becomes full bikini (her thong, its folds kept), less becomes plain skin (its shading kept), with a narrow
    anti-aliased border. That bikini share is the outline for all the others.
 2. Every other girl: outside the outline her skin is the red girl's cleaned skin, tinted to her own skin tone
    (the ratio of the two textures, learnt round each spot from her skin that the smear didn't reach and carried
    across the smear); inside it her bikini is the red girl's thong shading in her own bikini colour.
Hair, hands and anything else that isn't skin on the red girl stay as each girl painted them.

The model's texture is replaced in the GLB (every other byte kept) and, with --jpg-dir, the copy the game uses
(Assets/_Game/Avatar/Bodies/<file>_color.jpg, MeshyCharacters.BodyMaterial) is written the same, so no re-bake is
needed. Run it again after clean_character_textures.py (that starts from the originals).

python ArtSource/Tools/fix_bikini_bleed.py <tourist_girl_red.glb> <other girl.glb ...>
    [--jpg-dir Assets/_Game/Avatar/Bodies] [--preview-dir <dir>]
"""
import argparse
import io
import os
import sys

import numpy as np
from scipy import ndimage
from scipy.spatial import cKDTree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from glb_io import Glb, load_image, png_bytes  # noqa: E402
from texel_map import rasterize  # noqa: E402

BAND = (0.32, 0.66)     # the hip band, as a share of the body's height from the soles
FADE = 0.03             # ... faded in and out over this share at its top and bottom
THRESHOLD = 0.5         # bikini share from which a texel of the red girl is bikini
EDGE = 0.04             # half width of the anti-aliased border, in bikini share
LUM = np.array([0.299, 0.587, 0.114])


def jpeg_bytes(rgb, quality=95):
    from PIL import Image
    buf = io.BytesIO()
    Image.fromarray(np.clip(rgb * 255.0 + 0.5, 0, 255).astype(np.uint8), 'RGB').save(
        buf, 'JPEG', quality=quality, subsampling=0, optimize=True)
    return buf.getvalue()


def texture(glb):
    return load_image(glb.image_bytes(glb.base_color_image())).astype(np.float64)


class Hips:
    """The hip band of the shared mesh in texture space."""

    def __init__(self, glb, shape):
        prim = glb.primitive()
        P = glb.accessor(prim['attributes']['POSITION']).astype(np.float64)
        N = glb.accessor(prim['attributes']['NORMAL']).astype(np.float64)
        UV = glb.accessor(prim['attributes']['TEXCOORD_0']).astype(np.float64)
        T = glb.accessor(prim['indices']).reshape(-1, 3)
        H, W = shape
        covered, pos, _, tri = rasterize(UV, P, N, T, W, H)
        y0, y1 = P[:, 1].min(), P[:, 1].max()
        share = (pos[..., 1] - y0) / (y1 - y0)
        # Not the arms and hands hanging beside the hips (mostly skinned to an arm bone).
        joints = [glb.json['nodes'][j]['name'] for j in glb.json['skins'][0]['joints']]
        arm_joint = np.array([any(k in n.lower() for k in ('arm', 'hand', 'shoulder')) for n in joints])
        J = glb.accessor(prim['attributes']['JOINTS_0']).astype(int)
        Wt = glb.accessor(prim['attributes']['WEIGHTS_0']).astype(np.float64)
        arm_vertex = (Wt * arm_joint[J]).sum(axis=1) > 0.3
        arm_tri = arm_vertex[T].any(axis=1)
        on_arm = (tri >= 0) & arm_tri[np.maximum(tri, 0)]
        self.band = covered & ~on_arm & (share >= BAND[0]) & (share <= BAND[1])
        # The empty atlas round the band's islands (a few texels out) takes the nearest island texel's colour, so
        # the gutter's old colour doesn't bleed in as light lines along the seams (bilinear filtering, mipmaps).
        dist, (iy, ix) = ndimage.distance_transform_edt(~covered, return_indices=True)
        gutter = ~covered & (dist <= 8) & self.band[iy, ix]
        self.gutter = np.nonzero(gutter)
        self.gutter_from = (iy[gutter], ix[gutter])
        s = share[self.band]
        # 0 at the band's top and bottom, 1 a little inside it.
        self.weight = np.clip(np.minimum(s - BAND[0], BAND[1] - s) / FADE, 0.0, 1.0)
        self.pos = pos[self.band]
        # Texels well inside their island, to learn colours from (island rims hold the bake's gutter colour).
        self.inner = (ndimage.binary_erosion(covered, iterations=2) & self.band)[self.band]


def chroma(c):
    return c / np.maximum(c.sum(axis=-1, keepdims=True), 1e-3)


def local_mean(hips, pick, values, k=48):
    """Per band texel, the mean of values over the k nearest picked texels in 3D."""
    idx = np.nonzero(pick)[0]
    if len(idx) > 200000:
        idx = np.random.default_rng(0).choice(idx, 200000, replace=False)
    tree = cKDTree(hips.pos[idx])
    picked = values[idx].astype(np.float32)
    out = np.empty((len(hips.pos), values.shape[1]), np.float64)
    for s in range(0, len(hips.pos), 250000):
        _, nn = tree.query(hips.pos[s:s + 250000], k=k)
        out[s:s + 250000] = picked[nn].mean(axis=1)
    return out


def pad(hips, result):
    result[hips.gutter] = result[hips.gutter_from]
    return result


def fill_rims(hips, out):
    """Island rims (half gutter, the colours there are unreliable) take the mended colour of the nearest texel
    well inside an island: the same spot on the body, on this island or the one across the seam."""
    idx = np.nonzero(hips.inner)[0]
    rims = np.nonzero(~hips.inner)[0]
    _, nn = cKDTree(hips.pos[idx]).query(hips.pos[rims], k=1)
    out[rims] = out[idx[nn]]
    return out


def clean_reference(img, hips):
    """The red girl mended; returns (texture, bikini share per band texel, skin-or-bikini mask, note)."""
    C = img[hips.band]
    Ci = C[hips.inner]
    skin = np.median(Ci, axis=0)
    # Her bikini: the densest colour far from skin in hue.
    pick = (np.linalg.norm(chroma(Ci) - chroma(skin), axis=1) > 0.1) & (Ci.max(axis=1) > 0.3)
    cand = Ci[pick]
    q = np.clip((cand * 16).astype(int), 0, 15)
    key = q[:, 0] * 256 + q[:, 1] * 16 + q[:, 2]
    bikini = np.median(cand[key == np.bincount(key).argmax()], axis=0)

    d = bikini - skin
    t0 = ((C - skin) @ d) / (d @ d)
    off0 = np.linalg.norm(C - skin - t0[:, None] * d, axis=1) / np.linalg.norm(d)
    plain = hips.inner & (t0 < 0.08) & (off0 < 0.2)
    S = local_mean(hips, plain, C)
    D = bikini - S
    DD = np.maximum((D * D).sum(axis=1), 1e-6)
    t = ((C - S) * D).sum(axis=1) / DD
    rest = C - S - t[:, None] * D
    off = np.linalg.norm(rest, axis=1) / np.sqrt(DD)
    mixed = (t > -0.1) & (t < 1.2) & (off < 0.3)       # skin, bikini or a mix of them (not hair, not a hand's nail)

    a = np.clip((t - THRESHOLD) / (2 * EDGE) + 0.5, 0.0, 1.0)
    shade = np.clip(((C - t[:, None] * D) @ LUM) / np.maximum(S @ LUM, 1e-3), 0.6, 1.4)
    skin_side = S * shade[:, None]
    cloth_side = S + rest + D
    out = skin_side + a[:, None] * (cloth_side - skin_side)
    out = fill_rims(hips, out)
    out = C + (hips.weight * mixed)[:, None] * (out - C)
    result = img.copy()
    result[hips.band] = np.clip(out, 0, 1)
    pad(hips, result)
    note = f'skin {np.round(skin * 255)} bikini {np.round(bikini * 255)}'
    return result, a * mixed, mixed, note


def clean_like(img, ref, hips, a, mixed):
    """Another girl: the red girl's cleaned hips in her own skin tone and bikini colour."""
    C = img[hips.band]
    R = ref[hips.band]
    ref_skin = hips.inner & mixed & (a < 0.01)
    skin = np.median(C[ref_skin], axis=0)
    inside = hips.inner & (a > 0.99)
    bikini = np.median(C[inside], axis=0)
    # How much of her bikini is in each texel, by colour and by hue alone (a smear in the shadow under the bum is
    # dark; a dark bikini's smear has much the hue of a tan).
    d = bikini - skin
    t = ((C - skin) @ d) / (d @ d)
    dc = chroma(bikini) - chroma(skin)
    tc = ((chroma(C) - chroma(skin)) @ dc) / max(dc @ dc, 1e-6)
    t = np.maximum(t, tc)
    # Her skin the smear didn't reach: her skin's hue where the red girl has skin, and nothing of her bikini in it.
    hers = ref_skin & (np.linalg.norm(chroma(C) - chroma(skin), axis=1) < 0.02) & (t < 0.04)
    # Her skin colour round each spot (carried across the smear), with the red girl's fine shading on it (only its
    # brightness: the hue of her old blush stays out). Island rims keep no shading (their texels are half gutter).
    local = local_mean(hips, hers, C, k=96)
    ref_lum = R @ LUM
    shade = ref_lum / np.maximum(local_mean(hips, ref_skin, ref_lum[:, None], k=96)[:, 0], 1e-3)
    shade = np.clip(shade, 0.8, 1.15)
    # Only as far as the smear went: her clean skin keeps its own painting. Averaged over a centimetre or so, or
    # the texel-by-texel noise of the measure left a mottled patch where the smear thins out.
    smear = np.clip(t / 0.1, 0.0, 1.0)
    smear = local_mean(hips, np.ones(len(t), bool), smear[:, None], k=64)[:, 0]
    skin_out = C + smear[:, None] * (local * shade[:, None] - C)
    # Her bikini: its colour well inside the outline, shaded as the red girl's thong.
    ref_bikini = np.median(R[inside], axis=0)
    cloth_out = bikini * ((R @ LUM) / max(ref_bikini @ LUM, 1e-3))[:, None]
    out = fill_rims(hips, skin_out + a[:, None] * (cloth_out - skin_out))
    out = C + (hips.weight * mixed)[:, None] * (out - C)
    result = img.copy()
    result[hips.band] = np.clip(out, 0, 1)
    pad(hips, result)
    return result, f'skin {np.round(skin * 255)} bikini {np.round(bikini * 255)}, {int(hers.sum())} clean skin texels'


def save(path, glb, rgb, preview_dir, jpg_dir):
    name = os.path.splitext(os.path.basename(path))[0]
    image = glb.base_color_image()
    mime = glb.json['images'][image].get('mimeType', 'image/png')
    payload = png_bytes(rgb.astype(np.float32)) if mime == 'image/png' else jpeg_bytes(rgb, 94)
    glb.replace_image(image, payload, mime)
    glb.save(os.path.join(preview_dir, name + '.glb') if preview_dir else path)
    jpg = os.path.join(jpg_dir, name + '_color.jpg') if jpg_dir else None
    if jpg and not preview_dir and os.path.exists(jpg):
        with open(jpg, 'wb') as f:
            f.write(jpeg_bytes(rgb, 95))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('reference', help='tourist_girl_red.glb')
    ap.add_argument('others', nargs='*', help='the girls that are her mesh with another texture')
    ap.add_argument('--jpg-dir', help='write <name>_color.jpg here too (the copy the game uses), when it exists')
    ap.add_argument('--preview-dir', help='write <name>.glb here instead of changing the models')
    args = ap.parse_args()

    glb = Glb(args.reference)
    img = texture(glb)
    hips = Hips(glb, img.shape[:2])
    ref, a, mixed, note = clean_reference(img, hips)
    print(f'{os.path.basename(args.reference)}: {note}', flush=True)
    save(args.reference, glb, ref, args.preview_dir, args.jpg_dir)
    for path in args.others:
        g = Glb(path)
        other = texture(g)
        if other.shape != img.shape:
            print(f'{os.path.basename(path)}: texture size differs, skipped')
            continue
        out, note = clean_like(other, ref, hips, a, mixed)
        print(f'{os.path.basename(path)}: {note}', flush=True)
        save(path, g, out, args.preview_dir, args.jpg_dir)


if __name__ == '__main__':
    main()
