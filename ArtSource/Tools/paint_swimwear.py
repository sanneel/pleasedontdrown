"""Dresses a beach woman in real swimwear, painted onto her own texture (a copy: skeleton, skin weights and shape stay).

blender -b --factory-startup -P ArtSource/Tools/paint_swimwear.py -- <body.glb> <colour in.jpg> <colour out.jpg> <style> [--preview <png prefix>]
  style: lola | sporty | ruffle | belted | rash | retro | tankini | probe (only the landmarks and a preview)

Sandro (2026-10-19): "girls are wearing very bad underwear". The Tripo girls came with thin painted string bikinis (a
thong at the back). This paints over them:
  * the old bikini is taken off first: every texel of it (and its colour bleeding into the skin round it) is filled in
    smoothly from the skin all round it;
  * then the new suit, shaped in body space (every texel is traced back to the spot on the body it colours, as in
    paint_outfit.py), so its edges are clean lines on the body, the same across UV seams and on all the look-alikes;
  * cloth keeps a little of the body's own shading (folds, the curve of the bust), gets a darker line along its edges
    (the edge of the fabric lying on the skin), a light piping or trim, seams, and patterns (stripes, dots, bands);
  * nothing is added to the mesh: the bust jiggle, CPR, the kiss of life and swimming move it exactly as before, and
    with nothing hanging off it nothing can stretch or tear with the legs apart or the arms up.
The bodies' colour textures live beside their GLBs (Art/Characters/Textures/<body>_color.jpg); run it on the untouched
originals kept in ArtSource/Swimwear/src (it is not meant to paint over its own work). ArtSource/Swimwear/paint_all.sh
dresses them all.
"""
import math
import sys

import bpy
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
SRC, TEX_IN, TEX_OUT, STYLE = argv[0], argv[1], argv[2], argv[3]
PREVIEW = argv[argv.index('--preview') + 1] if '--preview' in argv else ''


def log(*a):
    print('[swim]', *a, flush=True)


def to_linear(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def to_srgb(c):
    c = np.clip(c, 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)


def smooth(u):
    u = np.clip(u, 0.0, 1.0)
    return u * u * (3 - 2 * u)


def srgb(c):
    return np.array(c, dtype=np.float32) / 255.0


# ------------------------------------------------------------------ load

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
body = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
rig = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
for o in list(bpy.context.scene.objects):
    if o not in (body, rig):
        bpy.data.objects.remove(o, do_unlink=True)
mat = body.data.materials[0]
tex_node = next(n for n in mat.node_tree.nodes if n.type == 'TEX_IMAGE')
base_img = bpy.data.images.load(TEX_IN)
base_img.colorspace_settings.name = 'sRGB'
tex_node.image = base_img
TW, TH = base_img.size
px = np.empty(TW * TH * 4, dtype=np.float32)
base_img.pixels.foreach_get(px)
rgb = px.reshape(-1, 4)[:, :3].copy()
del px
lin = to_linear(rgb).astype(np.float32)
Y = lin @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)

M = body.matrix_world
co = np.array([tuple(M @ v.co) for v in body.data.vertices])
joint = {b.name: (np.array(tuple(rig.matrix_world @ b.head_local)), np.array(tuple(rig.matrix_world @ b.tail_local)))
         for b in rig.data.bones}
H = float(co[:, 2].max())
J = lambda name: joint[name][0]
neck_z, chest_z, spine_z, hips_z = J('Neck')[2], J('Chest')[2], J('Spine')[2], J('Hips')[2]
log(f'texture {TW}x{TH}, height {H:.3f}, neck {neck_z:.3f}, chest {chest_z:.3f}, spine {spine_z:.3f}, hips {hips_z:.3f}')
log('bones', sorted(joint))

# ------------------------------------------------------------------ texel -> body position, normal and part

groups = {g.index: g.name for g in body.vertex_groups}
names = sorted(set(groups.values()))
vlabel = np.zeros(len(co), dtype=np.int32)
for v in body.data.vertices:
    if v.groups:
        g = max(v.groups, key=lambda e: e.weight)
        vlabel[v.index] = names.index(groups[g.group])
RES = 2048
pos = np.full((RES, RES, 3), np.nan, dtype=np.float32)
nrm = np.zeros((RES, RES, 3), dtype=np.float32)
lab_map = np.full((RES, RES), -1, dtype=np.int32)
uv_layer = body.data.uv_layers.active.data
uvs = np.empty(len(uv_layer) * 2)
uv_layer.foreach_get('uv', uvs)
uvs = uvs.reshape(-1, 2)
# Hair, found on the mesh (its strands hang over the back and the shoulders in front of the skin, and blonde hair is
# near the skin's colour, so a texel's colour alone won't do): each vertex's colour from the texture; hair-coloured
# vertices on the head and anything joined to them through hair-coloured vertices; then whole triangles.
loop_v = np.empty(len(body.data.loops), dtype=np.int64)
body.data.loops.foreach_get('vertex_index', loop_v)
tex_img = rgb.reshape(TH, TW, 3)
lx = np.clip((uvs[:, 0] % 1.0 * TW).astype(int), 0, TW - 1)
ly = np.clip((uvs[:, 1] % 1.0 * TH).astype(int), 0, TH - 1)
vcol = np.zeros((len(co), 3)); vn = np.zeros(len(co))
np.add.at(vcol, loop_v, tex_img[ly, lx]); np.add.at(vn, loop_v, 1)
vcol /= np.maximum(vn, 1)[:, None]
vgroup = np.array([names[i] for i in vlabel])
head_top = J('Head')[2] + 0.4 * (H - J('Head')[2])
hair_ref = np.median(vcol[co[:, 2] > head_top], axis=0)
skin_v = np.median(vcol[np.isin(vgroup, ['ForearmL', 'ForearmR'])], axis=0)
v_hairish = (np.linalg.norm(vcol - hair_ref[None], axis=1) < 0.16) & (np.linalg.norm(vcol - skin_v[None], axis=1) > 0.06)
edges = np.empty(len(body.data.edges) * 2, dtype=np.int64)
body.data.edges.foreach_get('vertices', edges)
edges = edges.reshape(-1, 2)
v_hair = v_hairish & (co[:, 2] > J('Head')[2])
for _ in range(400):                       # grown down the strands, through hair-coloured vertices only
    a, b = v_hair[edges[:, 0]], v_hair[edges[:, 1]]
    nxt = v_hair.copy()
    nxt[edges[a & ~b, 1]] = True
    nxt[edges[b & ~a, 0]] = True
    nxt &= v_hairish | (co[:, 2] > head_top)
    if (nxt == v_hair).all():
        break
    v_hair = nxt
log(f'hair vertices {int(v_hair.sum())} of {len(co)}')
hair_tri_map = np.zeros((2048, 2048), dtype=bool)
body.data.calc_loop_triangles()
for t in body.data.loop_triangles:
    l = t.loops
    p = uvs[[l[0], l[1], l[2]]] * RES
    x0, x1 = int(max(0, p[:, 0].min())), int(min(RES, p[:, 0].max() + 1))
    y0, y1 = int(max(0, p[:, 1].min())), int(min(RES, p[:, 1].max() + 1))
    if x1 <= x0 or y1 <= y0 or (x1 - x0) * (y1 - y0) > 80000:
        continue
    gx, gy = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
    (ax, ay), (bx, by), (cx, cy) = p
    d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
    if abs(d) < 1e-12:
        continue
    l1 = ((by - cy) * (gx - cx) + (cx - bx) * (gy - cy)) / d
    l2 = ((cy - ay) * (gx - cx) + (ax - cx) * (gy - cy)) / d
    l3 = 1 - l1 - l2
    inside = (l1 >= -0.02) & (l2 >= -0.02) & (l3 >= -0.02)
    if not inside.any():
        continue
    v = co[list(t.vertices)]
    pts = l1[..., None] * v[0] + l2[..., None] * v[1] + l3[..., None] * v[2]
    pos[y0:y1, x0:x1][inside] = pts[inside]
    corner = np.argmax(np.stack([l1, l2, l3]), axis=0)
    lab_map[y0:y1, x0:x1][inside] = vlabel[np.array(t.vertices)][corner][inside]
    if v_hair[list(t.vertices)].sum() >= 2:
        hair_tri_map[y0:y1, x0:x1][inside] = True
    n3 = np.cross(v[1] - v[0], v[2] - v[0]); n3 /= max(1e-12, np.linalg.norm(n3))
    nrm[y0:y1, x0:x1][inside] = n3
covered_map = ~np.isnan(pos[..., 0])
for _ in range(3):
    empty = np.isnan(pos[..., 0])
    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        shifted = np.roll(pos, (dy, dx), axis=(0, 1))
        sn = np.roll(nrm, (dy, dx), axis=(0, 1))
        take = empty & ~np.isnan(shifted[..., 0])
        pos[take] = shifted[take]; nrm[take] = sn[take]
        lab_map[take] = np.roll(lab_map, (dy, dx), axis=(0, 1))[take]
        empty = np.isnan(pos[..., 0])
iy = (np.arange(TH) * RES // TH)
ix = (np.arange(TW) * RES // TW)
P = pos[iy][:, ix].reshape(-1, 3)
L = lab_map[iy][:, ix].reshape(-1)
HAIR_MESH = hair_tri_map[iy][:, ix].reshape(-1)
N = nrm[iy][:, ix].reshape(-1, 3)
inner = covered_map[iy][:, ix].reshape(-1)          # on a triangle (not the gutter round an island)
del pos, nrm, lab_map
covered = ~np.isnan(P[:, 0])
P = np.nan_to_num(P, nan=-9).astype(np.float32)
Px, Py, Pz = P[:, 0], P[:, 1], P[:, 2]
part = np.array(names + ['?'])[np.clip(L, 0, len(names))]
part = np.where(L < 0, '?', part)
TORSO = np.isin(part, ['Hips', 'Spine', 'Chest', 'BustL', 'BustR', 'ShoulderL', 'ShoulderR'])
THIGH = np.isin(part, ['ThighL', 'ThighR'])
ARM = np.isin(part, ['UpperArmL', 'UpperArmR'])
front_y = J('Chest')[1]
front = Py < front_y


def seg_distance(p, a, b):
    a = np.asarray(a, dtype=p.dtype); b = np.asarray(b, dtype=p.dtype)
    ab = b - a
    t = np.clip(((p - a) @ ab) / max(1e-9, float(ab @ ab)), 0, 1)
    return np.linalg.norm(p - (a + t[:, None] * ab), axis=1), t


# ------------------------------------------------------------------ landmarks

skin_ref_s = np.median(rgb[covered & np.isin(part, ['ForearmL', 'ForearmR'])], axis=0)
skin_ref = to_linear(skin_ref_s)
skin_d = np.linalg.norm(rgb - skin_ref_s[None], axis=1)
# The skin of the torso itself (often a shade off the arms').
torso_skin = covered & TORSO & (skin_d < 0.12)
skin_t_s = np.median(rgb[torso_skin], axis=0) if torso_skin.sum() > 1000 else skin_ref_s
skin_d = np.minimum(skin_d, np.linalg.norm(rgb - skin_t_s[None], axis=1))
thigh_z = (J('ThighL')[2] + J('ThighR')[2]) * 0.5
# (From the hip joints: measured on the skin, the inner thighs pressed together read as a crotch 7 cm too low.)
crotch_z = thigh_z - 0.075 * H / 1.7
# The bust: the most forward point of the chest on each side (the bust bones sit inside it, near the middle).
bust = []
for s in (-1, 1):
    zone_b = covered & TORSO & front & (Px * s > 0.03) & (Px * s < 0.16) & (Pz > chest_z - 0.06) & (Pz < neck_z - 0.08)
    if zone_b.sum() > 100:
        k = np.argsort(Py[zone_b])[:max(20, int(zone_b.sum() * 0.01))]
        bust.append(np.median(P[zone_b][k], axis=0))
bust_z = float(np.mean([b[2] for b in bust])) if bust else chest_z + 0.05
bust_x = float(np.mean([abs(b[0]) for b in bust])) if bust else 0.09
waist_z = hips_z + 0.09
shoulder_x = abs(J('UpperArmL')[0]) if 'UpperArmL' in joint else 0.17
navel_z = hips_z + 0.06
log(f'crotch {crotch_z:.3f}, thigh joints {thigh_z:.3f}, bust {bust_z:.3f} x {bust_x:.3f}, shoulder x {shoulder_x:.3f}, front y {front_y:.3f}')

# Distance from the torso's middle line (front to back), for things wrapping round (bands, waistbands).
body_mid_y = (J('Chest')[1] + J('Spine')[1]) * 0.5
# Hip width at each height, for the leg line rising to the side.
hip_half = float(np.percentile(np.abs(Px[covered & (TORSO | THIGH) & (np.abs(Pz - (crotch_z + 0.08)) < 0.015)]), 98))
chest_half = max(float(np.percentile(np.abs(Px[covered & TORSO & (Pz > bust_z - 0.06) & (Pz < bust_z)]), 96)), 0.125 * H / 1.7)
shoulder_x = chest_half
log(f'chest half width {chest_half:.3f}')
log(f'hip half width {hip_half:.3f}')

# Hair stays (long hair hangs over the back and the chest): coloured like the top of the head.
head_tex = covered & (Pz > J('Head')[2] + 0.4 * (H - J('Head')[2]))
hair_ref = np.median(rgb[head_tex], axis=0)
# (Only where the hair's colour is clearly not the skin's: pale blonde hair is close to tanned skin, and painting
# round 'hair' left the suits in pieces.)
# Whole UV islands of hair: an island (a connected piece of the texture) mostly on hair triangles is all hair (the
# mesh test misses strands coloured like skin or like the bikini, and those were taken for the bikini and painted).
IR = 1024
cov_i = covered_map[::2, ::2]
hair_i = hair_tri_map[::2, ::2]
lab = np.where(cov_i, np.arange(IR * IR).reshape(IR, IR), IR * IR).astype(np.int64)
for _ in range(1500):
    m = lab.copy()
    for ax in (0, 1):
        for d in (1, -1):
            m = np.minimum(m, np.roll(lab, d, axis=ax))
    m = np.where(cov_i, m, IR * IR)
    if (m == lab).all():
        break
    lab = m
ids, inv = np.unique(lab[cov_i], return_inverse=True)
frac = np.bincount(inv, weights=hair_i[cov_i]) / np.maximum(np.bincount(inv), 1)
island_hair = np.zeros((IR, IR), dtype=bool)
island_hair[cov_i] = frac[inv] > 0.4
log(f'UV islands {len(ids)}, hair islands {int((frac > 0.4).sum())}')
HAIR_ISLAND = island_hair[(np.arange(TH) * IR // TH)][:, (np.arange(TW) * IR // TW)].reshape(-1)
hair = (HAIR_MESH | HAIR_ISLAND) & ~np.isin(part, ["HandL", "HandR", "ForearmL", "ForearmR"])
keep = covered & ~hair
log(f'skin {np.round(skin_t_s * 255)}, hair {np.round(hair_ref * 255)}, hair texels {int(hair.sum())}')

# ------------------------------------------------------------------ the old bikini, taken off

# Its texels: on the torso and the tops of the thighs, between the knees and the collarbones, not skin, not hair.
# (the neck too: halter strings tie behind it)
zone = covered & (TORSO | THIGH | (part == "Neck")) & (Pz > crotch_z - 0.045) & (Pz < J("Head")[2]) & ~hair
old = zone & (skin_d > 0.13)
# The bikini's own colour (the commonest clearly-not-skin colour there). Then every texel whose colour lies on the way
# from the skin to it is the bikini, or its colour showing through (Tripo painted soft see-through halos round the
# pieces and under the cups): skin shaded only darker or lighter lies off that line, and stays.
far = old & (skin_d > 0.3)
bleed = np.zeros(len(P), dtype=bool)
if far.sum() > 1000:
    bik_ref = np.median(rgb[far], axis=0)
    d_bik = np.linalg.norm(rgb - bik_ref[None], axis=1)
    old &= (d_bik < skin_d * 1.5) | (skin_d > 0.25)
    axis_c = bik_ref - skin_t_s
    alpha = ((rgb - skin_t_s[None]) @ axis_c) / max(float(axis_c @ axis_c), 1e-4)
    resid = np.linalg.norm(rgb - (skin_t_s[None] + alpha[:, None] * axis_c[None]), axis=1)
    bleed = zone & (alpha > 0.16) & (resid < 0.1)
    log(f'old bikini colour {np.round(bik_ref * 255)}, its colour showing through: {int((bleed & ~old).sum())} texels more')
hsv_sat = (rgb.max(1) - rgb.min(1)) / np.maximum(rgb.max(1), 1e-4)
skin_sat = float((skin_t_s.max() - skin_t_s.min()) / max(skin_t_s.max(), 1e-4))
skin_Y = float(to_linear(skin_t_s) @ np.array([0.2126, 0.7152, 0.0722]))
crease = old & (np.abs(hsv_sat - skin_sat) < 0.07) & (Y < skin_Y) & (Y > skin_Y * 0.45)
old &= ~crease
old |= bleed
old_img = old.reshape(TH, TW)
# Grown 14 texels (the soft edge where its colour bled into the skin).
grown = old_img.copy()
for _ in range(14):
    g = grown.copy()
    g[1:] |= grown[:-1]; g[:-1] |= grown[1:]; g[:, 1:] |= grown[:, :-1]; g[:, :-1] |= grown[:, 1:]
    grown = g
grown &= zone.reshape(TH, TW) | ~covered.reshape(TH, TW)
log(f'old bikini texels {int(old.sum())} (+ {int(grown.sum() - old.sum())} round it)')


def fill_smooth(channels, unknown):
    """Fills unknown cells smoothly from the known ones round them (Laplace), coarse grids first."""
    h, w = unknown.shape
    if h * w > 4096 and h > 16 and w > 16:
        ch, cw = (h + 1) // 2, (w + 1) // 2
        cu = np.ones((ch, cw), dtype=bool)
        coarse = []
        known = ~unknown
        count = np.zeros((ch, cw), dtype=np.float32)
        for dy in (0, 1):
            for dx in (0, 1):
                k = known[dy::2, dx::2]
                count[:k.shape[0], :k.shape[1]] += k
        for c in channels:
            s = np.zeros((ch, cw), dtype=np.float32)
            for dy in (0, 1):
                for dx in (0, 1):
                    v = np.where(known, c, 0)[dy::2, dx::2]
                    s[:v.shape[0], :v.shape[1]] += v
            coarse.append(np.where(count > 0, s / np.maximum(count, 1), 0))
        cu = count == 0
        fill_smooth(coarse, cu)
        for c, cc in zip(channels, coarse):
            up = np.repeat(np.repeat(cc, 2, 0), 2, 1)[:h, :w]
            c[unknown] = up[unknown]
    for _ in range(60):   # (Jacobi: all at once, so no over-relaxing)
        for c in channels:
            avg = (np.roll(c, 1, 0) + np.roll(c, -1, 0) + np.roll(c, 1, 1) + np.roll(c, -1, 1)) * 0.25
            c[unknown] = avg[unknown]


# The skin under it, filled in on the body itself (not in the texture, where UV islands and their gutters lie side by
# side): the known skin's colour averaged into 1 cm cells round the body, carried out into the empty cells next to
# them until every cell is reached, smoothed, and read back at every texel that was the bikini.
gflat = grown.reshape(-1)
known = covered & inner & ~gflat & ~hair & (skin_d < 0.15) & ~bleed
lo = P[covered].min(0) - 0.03
CELL = 0.01
dims = np.ceil((P[covered].max(0) + 0.03 - lo) / CELL).astype(int) + 1
def cell_of(idx):
    return np.clip(((P[idx] - lo) / CELL).astype(int), 0, dims - 1)
ck = cell_of(known)
flat_k = np.ravel_multi_index(ck.T, dims)
acc = np.zeros((np.prod(dims), 3), dtype=np.float64)
cnt = np.zeros(np.prod(dims), dtype=np.float64)
np.add.at(acc, flat_k, lin[known])
np.add.at(cnt, flat_k, 1)
grid = (acc / np.maximum(cnt, 1)[:, None]).reshape(*dims, 3)
have = (cnt > 3).reshape(dims)
for _ in range(40):
    if have.all():
        break
    s = np.zeros_like(grid); n = np.zeros(dims)
    for ax in range(3):
        for d in (1, -1):
            s += np.roll(grid * have[..., None], d, axis=ax)
            n += np.roll(have, d, axis=ax)
    new = ~have & (n > 0)
    grid[new] = s[new] / n[new][:, None]
    have |= new
for _ in range(3):                        # a little smoothing (cells next to each other are 1 cm apart)
    s = np.zeros_like(grid)
    for ax in range(3):
        for d in (1, -1):
            s += np.roll(grid, d, axis=ax)
    grid = grid * 0.4 + s / 6 * 0.6
fill_idx = np.where(gflat & covered)[0]
cf = (P[fill_idx] - lo) / CELL - 0.5
c0 = np.clip(np.floor(cf).astype(int), 0, dims - 2)
t = np.clip(cf - c0, 0, 1)
val = np.zeros((len(fill_idx), 3))
for dx in (0, 1):
    for dy in (0, 1):
        for dz in (0, 1):
            w = (t[:, 0] if dx else 1 - t[:, 0]) * (t[:, 1] if dy else 1 - t[:, 1]) * (t[:, 2] if dz else 1 - t[:, 2])
            val += grid[c0[:, 0] + dx, c0[:, 1] + dy, c0[:, 2] + dz] * w[:, None]
base = lin.copy()
# Blended in over the grown rim, so the filled skin meets the painted skin without a seam.
core = old.copy()
blend = np.where(core[fill_idx], 1.0, 0.85)[:, None]
base[fill_idx] = base[fill_idx] * (1 - blend) + val * blend
lin2 = base.astype(np.float32)
Y2 = lin2 @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
del acc, cnt, grid

# ------------------------------------------------------------------ painting helpers

out = lin2.copy()
ONE = np.ones(len(P), dtype=np.float32)
# Where cloth may go: skin, or where the old bikini was (not hair, eyes, nails, flip-flops...).
# (and only on what was skin or the old bikini: hair the mesh test missed is never painted)
paintable = (keep & (TORSO | THIGH | ARM | (part == "Neck")) & ((skin_d < 0.24) | grown.reshape(-1))).astype(np.float32)
# (the shoulder helpers skin the top of the arm: off the body's side there is arm, not torso)
SHOULDER_ARM = np.isin(part, ["ShoulderL", "ShoulderR"]) & (np.abs(Px) > chest_half - 0.015)
ref_Y = float(np.median(Y2[covered & TORSO])) if (covered & TORSO).any() else 0.3


def blur(img, r):
    """A box blur (r texels) of a texture-sized image, the gutters round UV islands left out."""
    w = inner.reshape(TH, TW).astype(np.float32)

    def box(a):
        for axis in (0, 1):
            c = np.cumsum(a, axis=axis, dtype=np.float64)
            c = np.concatenate([np.zeros_like(c.take([0], axis=axis)), c], axis=axis)
            n = a.shape[axis]
            hi = np.clip(np.arange(n) + r + 1, 0, n)
            lo = np.clip(np.arange(n) - r, 0, n)
            a = (c.take(hi, axis=axis) - c.take(lo, axis=axis)).astype(np.float32)
        return a
    return box(img * w) / np.maximum(box(w), 1e-4)


# The light and dark the cloth keeps: the body's broad shading only (blurred), not painted detail (old seams, the navel).
Y_soft = blur(Y2.reshape(TH, TW), max(2, TW // 400)).reshape(-1)
# Distances measured across the body (x, z) are shorter than along its surface where the surface turns away: scaled up
# there, so edges and trims are as wide on the sides and under the bust as in front.
slant_x = (1.0 / np.maximum(0.3, np.sqrt(np.clip(1 - N[:, 0] ** 2, 0, 1)))).astype(np.float32)
slant_z = (1.0 / np.maximum(0.3, np.sqrt(np.clip(1 - N[:, 2] ** 2, 0, 1)))).astype(np.float32)


def edge(value, width=0.0035):
    """0..1 soft step for value > 0 (metres): clean, anti-aliased cloth edges."""
    return smooth(0.5 + value / width)


def band(value, width):
    return np.clip(1 - np.abs(value) / width, 0, 1)


def fabric(colour, sd=None, shade=0.2):
    """The cloth's colour (linear) at every texel: the body's own light and dark kept a little, darker toward its
    edges (it curves down onto the skin there), lighter on top-facing parts."""
    c = to_linear(srgb(colour))[None, :]
    k = np.clip((Y_soft / max(ref_Y, 1e-4)) ** shade, 0.8, 1.12)
    k = k * (1 + 0.06 * np.clip(N[:, 2], -1, 1))
    if sd is not None:
        k = k * (1 - 0.16 * smooth(1 - sd / 0.005))
    return c * k[:, None]


def paint(mask, colour_lin):
    global out
    m = np.clip(mask, 0, 1)[:, None]
    out = out * (1 - m) + colour_lin * m


def shadow(sd, strength=0.3, width=0.0028):
    """The thin shadow a fabric edge casts on the skin just outside it."""
    global out
    s = smooth(1 + sd / width) * (sd < 0) * paintable * strength
    out = out * (1 - s)[:, None]


def dots(u, v, spacing, radius):
    """Polka dots on a (u, v) grid in metres (every other row offset)."""
    row = np.floor(v / spacing + 0.5)
    uu = u + (row % 2) * spacing * 0.5
    du = (uu / spacing - np.round(uu / spacing)) * spacing
    dv = v - row * spacing
    return smooth((radius - np.sqrt(du * du + dv * dv)) / 0.0011)


def piece(sd, colour, trim=None, pattern=None, shade=0.2, trim_width=0.0045, where=None):
    """Paints one piece of the suit: sd > 0 inside (metres from its edge), its trim and the shadow it casts."""
    inside = paintable if where is None else paintable * where
    sd = np.where(inside > 0, sd, -9).astype(np.float32)
    m = edge(sd) * inside
    c = fabric(colour, sd, shade)
    if pattern is not None:
        pm, pc = pattern
        c = c * (1 - pm[:, None]) + fabric(pc, sd, shade) * pm[:, None]
    shadow(sd)
    paint(m, c)
    if trim is not None:
        t = smooth((trim_width - sd) / 0.0012) * (sd > 0) * inside
        paint(t, fabric(trim, None, 0.12) * (1 + 0.1 * smooth(1 - np.abs(sd - trim_width * 0.5) / (trim_width * 0.5)))[:, None])
    return m


def ruffle(edge_sd, piece_sd, colour, depth=0.014, fold=0.008, where=None):
    """A frill along one edge of a piece (edge_sd: metres in from that edge; piece_sd: the whole piece's): a band
    that deep with folds light and dark across it, a scalloped inner edge, and the shadow it casts on the piece."""
    u = around
    scallop = 0.003 * np.abs(np.sin(u / fold * math.pi))
    inner = depth - scallop - edge_sd                      # > 0 on the frill
    region = paintable * (TORSO_F if where is None else where)
    m = edge(inner, 0.0022) * edge(piece_sd) * region
    shade = 0.8 + 0.32 * (0.5 + 0.5 * np.cos(u / fold * math.pi * 2))
    s = smooth(1 + inner / 0.004) * (inner < 0) * edge(piece_sd) * region * 0.3
    global out
    out = out * (1 - s)[:, None]
    paint(m, fabric(colour, None, 0.15) * shade[:, None])
    return m


# Round-the-body coordinate (metres along the surface, roughly): 0 front middle, larger round to the back.
ang = np.arctan2(Px, -(Py - body_mid_y))
around = ang * 0.14
backness = smooth((np.abs(ang) - 1.1) / 0.9)          # 0 at the front .. 1 at the back, smoothly round the sides


def leg_line(cut_front, cut_back, low=0.0):
    """> 0 above a swimsuit's leg opening: from the crotch it rises toward the side of the hip by cut (m), less at
    the back (cut_back), changing smoothly round the side."""
    side = smooth((np.abs(Px) - 0.025) / max(0.03, hip_half * 0.8 - 0.025))
    cut = cut_front + (cut_back - cut_front) * backness
    return (Pz - (crotch_z - 0.004 - low + cut * side)) * slant_z


def top_edge(z):
    return (z - Pz) * slant_z


def bottom_edge(z):
    return (Pz - z) * slant_z


def body_side():
    """> 0 on the torso inside the armpits (a suit stops short of the arms)."""
    # (only up by the armpits: lower down the side of the bust bulges out past the chest's width, and a cut there
    # left a hole in the cloth)
    return np.where(Pz > bust_z + 0.035, ((chest_half - 0.012) - np.abs(Px)) * slant_x, 1.0)


def cups(up=0.065, wide=0.0):
    """Two cups round the bust, joined in the middle: > 0 inside (metres, roughly)."""
    sd = np.full(len(P), -9.0, dtype=np.float32)
    rx = max(0.072, bust_x * 1.25) + wide
    for b in bust:
        dx = (Px - b[0]) / rx
        dz = np.where(Pz > b[2], (Pz - b[2]) / up, (Pz - b[2]) / 0.07)
        sd = np.maximum(sd, (1 - np.sqrt(dx * dx + dz * dz)) * 0.045)
    return np.where(front, sd, -9)


def strap_path(points, width):
    """> 0 within width/2 of a polyline of (x, z, side) points, side +1 front, -1 back: the strap lies on that side
    (a segment from front to back goes over the shoulder)."""
    sd = np.full(len(P), -9.0, dtype=np.float32)
    q = np.stack([Px, Pz], 1)
    for (ax_, az, sa), (bx_, bz, sb) in zip(points[:-1], points[1:]):
        a = np.array([ax_, az], dtype=np.float32); b = np.array([bx_, bz], dtype=np.float32); ab = b - a
        t = np.clip(((q - a) @ ab) / max(1e-9, float(ab @ ab)), 0, 1)
        d = np.linalg.norm(q - (a + t[:, None] * ab), axis=1)
        if sa == sb:
            on = front if sa > 0 else ~front
        else:
            on = Pz > neck_z - 0.06          # over the shoulder: both sides, up top
        sd = np.maximum(sd, np.where(on, width * 0.5 - d, -9))
    return sd


def shoulder_straps(x_front, z_front, x_top, x_back, z_back, width):
    sd = np.full(len(P), -9.0, dtype=np.float32)
    for s in (-1, 1):
        sd = np.maximum(sd, strap_path([(s * x_front, z_front, 1), (s * x_top, neck_z - 0.02, 1),
                                        (s * x_top, neck_z - 0.02, -1), (s * x_back, z_back, -1)], width))
    return sd


def halter(x_front, z_front, width):
    sd = np.full(len(P), -9.0, dtype=np.float32)
    for s in (-1, 1):
        sd = np.maximum(sd, strap_path([(s * x_front, z_front, 1), (s * 0.035, neck_z + 0.02, 1),
                                        (s * 0.03, neck_z + 0.02, -1), (s * 0.0, neck_z - 0.0, -1)], width))
    return sd


TORSO_F = (TORSO & ~SHOULDER_ARM).astype(np.float32)
LOWER = (TORSO | THIGH).astype(np.float32)
UPPER = ((TORSO & ~SHOULDER_ARM) | (part == "Neck")).astype(np.float32)
SLEEVED = (TORSO | ARM | (part == "Neck")).astype(np.float32)
under_z = bust_z - 0.075            # under the bust
navel_hi = navel_z + 0.02           # a high waist
low_waist = hips_z + 0.02

# ------------------------------------------------------------------ the styles

if STYLE == 'probe':
    pass
elif STYLE == 'lola':
    # Lola: a cherry-red one-piece, sweetheart neckline, white piping, straps over the shoulders, a scoop back.
    red, white = (198, 24, 42), (246, 244, 238)
    front_top = np.maximum(cups(0.06, 0.006), top_edge(bust_z - 0.035))
    back_top = top_edge(chest_z - 0.035)
    sd = np.minimum(np.minimum(leg_line(0.12, 0.05), np.where(front, front_top, back_top)), body_side())
    piece(sd, red, trim=white, where=LOWER)
    piece(shoulder_straps(bust_x + 0.01, bust_z + 0.04, chest_half - 0.05, chest_half - 0.055, chest_z - 0.07, 0.012), red, where=UPPER)
elif STYLE == 'sporty':
    # Red: a sporty two-piece, a racerback crop top with a white band under the bust and high-waisted bottoms with a
    # white stripe down each side.
    red, white = (206, 28, 40), (248, 248, 246)
    neck_scoop = top_edge(neck_z - 0.06 - 0.05 * np.clip(1 - (Px / 0.09) ** 2, 0, 1))
    front_top = np.minimum(np.minimum(neck_scoop, bottom_edge(under_z - 0.012)), body_side())
    racer = np.minimum((0.04 + 0.35 * np.clip(Pz - under_z, 0, 1)) - np.abs(Px), top_edge(neck_z - 0.04))
    back_top = np.minimum(bottom_edge(under_z - 0.012), np.maximum(racer, top_edge(under_z + 0.05)))
    top_sd = np.minimum(np.where(front, front_top, back_top), body_side())
    piece(top_sd, red, trim=white, where=UPPER)
    paint(band(Pz - under_z, 0.007) * edge(top_sd) * paintable * TORSO_F, fabric(white))
    sd = np.minimum(leg_line(0.10, 0.04), top_edge(navel_hi))
    piece(sd, red, trim=white, where=LOWER)
    paint(band(np.abs(Px) - (hip_half - 0.02), 0.006) * edge(sd) * paintable * LOWER, fabric(white))
elif STYLE == 'ruffle':
    # Turquoise: a ruffled bandeau and high-waisted bottoms with a frill along the waistband and white side bows.
    turq, white = (28, 178, 190), (250, 250, 248)
    top_sd = np.minimum(np.minimum(bottom_edge(under_z - 0.005), top_edge(bust_z + 0.04 + np.where(front, 0.0, -0.035))), body_side())
    piece(top_sd, turq, where=TORSO_F)
    ruffle(top_edge(bust_z + 0.04 + np.where(front, 0.0, -0.035)), top_sd, turq, depth=0.018)
    sd = np.minimum(leg_line(0.09, 0.04), top_edge(navel_hi))
    piece(sd, turq, where=LOWER)
    ruffle(top_edge(navel_hi), sd, turq, depth=0.012, where=LOWER)
    for s in (-1, 1):
        q = np.stack([(Px - s * (hip_half - 0.012)) * 0.75, (Pz - (navel_hi - 0.035)) * 1.6], 1)
        bow = smooth((0.016 - np.linalg.norm(q, axis=1)) / 0.002) * paintable * LOWER
        paint(bow, fabric(white))
elif STYLE == 'belted':
    # Emerald: a halter one-piece with a white belt and a gold buckle.
    green, white, gold = (22, 132, 74), (244, 244, 236), (218, 172, 64)
    front_top = np.maximum(cups(0.065, 0.008), top_edge(bust_z - 0.03))
    sd = np.minimum(np.minimum(leg_line(0.11, 0.045), np.where(front, front_top, top_edge(chest_z - 0.09))), body_side())
    piece(sd, green, trim=white, where=LOWER)
    piece(halter(bust_x + 0.005, bust_z + 0.04, 0.015), green, where=UPPER)
    belt_z = hips_z + 0.075
    belt = band(Pz - belt_z, 0.016) * edge(sd) * paintable * LOWER
    paint(smooth(belt * 3), fabric(white))
    q = np.abs(np.stack([Px, (Pz - belt_z) * 1.15], 1))
    box = np.maximum(q[:, 0], q[:, 1])
    buckle = front * smooth((0.02 - box) / 0.0015) * (1 - smooth((0.013 - box) / 0.0015)) * paintable
    paint(buckle, fabric(gold, None, 0.4))
elif STYLE == 'rash':
    # Magenta: a zip-front short-sleeved rash-guard crop top, boy-short swim shorts with black side panels.
    mag, black = (205, 32, 132), (28, 28, 32)
    arm_t = np.zeros(len(P), dtype=np.float32)
    for b in ('UpperArmL', 'UpperArmR'):
        _, t = seg_distance(P, *joint[b])
        arm_t = np.where(part == b, t, arm_t)
    collar = top_edge(neck_z - 0.01 - 0.03 * front * np.clip(1 - (Px / 0.05) ** 2, 0, 1))
    top_sd = np.minimum(collar, bottom_edge(under_z - 0.02))
    top_sd = np.where(ARM, np.minimum((0.4 - arm_t) * 0.28, 0.02), top_sd)
    piece(top_sd, mag, trim=black, where=SLEEVED)
    paint(front * band(Px, 0.0028) * edge(top_sd) * paintable * TORSO_F, fabric(black))
    sd = np.minimum(leg_line(0.0, 0.0, low=0.06), top_edge(low_waist + 0.025))
    piece(sd, mag, trim=black, where=LOWER)
    paint(smooth((np.abs(Px) - (hip_half - 0.012)) / 0.003) * edge(sd) * paintable * LOWER, fabric(black))
elif STYLE == 'retro':
    # Pink polka dots: a retro halter top and high-waisted bottoms with a white frill round the legs.
    pink, white = (238, 104, 156), (252, 250, 250)
    pat = (dots(around, Pz, 0.032, 0.0065), white)
    front_top = np.minimum(np.maximum(cups(0.06, 0.01), top_edge(bust_z - 0.025)), bottom_edge(under_z - 0.008))
    back_top = np.minimum(bottom_edge(under_z - 0.008), top_edge(under_z + 0.035))
    top_sd = np.minimum(np.where(front, front_top, back_top), body_side())
    piece(top_sd, pink, trim=white, pattern=pat, where=TORSO_F)
    piece(halter(bust_x + 0.0, bust_z + 0.04, 0.013), pink, pattern=pat, where=UPPER)
    sd = np.minimum(leg_line(0.07, 0.03), top_edge(navel_hi + 0.01))
    piece(sd, pink, trim=white, pattern=pat, where=LOWER)
    legs_sd = leg_line(0.07, 0.03)
    ruffle(legs_sd, sd, white, depth=0.011, fold=0.006, where=LOWER)
elif STYLE == 'tankini':
    # Lavender: a tankini (a top down to the hips, a frilled hem) with wide straps, over violet high-waisted bottoms.
    lav, violet = (178, 142, 222), (112, 70, 164)
    neck_scoop = top_edge(bust_z + 0.03 - 0.02 * np.clip(1 - (Px / 0.08) ** 2, 0, 1))
    top_sd = np.minimum(np.minimum(np.where(front, neck_scoop, top_edge(chest_z - 0.07)), bottom_edge(hips_z + 0.035)), body_side())
    sd = np.minimum(leg_line(0.09, 0.04), top_edge(hips_z + 0.06))
    piece(sd, violet, where=LOWER)
    piece(top_sd, lav, trim=violet, where=LOWER)
    piece(shoulder_straps(bust_x + 0.005, bust_z + 0.02, chest_half - 0.045, chest_half - 0.05, chest_z - 0.06, 0.022), lav, where=UPPER)
    ruffle(bottom_edge(hips_z + 0.035), top_sd, lav, depth=0.02)
else:
    raise SystemExit('unknown style ' + STYLE)

# ------------------------------------------------------------------ save

new_rgb = to_srgb(out)
if STYLE != 'probe':
    img = bpy.data.images.new('swimwear', TW, TH)
    pxo = np.ones((TW * TH, 4), dtype=np.float32)
    pxo[:, :3] = new_rgb.astype(np.float32)
    img.pixels.foreach_set(pxo.ravel())
    tex_node.image = img
    scene = bpy.context.scene
    scene.render.image_settings.file_format = 'JPEG'
    scene.render.image_settings.quality = 94
    img.save_render(TEX_OUT, scene=scene)
    log('wrote', TEX_OUT)
else:
    img = bpy.data.images.new('probe', TW, TH)
    pxo = np.ones((TW * TH, 4), dtype=np.float32)
    show = to_srgb(lin2)
    show[old] = show[old] * 0.5 + np.array([0, 1, 0]) * 0.5
    pxo[:, :3] = show
    img.pixels.foreach_set(pxo.ravel())
    tex_node.image = img

if PREVIEW:
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'TEXTURE'
    scene.world = bpy.data.worlds.new('w')
    scene.world.color = (0.8, 0.85, 0.9)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam'))
    cam.data.type = 'ORTHO'
    scene.collection.objects.link(cam); scene.camera = cam
    cz = (crotch_z + neck_z) * 0.5
    for name, angle, scale in (('front', 0, 0.75), ('side', 90, 0.75), ('back', 180, 0.75), ('full', 0, H * 1.1)):
        a = math.radians(angle)
        cam.data.ortho_scale = scale
        z = cz if name != 'full' else H * 0.5
        cam.location = (6 * math.sin(a), -6 * math.cos(a), z)
        cam.rotation_euler = (math.pi / 2, 0, a)
        scene.render.resolution_x, scene.render.resolution_y = (800, 800) if name != 'full' else (500, 1000)
        scene.render.filepath = f'{PREVIEW}_{name}.png'
        bpy.ops.render.render(write_still=True)
