"""Cleans the tourists' colour textures in 3D, across their UV seams: a base model and all its look-alikes together.

What was wrong. The atlases are cut into hundreds of small islands with saw-tooth rims, and the bake left the wrong
colour along those rims; the painted hairlines, brows and bikini hems are rough. On the model the hairline and brows
came out torn, with pale or dark outlines wherever two colours meet. The look-alikes (make_variants.py) were
recoloured texel by texel with hard masks, which added pale fringes round the new hair colour and smudges of hair
colour in the irises.

What this does.
 1. Every texel of the base texture is traced to its spot on the body (texel_map.rasterize).
 2. The base's colours are grouped into materials (skin, hair, each fabric, lips...) by k-means, near-identical
    groups merged; the painted eyes get materials of their own (white, iris).
 3. Round every spot on the body the share of each material is measured over a few millimetres of surface (a
    Gaussian over the nearest texels in 3D, from texels well inside their island only; the way the surface faces
    counts as distance, so a lock of hair and the cheek under it don't mix). Finer round the brows, eyes and mouth.
 4. The base texture's painting is kept exactly. (Repainting the islands' rims from that map, then only rim
    texels unlike every material near them, still tore the brows, lash tips and lens rims: the face is cut into so
    many small islands that those are mostly rim. The bake's rims turned out right; the seams were the gutter.)
    A look-alike is mended from the base: per material, the colour mapping make_variants.py applied is learnt
    (base -> look-alike, island interiors); where the look-alike is plainly off what that mapping gives the base
    there (its masks' misses: a red halter string on a teal bikini, skin colour in the hair, a dark hem) it is
    repainted so, an anti-aliased border as its two materials' mix. Everything else, and the painted eyes, brows
    and mouth, stays its own; hair colour in its irises is repainted iris colour.
 5. Every island is padded out into the empty atlas (no gutter colour in the mipmaps); saved as 4:4:4 JPEG (chroma
    subsampling drew coloured fringes along sharp colour edges).

python ArtSource/Tools/clean_character_textures.py <base.glb> [variant.glb ...] --out-dir <dir>
Inputs are read, never written: always clean from ArtSource/CharacterClean/Originals.
"""
import argparse
import io
import os
import sys
import time

import numpy as np
from scipy import ndimage
from scipy.spatial import cKDTree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from glb_io import Glb, load_image  # noqa: E402
from texel_map import rasterize  # noqa: E402


def to_linear(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def to_srgb(c):
    c = np.clip(c, 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)


def to_lab(c):
    lin = to_linear(np.clip(c, 0, 1))
    m = np.array([[0.4124, 0.3576, 0.1805], [0.2126, 0.7152, 0.0722], [0.0193, 0.1192, 0.9505]])
    xyz = lin @ m.T / np.array([0.95047, 1.0, 1.08883])
    f = np.where(xyz > 0.008856, np.cbrt(xyz), 7.787 * xyz + 16 / 116)
    return np.stack([116 * f[..., 1] - 16, 500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2])], axis=-1)


def luminance(lin):
    return lin @ np.array([0.2126, 0.7152, 0.0722])


def delta_e(a, b):
    return np.sqrt(((a - b) ** 2).sum(-1))


def kmeans(x, k, iters=25, seed=0):
    rng = np.random.default_rng(seed)
    centers = [x[rng.integers(len(x))]]
    for _ in range(1, k):  # k-means++: a small material (a bikini string) still gets a centre
        d = np.min(((x[:, None, :] - np.array(centers)[None]) ** 2).sum(-1), axis=1)
        centers.append(x[rng.choice(len(x), p=d / d.sum())])
    centers = np.array(centers)
    for _ in range(iters):
        label = np.argmin(((x[:, None, :] - centers[None]) ** 2).sum(-1), axis=1)
        centers = np.array([x[label == j].mean(0) if np.any(label == j) else centers[j] for j in range(k)])
    return centers


def merge_close(centers, limit):
    """Groups of the same colour in another shade (shading counts less than hue) become one material."""
    parent = list(range(len(centers)))

    def root(i):
        while parent[i] != i:
            i = parent[i]
        return i
    for i in range(len(centers)):
        for j in range(i + 1, len(centers)):
            d = centers[i] - centers[j]
            if np.sqrt((0.3 * d[0]) ** 2 + d[1] ** 2 + d[2] ** 2) < limit:
                parent[root(j)] = root(i)
    roots = sorted({root(i) for i in range(len(centers))})
    return np.array([roots.index(root(i)) for i in range(len(centers))]), len(roots)


def island_ids(tri_id, triangles, uv):
    """Island per texel: triangles sharing a UV edge belong together."""
    parent = np.arange(len(triangles))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i
    q = np.round(uv * 16384).astype(np.int64)
    seen = {}
    for t, tri in enumerate(triangles):
        for e in range(3):
            a, b = q[tri[e]], q[tri[(e + 1) % 3]]
            ka, kb = (int(a[0]), int(a[1])), (int(b[0]), int(b[1]))
            key = (ka, kb) if ka < kb else (kb, ka)
            other = seen.setdefault(key, t)
            if other != t:
                ra, rb = find(t), find(other)
                if ra != rb:
                    parent[ra] = rb
    roots = np.array([find(i) for i in range(len(triangles))])
    out = np.full(tri_id.shape, -1, np.int64)
    m = tri_id >= 0
    out[m] = roots[tri_id[m]]
    return out


class Field:
    """Per-material share and colour round any spot on the body: a Gaussian over the nearest trustworthy texels in 3D
    (the way the surface faces counts as distance too, so a lock of hair and the cheek under it don't mix)."""

    FACING = 0.008  # metres of distance per unit of normal difference

    def __init__(self, positions, normals, labels, materials, radius, k=40, keep_every=1):
        pick = np.arange(0, len(positions), keep_every)
        self.labels = labels[pick]
        self.index = pick
        self.tree = cKDTree(np.concatenate([positions[pick], normals[pick] * self.FACING], 1))
        self.materials = materials
        self.radius = radius
        self.k = k

    def shares(self, positions, normals, colors=None, chunk=20000):
        """Shares (n x materials) and, given the trusted texels' colours, the local colour of each material."""
        n = len(positions)
        M = self.materials
        share = np.zeros((n, M))
        local = np.zeros((n, M, 3)) if colors is not None else None
        csrc = colors[self.index] if colors is not None else None
        for s in range(0, n, chunk):
            e = min(n, s + chunk)
            q = np.concatenate([positions[s:e], normals[s:e] * self.FACING], 1)
            d, idx = self.tree.query(q, k=self.k, distance_upper_bound=self.radius * 2.5, workers=-1)
            valid = idx < len(self.labels)
            idx = np.where(valid, idx, 0)
            w = np.where(valid, np.exp(-0.5 * (d / self.radius) ** 2), 0.0)
            lab = self.labels[idx]
            cols = csrc[idx] if csrc is not None else None
            for m in range(M):
                wm = w * (lab == m)
                share[s:e, m] = wm.sum(1)
                if cols is not None:
                    local[s:e, m] = np.einsum('nk,nkc->nc', wm, cols)
        if local is not None:
            local /= np.maximum(share[..., None], 1e-9)
        return share, local


def find_face(pos, nrm, lab, top):
    """Eye centres (from the whites of the painted eyes), or None. Sunglasses pushed up on the head are white too:
    the eyes are the lowest white patches on the front of the face, a pair either side."""
    front = (nrm[:, 2] > 0.3) & (pos[:, 1] > top - 0.24) & (pos[:, 1] < top - 0.07) & (np.abs(pos[:, 0]) < 0.075)
    if front.sum() < 200:
        return None
    zf = np.percentile(pos[front][:, 2], 90)
    white = front & (lab[:, 0] > 74) & (np.abs(lab[:, 1]) < 10) & (np.abs(lab[:, 2]) < 18) & (pos[:, 2] > zf - 0.045)
    eyes = []
    for side in (-1, 1):
        s = pos[white & (pos[:, 0] * side > 0.008)]
        if len(s) < 20:
            return None
        ys = np.sort(s[:, 1])
        c = np.median(s[s[:, 1] < ys[int(0.5 * len(ys))] + 0.01], 0)
        s = s[np.linalg.norm(s[:, :2] - c[:2], axis=1) < 0.025]
        if len(s) < 15:
            return None
        eyes.append(np.median(s, 0))
    if abs(eyes[0][1] - eyes[1][1]) > 0.02:
        return None
    return {'eyes': eyes, 'eye_y': 0.5 * (eyes[0][1] + eyes[1][1]), 'z': 0.5 * (eyes[0][2] + eyes[1][2])}


def face_zones(pos, nrm, face):
    """Painted eyes and mouth (keep their painting), and the brows (cleaned on a fine scale)."""
    n = len(pos)
    eye, mouth, brow = np.zeros(n, bool), np.zeros(n, bool), np.zeros(n, bool)
    if face is None:
        return eye, mouth, brow
    front = nrm[:, 2] > 0.1
    for c in face['eyes']:
        # (Wide: the lashes and eyeliner reach well past the whites at the outer corner.)
        dx, dy, dz = (pos[:, 0] - c[0]) / 0.034, (pos[:, 1] - c[1]) / 0.022, (pos[:, 2] - c[2]) / 0.035
        eye |= front & (dx * dx + dy * dy + dz * dz < 1.0)
        dx, dy = (pos[:, 0] - c[0]) / 0.036, (pos[:, 1] - (c[1] + 0.028)) / 0.018
        brow |= front & (dx * dx + dy * dy < 1.0) & (np.abs(pos[:, 2] - c[2]) < 0.035)
    dx, dy = pos[:, 0] / 0.038, (pos[:, 1] - (face['eye_y'] - 0.064)) / 0.022
    mouth |= front & (dx * dx + dy * dy < 1.0) & (pos[:, 2] > face['z'] - 0.02)
    brow &= ~eye
    return eye, mouth, brow


def features(srgb):
    return np.concatenate([srgb, srgb * srgb, np.ones((len(srgb), 1))], 1)


def fit_mapping(src, dst):
    """A smooth colour mapping src -> dst (sRGB, n x 3), and its fit (median, 90th percentile dE). The bits the
    recolour's masks missed must not shape it: they are often a slightly different shade of the same material, so a
    curved mapping would learn them too. So first a straight mapping, twice without its worst 40%, then the curved
    one on what that keeps. It is only used on the colours it was learnt from (outside them it drew green specks)."""
    f = features(src)
    dst_lab = to_lab(dst)
    fit = np.ones(len(src), bool)
    for _ in range(2):
        c, *_ = np.linalg.lstsq(f[fit][:, [0, 1, 2, 6]], dst[fit], rcond=None)
        err = delta_e(to_lab(np.clip(f[:, [0, 1, 2, 6]] @ c, 0, 1)), dst_lab)
        fit = err <= np.percentile(err, 60)
    c, *_ = np.linalg.lstsq(f[fit], dst[fit], rcond=None)
    err = delta_e(to_lab(np.clip(f @ c, 0, 1)), dst_lab)
    box = np.percentile(src, [0.5, 99.5], axis=0)

    def mapping(srgb):
        return np.clip(features(np.clip(srgb, box[0], box[1])) @ c, 0, 1)
    return mapping, np.median(err), np.percentile(err, 90)


def jpeg_bytes(rgb):
    from PIL import Image
    buf = io.BytesIO()
    Image.fromarray(np.clip(rgb * 255.0 + 0.5, 0, 255).astype(np.uint8), 'RGB').save(buf, 'JPEG', quality=94, subsampling=0, optimize=True)
    return buf.getvalue()


class Cleaner:
    """The base model's materials and where each one is, cleaned; then any texture on the same UVs (the base, or a
    look-alike) repainted where it disagrees with that, from its own colours."""

    def __init__(self, base_path, radius=0.005, fine=0.0018, k=14, merge=11.0, log=print):
        t0 = time.time()
        self.log = log
        g = Glb(base_path)
        prim = g.primitive()
        P = g.accessor(prim['attributes']['POSITION']).astype(np.float64)
        N = g.accessor(prim['attributes']['NORMAL']).astype(np.float64)
        UV = g.accessor(prim['attributes']['TEXCOORD_0']).astype(np.float64)
        T = g.accessor(prim['indices']).reshape(-1, 3)
        src = load_image(g.image_bytes(g.base_color_image())).astype(np.float64)
        H, W = src.shape[:2]
        self.shape = (H, W)
        cov, pos, nrm, tid = rasterize(UV, P, N, T, W, H)
        isl = island_ids(tid, T, UV)
        edge = np.zeros((H, W), bool)
        for dy, dx in ((0, 1), (1, 0), (0, -1), (-1, 0), (1, 1), (-1, -1), (1, -1), (-1, 1)):
            edge |= np.roll(np.roll(isl, dy, 0), dx, 1) != isl
        trusted = cov & (ndimage.distance_transform_edt(~edge) > 3.0)
        self.cov = cov
        self.pad = ndimage.distance_transform_edt(~cov, return_indices=True)[1]
        self.pc, self.nc = pc, nc = pos[cov], nrm[cov]
        self.trusted = tr = trusted[cov]
        base = src[cov]
        self.base_srgb = base
        lab = to_lab(base)
        top = P[:, 1].max()

        # Materials.
        rng = np.random.default_rng(1)
        pick = rng.choice(np.nonzero(tr)[0], min(80000, tr.sum()), replace=False)
        centers = kmeans(lab[pick], k)
        group, materials = merge_close(centers, merge)
        label = group[np.concatenate([np.argmin(((lab[s:s + 200000, None, :] - centers[None]) ** 2).sum(-1), axis=1)
                                      for s in range(0, len(lab), 200000)])]
        face = find_face(pc, nc, lab, top)
        eye, mouth, brow = face_zones(pc, nc, face)
        self.skin_mat = -1
        if face is not None:
            # The painted eyes: whites and irises are materials of their own (a brown iris is the colour of brown hair).
            white = eye & (lab[:, 0] > 70) & (np.hypot(lab[:, 1], lab[:, 2]) < 22)
            self.skin_mat = np.bincount(label[mouth | brow], minlength=materials).argmax()
            iris = eye & ~white & (label != self.skin_mat)
            label[white] = materials
            label[iris] = materials + 1
            materials += 2
        self.materials = materials
        self.face = face
        self.eye, self.mouth, self.brow = eye, mouth, brow
        self.detail = detail = eye | mouth | brow

        # Where each material is: shares over a few millimetres (finer round the face's features), from the base.
        tri = np.nonzero(tr)[0]
        self.tri = tri
        self.coarse = Field(pc[tri], nc[tri], label[tri], materials, radius, k=40, keep_every=3)
        self.fine_f = Field(pc[tri], nc[tri], label[tri], materials, fine, k=24, keep_every=1)
        share, _ = self.coarse.shares(pc, nc)
        if detail.any():
            share[detail], _ = self.fine_f.shares(pc[detail], nc[detail])
        total = share.sum(1, keepdims=True)
        self.known = total[:, 0] > 1e-6
        share = np.where(self.known[:, None], share / np.maximum(total, 1e-9), 0)
        self.share = share
        self.best = share.argmax(1)
        self.top = share[np.arange(len(label)), self.best]
        self.label = label
        wgt = share ** 4
        self.wgt = wgt / np.maximum(wgt.sum(1, keepdims=True), 1e-9)
        # How far each iris texel of the base is from the iris colour round it (pupils, highlights): a look-alike's
        # texel only counts as a smudge where the base's was ordinary iris.
        self.base_iris_dev = np.full(len(label), 99.0)
        if face is not None:
            e = np.nonzero(eye)[0]
            _, loc = self.fine_f.shares(pc[e], nc[e], colors=to_linear(base)[tri])
            self.base_iris_dev[e] = delta_e(lab[e], to_lab(to_srgb(loc[:, materials - 1])))
            # The hair: what covers the sides of the head beside the eyes.
            side = (np.abs(pc[:, 0]) > 0.075) & (np.abs(pc[:, 0]) < 0.12) & (np.abs(pc[:, 1] - face['eye_y'] - 0.01) < 0.03)
            counts = np.bincount(label[side & tr], minlength=materials)
            self.hair_mat = int(counts.argmax()) if counts.max() > 200 and counts.argmax() != self.skin_mat else -1
            # The iris as a whole (its light lower crescent too, which a look-alike's recolour took for hair): the dark,
            # non-white painting inside the eye. make_variants.py coloured it iris colour x brightness.
            skin_l = np.median(lab[(label == self.skin_mat) & (mouth | brow)][:, 0]) if (mouth | brow).any() else 60.0
            inner = np.zeros(len(label), bool)
            for c in face['eyes']:
                dx, dy = (pc[:, 0] - c[0]) / 0.021, (pc[:, 1] - c[1]) / 0.015
                inner |= dx * dx + dy * dy < 1.0
            self.iris_region = eye & inner & (label != materials - 2) & (lab[:, 0] < skin_l - 6)
            # The painting inside each eye's opening (iris, its lower crescent, lashes): within 1 cm of the eye's
            # whites and no further out than their tips (hair can reach the outer corner), and not skin. Where a
            # look-alike has hair colour there, the recolour took it for hair: the red base's lower iris is the
            # same brown as her hair. (Neither the iris's colour nor its place finds it alone: these cartoon eyes
            # glance sideways, so the iris isn't in the middle of the whites.)
            skin_lab = lab[label == self.skin_mat].mean(0)
            whites = label == materials - 2
            self.eye_paint = np.zeros(len(label), bool)
            for c in face['eyes']:
                w = np.nonzero(whites & (np.hypot(pc[:, 0] - c[0], pc[:, 1] - c[1]) < 0.025))[0]
                if len(w) < 30:
                    continue
                # (On the eye's own surface, near a plane through its whites (the iris bulges up to a centimetre):
                # hair locks hang two centimetres in front of the temple.)
                plane, *_ = np.linalg.lstsq(np.c_[pc[w, :2], np.ones(len(w))], pc[w, 2], rcond=None)
                surface = np.abs(np.c_[pc[:, :2], np.ones(len(pc))] @ plane - pc[:, 2]) < 0.013
                inside = ((pc[:, 0] >= pc[w, 0].min()) & (pc[:, 0] <= pc[w, 0].max()) &
                          (pc[:, 1] >= pc[w, 1].min() - 0.003) & (pc[:, 1] <= pc[w, 1].max() + 0.003))
                cand = np.nonzero(eye & ~whites & surface & inside & (nc[:, 2] > 0.3))[0]
                dist, _ = cKDTree(pc[w]).query(pc[cand], distance_upper_bound=0.011)
                self.eye_paint[cand[np.isfinite(dist)]] = True
            self.eye_paint &= delta_e(lab, skin_lab) > 12.0
            self.iris_shade = np.clip(luminance(to_linear(base)) / 0.12, 0.15, 1.6)
        else:
            self.hair_mat = -1
            self.iris_region = np.zeros(len(label), bool)
            self.eye_paint = self.iris_region
        log(f'[clean] {os.path.basename(base_path)}: {W}x{H}, {materials} materials, '
            f'{"eyes found" if face is not None else "NO EYES FOUND"}, {time.time() - t0:.0f} s')

    def clean(self, rgb, name, variant=True):
        """A texture on these UVs as it is (every painted texel kept: repainting the islands' rims from the 3D map
        tore the brows, lashes and lens rims, which are cut into many small islands), but for a look-alike's irises."""
        t0 = time.time()
        c = rgb[self.cov]
        lin = to_linear(c)
        lab = to_lab(c)
        out = lin.copy()
        # A look-alike's irises: its recolour took the iris's light lower crescent (and other bits the colour of the
        # base's hair) for hair. make_variants.py coloured an iris iris-colour x brightness: every texel of the iris
        # that is far from that and close to the hair colour gets it.
        smudge = np.zeros(len(lin), bool)
        if variant and self.face is not None and self.hair_mat >= 0:
            region = self.iris_region & self.trusted
            if region.sum() > 50:
                # (Its hair colour over the whole head: there is no hair right by the eyes to take it from.)
                hair = np.median(lin[(self.label == self.hair_mat) & self.trusted], axis=0)
                k = np.median(lin[region] / self.iris_shade[region, None], axis=0)
                predicted = k[None] * self.iris_shade[:, None]
                hairy = delta_e(lab, to_lab(to_srgb(hair[None]))) < 18.0
                smudge = self.eye_paint & hairy & (delta_e(lab, to_lab(to_srgb(predicted))) > 12.0)
                out[smudge] = predicted[smudge]
        if variant:
            self.log(f'[clean]   {name}: {smudge.sum()} iris smudges, {time.time() - t0:.0f} s')
        return np.clip(out, 0, 1)

    def resynth(self, rgb, base_clean_lin, name):
        """A look-alike mended from the base. make_variants.py recoloured each material (skin, hair, eyes, outfit)
        by a smooth colour mapping, but its masks missed bits: a red halter string left on a teal bikini, a dark hem,
        skin colour in the hair. Per material, the mapping base -> look-alike is learnt from the island interiors;
        texels plainly off it are repainted from the base through it."""
        t0 = time.time()
        own = self.clean(rgb, name, variant=True)
        b_raw = self.base_srgb
        v = rgb[self.cov]
        v_lab = to_lab(v)
        b_clean = to_srgb(base_clean_lin)
        b_lab = to_lab(b_clean)
        M = self.materials
        rng = np.random.default_rng(2)
        coef = [None] * M
        centre = np.zeros((M, 3))
        centre_lin = np.zeros((M, 3))
        notes = []
        for m in range(M):
            sel = np.nonzero(self.trusted & (self.label == m) & (self.share[:, m] > 0.95))[0]
            if len(sel):
                centre[m] = to_lab(b_raw[sel]).mean(0)
                centre_lin[m] = to_linear(b_raw[sel]).mean(0)
            if len(sel) < 300:
                notes.append(f'{m}:few')
                continue
            if len(sel) > 40000:
                sel = rng.choice(sel, 40000, replace=False)
            mapping, med, p90 = fit_mapping(b_raw[sel], v[sel])
            good = med < 3.0 and p90 < 8.0
            notes.append(f'{m}:{med:.1f}/{p90:.1f}{"" if good else "!"}')
            if good:
                coef[m] = mapping

        def fmap(m, srgb):
            return coef[m](srgb)
        # Which material a base texel is: present right there and close in colour. (On the fine scale: a halter
        # string is 2-3 mm wide, so on the coarse one it is never plainly bikini.)
        if not hasattr(self, 'share_fine'):
            f, _ = self.fine_f.shares(self.pc, self.nc)
            self.share_fine = f / np.maximum(f.sum(1, keepdims=True), 1e-9)
        p = np.empty_like(self.share)
        for m in range(M):
            p[:, m] = self.share_fine[:, m] * np.exp(-((b_lab - centre[m]) ** 2).sum(-1) / (2 * 12.0 ** 2))
        good_m = np.array([c is not None for c in coef])
        total = p.sum(1)
        top = np.argmax(np.where(good_m[None], p, -1.0), axis=1)
        rows = np.arange(len(v))
        # Only where the base is plainly one material with a mapping that fits, and the look-alike is plainly off what
        # that mapping gives (a halter string left red, skin colour in the hair). Everything the look-alike already
        # has right stays its own, texel for texel: replacing it all lost nipples, lip colour and the brows' colour,
        # whose material is decided by colour and so was sometimes the wrong one. The painted eyes, brows and mouth
        # are never touched (the iris was recoloured by brightness, not mapped; brows half hair, half not).
        features = self.eye | self.brow | self.mouth
        confident = (good_m[top] & (p[rows, top] > 0.9 * np.maximum(total, 1e-30)) & (total > 1e-12)
                     & (delta_e(b_lab, centre[top]) < 15.0))
        pred = np.zeros((len(v), 3))
        for m in np.nonzero(good_m)[0]:
            k = confident & (top == m)
            pred[k] = fmap(m, b_clean[k])
        use = self.known & ~features & confident & (delta_e(to_lab(pred), v_lab) > 12.0)
        # Only whole patches (a string, a gap in the hair): lone specks are painted details no material explains
        # (a nipple, a speck of stubble), which the look-alike has right.
        H, W = self.shape
        img = np.zeros((H, W), bool)
        img[self.cov] = use
        blobs, count = ndimage.label(img, structure=np.ones((3, 3)))
        sizes = np.bincount(blobs.ravel(), minlength=count + 1)
        img = (sizes >= 40)[blobs] & img
        use = img[self.cov]
        near = ndimage.binary_dilation(img, iterations=2)[self.cov]
        out = own.copy()
        out[use] = to_linear(pred[use])
        # The rest are mostly anti-aliased borders (bikini into skin, hair into skin: colours of their own, which
        # the recolour treated half one way, half the other). Each is split into the two pure materials round it
        # whose mix it is, and each part takes its own material's mapping (the texel's shading kept).
        sub = np.nonzero(self.known & ~features & ~confident)[0]
        mixed = np.zeros(len(v), bool)
        if len(sub) and good_m.sum() >= 1:
            _, loc = self.coarse.shares(self.pc[sub], self.nc[sub], colors=to_linear(b_raw)[self.tri])
            x = base_clean_lin[sub]
            x_lab = b_lab[sub]
            own_lab = to_lab(to_srgb(own[sub]))

            def unmix(ranked, valid, colour):
                """Best mix of two of three candidate materials (colour(rows, m) = their pure colours there)."""
                rows = np.arange(len(ranked))
                best_res = np.full(len(rows), np.inf)
                best_score = np.full(len(rows), np.inf)
                best = np.zeros((len(rows), 3))
                for i in range(3):
                    for j in range(i, 3):
                        a, bm = ranked[:, i], ranked[:, j]
                        ci, cj = colour(rows, a), colour(rows, bm)
                        d = ci - cj
                        alpha = np.clip(((x - cj) * d).sum(1) / np.maximum((d * d).sum(1), 1e-12), 0, 1)
                        recon = alpha[:, None] * ci + (1 - alpha[:, None]) * cj
                        res = delta_e(x_lab, to_lab(to_srgb(recon)))
                        res = np.where(valid[:, i] & valid[:, j], res, np.inf)
                        fi = np.zeros((len(rows), 3))
                        fj = np.zeros((len(rows), 3))
                        for m in np.unique(np.concatenate([a, bm])):
                            if not good_m[m]:
                                continue
                            ka, kb = a == m, bm == m
                            fi[ka] = to_linear(fmap(m, to_srgb(ci[ka])))
                            fj[kb] = to_linear(fmap(m, to_srgb(cj[kb])))
                        shade = np.clip(x / np.maximum(recon, 1e-4), 0.5, 2.0)
                        mapped = (alpha[:, None] * fi + (1 - alpha[:, None]) * fj) * shade
                        # Of the mixes that explain the base's colour, the one that gives what the look-alike has
                        # there (round the sunglasses hair is near in 3D too: a grey lens rim isn't hair and white).
                        score = res + 0.5 * delta_e(to_lab(to_srgb(np.clip(mapped, 0, 1))), own_lab)
                        better = score < best_score
                        best_score[better] = score[better]
                        best_res[better] = res[better]
                        best[better] = mapped[better]
                return best_res, best

            # The materials round it (their local colours), else (a thin halter string: no pure bikini texel near
            # enough in 3D) the three closest in colour (their colours over the whole body).
            share_good = np.where(good_m[None], self.share[sub], 0.0)
            ranked = np.argsort(-np.where(share_good > 0.02, share_good, -1.0), axis=1)[:, :3]
            best_res, best = unmix(ranked, np.take_along_axis(share_good, ranked, 1) > 0.02, lambda r, m: loc[r, m])
            dist = np.where(good_m[None], ((x_lab[:, None, :] - centre[None]) ** 2).sum(-1), np.inf)
            ranked = np.argsort(dist, axis=1)[:, :3]
            res2, best2 = unmix(ranked, np.isfinite(np.take_along_axis(dist, ranked, 1)), lambda r, m: centre_lin[m])
            second = (best_res >= 12.0) & (res2 < best_res)
            best_res[second], best[second] = res2[second], best2[second]
            # (Only the borders of the patches repainted above: elsewhere a colour the materials don't explain is
            # the look-alike's own painting.)
            ok = (best_res < 12.0) & (delta_e(to_lab(to_srgb(np.clip(best, 0, 1))), own_lab) > 12.0) & near[sub]
            out[sub[ok]] = np.clip(best[ok], 0, 1)
            mixed[sub[ok]] = True
        self.log(f'[clean]   {name}: repainted from the base {100 * use.mean():.2f}% + {100 * mixed.mean():.2f}% '
                 f'unmixed borders; '
                 f'(fit dE median/p90 per material {" ".join(notes)}), {time.time() - t0:.0f} s')
        return out

    def smooth_face(self, lin, name, base_lin=None):
        """The face and neck smooth: where hair meets skin a clean anti-aliased line (no saw teeth of hair in the
        forehead, pale fringes, blobs of hair colour), specks and blotches off the skin, the head a touch sharper. Only
        texels that are skin or hair there; the painted eyes, brows and mouth, sunglasses, hair ties... are kept.
        A look-alike (base_lin: the base's texture) first has its head's skin and hair put right from the base:
        make_variants.py took the skin-coloured rim of the hair for hair and turned it a pale beige, which drew a
        pale outline round the whole face."""
        if self.face is None or self.skin_mat < 0 or self.hair_mat < 0:
            return lin
        t0 = time.time()
        region = self.known & (self.pc[:, 1] > self.face['eye_y'] - 0.22)
        idx = np.nonzero(region)[0]
        # Skin, hair or other, by the base's colour: the hairline's own in-between colours (which the clustering
        # made materials of their own) are skin or hair by which they're nearer, anything off the line between the
        # two is other. All texels of the head count (not only island interiors: the face is cut into so many small
        # islands that its hairline is mostly rim).
        b_lab = to_lab(self.base_srgb[idx])
        lab_s = b_lab[self.label[idx] == self.skin_mat].mean(0)
        lab_h = b_lab[self.label[idx] == self.hair_mat].mean(0)
        d = lab_h - lab_s
        t = np.clip(((b_lab - lab_s) * d).sum(1) / (d * d).sum(), 0, 1)
        off_line = delta_e(b_lab, lab_s + t[:, None] * d) > 12.0
        kind = np.where(off_line, 2, np.where(t < 0.5, 0, 1))
        field = Field(self.pc[idx], self.nc[idx], kind, 3, 0.0025, k=30)
        share, _ = field.shares(self.pc[idx], self.nc[idx])
        total = np.maximum(share.sum(1), 1e-9)
        sk, hr = share[:, 0] / total, share[:, 1] / total
        keep = (self.eye | self.brow | self.mouth)[idx]
        pure = (sk + hr > 0.95) & ~keep
        # (A look-alike's brows are rebuilt with the hairline: the base's are hair coloured, and the recolour left a
        # pale outline round them too.)
        keep_v = (self.eye | self.mouth)[idx]
        mended = 0
        if base_lin is not None:
            # Skin texels as the base's skin through the look-alike's skin mapping, hair as its hair through its hair
            # mapping (both learnt where the head is plainly skin or plainly hair), wherever the look-alike is off.
            b = to_srgb(base_lin[idx])
            v = to_srgb(lin[idx])
            v_lab = to_lab(v)
            fixed = v.copy()
            maps = [None, None]
            for k_, sh in ((0, sk), (1, hr)):
                learn = (kind == k_) & (sh > 0.98) & ~keep_v
                if learn.sum() < 300:
                    continue
                mapping, med, _ = fit_mapping(b[learn], v[learn])
                if med > 4.0:
                    continue
                maps[k_] = mapping
                these = (kind == k_) & ~keep_v
                pred = mapping(b[these])
                off = delta_e(to_lab(pred), v_lab[these]) > 10.0
                rows = np.nonzero(these)[0][off]
                fixed[rows] = pred[off]
                mended += len(rows)
            lin = lin.copy()
            lin[idx] = to_linear(fixed)
        band = np.zeros(len(idx), bool)
        if base_lin is not None and maps[0] is not None and maps[1] is not None:
            # The hairline rebuilt from the base's, texel for texel: the same mix of skin and hair the base has there
            # (its anti-aliasing), in the look-alike's skin and hair colours. The recolour's soft hair mask had left a
            # pale line there and round the brows; a mix of the real skin and hair can't be pale.
            _, b_local = field.shares(self.pc[idx], self.nc[idx], colors=base_lin[idx])
            bs, bh = b_local[:, 0], b_local[:, 1]
            x = base_lin[idx]
            d = bh - bs
            alpha = ((x - bs) * d).sum(1) / np.maximum((d * d).sum(1), 1e-12)
            band = (kind < 2) & ~keep_v & (sk > 0.02) & (hr > 0.02) & (alpha > 0.04) & (alpha < 0.96)
            a_ = np.clip(alpha[band], 0, 1)[:, None]
            skin_v = to_linear(maps[0](to_srgb(bs[band])))
            hair_v = to_linear(maps[1](to_srgb(bh[band])))
            recon = (1 - a_) * bs[band] + a_ * bh[band]
            shade = np.clip(x[band] / np.maximum(recon, 1e-4), 0.7, 1.4)
            lin[idx[band]] = ((1 - a_) * skin_v + a_ * hair_v) * shade
        _, local = field.shares(self.pc[idx], self.nc[idx], colors=lin[idx])
        ws = sk ** 4 / np.maximum(sk ** 4 + hr ** 4, 1e-12)
        skin_c = local[:, 0]
        x = lin[idx]
        out = lin.copy()
        res = x.copy()
        # Skin: specks and blotches go; the rest keeps its own painting (softening it too made the faces blurry).
        on_skin = pure & (ws > 0.98) & ~band
        off = delta_e(to_lab(to_srgb(x)), to_lab(to_srgb(skin_c))) > 7.0
        on_skin &= off
        res[on_skin] = skin_c[on_skin]
        out[idx] = res
        # The whole head a touch sharper (lashes, brows, lips): its texture is about a millimetre a texel, so close
        # up (CPR, the kiss of life) it is magnified and soft. An unsharp mask over the surface in 3D, not the atlas,
        # so it never reaches across a UV seam.
        tree = cKDTree(self.pc[idx])
        dist, near = tree.query(self.pc[idx], k=16, distance_upper_bound=0.002, workers=-1)
        ok = np.isfinite(dist)
        w = np.where(ok, np.exp(-0.5 * (np.where(ok, dist, 0) / 0.0008) ** 2), 0.0)
        cur = out[idx]
        blur = (w[..., None] * cur[np.where(ok, near, 0)]).sum(1) / np.maximum(w.sum(1, keepdims=True), 1e-9)
        out[idx] = cur + 0.6 * (cur - blur)
        self.log(f'[clean]   {name}: face smoothed ({mended} skin/hair texels mended, {on_skin.sum()} skin specks, '
                 f'{band.sum()} hairline texels, sharpened), '
                 f'{time.time() - t0:.0f} s')
        return np.clip(out, 0, 1)

    def image(self, lin_cov):
        H, W = self.shape
        out = np.zeros((H, W, 3))
        out[self.cov] = to_srgb(lin_cov)
        iy, ix = self.pad
        return out[iy, ix]


def write_glb(src_path, out_path, rgb):
    g = Glb(src_path)
    g.replace_image(g.base_color_image(), jpeg_bytes(rgb.astype(np.float32)), 'image/jpeg')
    g.save(out_path)


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('base')
    ap.add_argument('variants', nargs='*')
    ap.add_argument('--out-dir', required=True)
    ap.add_argument('--radius', type=float, default=0.005)
    a = ap.parse_args()
    cleaner = Cleaner(a.base, radius=a.radius)
    os.makedirs(a.out_dir, exist_ok=True)
    base_clean = None
    for path in [a.base] + a.variants:
        vg = Glb(path)
        rgb = load_image(vg.image_bytes(vg.base_color_image())).astype(np.float64)
        name = os.path.basename(path)
        if base_clean is None:
            base_clean = cleaner.clean(rgb, name, variant=False)
            out = cleaner.smooth_face(base_clean, name)
        else:
            out = cleaner.smooth_face(cleaner.resynth(rgb, base_clean, name), name, base_lin=base_clean)
        write_glb(path, os.path.join(a.out_dir, name), cleaner.image(out))
