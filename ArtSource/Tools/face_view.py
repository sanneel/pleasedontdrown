"""Quick look at a character's texture as it lies on the body, without Unity: every texel splatted at its 3D spot,
seen orthographically from the front (or another side). For checking texture passes.

python ArtSource/Tools/face_view.py <a.glb>[@texture.png] [<b.glb> ...] --out view.png [--region face|head|body|torso] [--side front|left|back]
"""
import argparse
import sys

import numpy as np

sys.path.insert(0, __file__.replace('\\', '/').rsplit('/', 1)[0])
from glb_io import Glb, load_image  # noqa: E402
from texel_map import rasterize  # noqa: E402


def view(path, region='face', side='front', px=900, texture=None):
    g = Glb(path)
    prim = g.primitive()
    P = g.accessor(prim['attributes']['POSITION']).astype(np.float64)
    N = g.accessor(prim['attributes']['NORMAL']).astype(np.float64)
    UV = g.accessor(prim['attributes']['TEXCOORD_0']).astype(np.float64)
    T = g.accessor(prim['indices']).reshape(-1, 3)
    img = load_image(open(texture, 'rb').read() if texture else g.image_bytes(g.base_color_image()))
    H, W = img.shape[:2]
    cov, pos, nrm, _ = rasterize(UV, P, N, T, W, H)
    top = P[:, 1].max()
    p, n, c = pos[cov], nrm[cov], img[cov]
    rot = {'front': (0, 1, 2, 1), 'back': (0, 1, 2, -1), 'left': (2, 1, 0, -1), 'right': (2, 1, 0, 1)}[side]
    ax, ay, az, sgn = rot
    x, y, depth = p[:, ax] * (sgn if side in ('front', 'right') else -sgn), p[:, ay], p[:, az] * sgn
    box = {'face': (-0.11, 0.11, top - 0.26, top + 0.01), 'head': (-0.16, 0.16, top - 0.34, top + 0.02),
           'torso': (-0.25, 0.25, top - 0.75, top - 0.2), 'body': (-0.5, 0.5, -0.02, top + 0.02)}[region]
    x0, x1, y0, y1 = box
    sel = (x > x0) & (x < x1) & (y > y0) & (y < y1)
    scale = px / (x1 - x0)
    w, h = px, int((y1 - y0) * scale)
    out = np.zeros((h, w, 3))
    ix = ((x[sel] - x0) * scale).astype(int).clip(0, w - 1)
    iy = ((y1 - y[sel]) * scale).astype(int).clip(0, h - 1)
    d, col = depth[sel], c[sel]
    # Each texel a small square (no holes for the back to show through), nearest drawn last.
    r = max(1, int(round(0.0011 * scale / 2)))
    offs = [(oy, ox) for oy in range(-r, r + 1) for ox in range(-r, r + 1)]
    yy = np.concatenate([(iy + oy).clip(0, h - 1) for oy, _ in offs])
    xx = np.concatenate([(ix + ox).clip(0, w - 1) for _, ox in offs])
    dd = np.tile(d, len(offs))
    cc = np.tile(col, (len(offs), 1))
    order = np.argsort(dd, kind='stable')
    out[yy[order], xx[order]] = cc[order]
    return out


if __name__ == '__main__':
    from PIL import Image
    ap = argparse.ArgumentParser()
    ap.add_argument('glbs', nargs='+')
    ap.add_argument('--out', required=True)
    ap.add_argument('--region', default='face')
    ap.add_argument('--side', default='front')
    ap.add_argument('--px', type=int, default=900)
    a = ap.parse_args()
    views = [view(gp.split('@')[0], a.region, a.side, a.px, gp.split('@')[1] if '@' in gp else None) for gp in a.glbs]
    Image.fromarray((np.clip(np.concatenate(views, 1), 0, 1) * 255).astype(np.uint8)).save(a.out)
