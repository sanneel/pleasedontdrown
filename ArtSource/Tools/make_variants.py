"""Makes look-alike variants of a rigged character (output of prepare_character.py): other skin, hair, eye and outfit
colours, a slightly different face and build. The skeleton and skin weights stay, so the game bakes them like any body.

blender -b --factory-startup -P ArtSource/Tools/make_variants.py -- <in.glb> <out_prefix> [options]
  --count 10          variants to write: <out_prefix>_v01.glb ... (numbered from 1)
  --seed 1            random seed (same seed, same variants)
  --feminine 1        women: bust size varies, no grey hair or beards
  --preview <prefix>  also render a front lineup (+ close-ups of the faces) of the original and every variant
  --masks <prefix>    debug: write the skin / hair / eye / clothes masks as pictures

How the texture is split: every texel is traced back to the spot on the body it colours (the UV triangles are drawn
into a map of 3D positions). Skin = the colours found on the hands and the middle of the face; hair = what is left on
the head (above the chest); eyes = dark, non-skin texels on the front of the face at eye height; the rest below the
neck is clothing. Recolouring keeps the shading of the original (ratios in linear light).
"""
import json
import math
import random
import sys

import bpy
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
SRC, PREFIX = argv[0], argv[1]
opts = {'count': 10, 'seed': 1, 'feminine': 1, 'preview': '', 'masks': '', 'start': 1}
i = 2
while i < len(argv):
    key = argv[i].lstrip('-')
    opts[key] = type(opts[key])(argv[i + 1]) if not isinstance(opts[key], str) else argv[i + 1]
    i += 2


def log(*a):
    print('[variants]', *a)


def srgb(c):
    return np.array(c, dtype=np.float64) / 255.0


# Palettes (sRGB 0-255): the target median colour of that region.
SKIN = [('porcelain', (246, 214, 192)), ('fair', (236, 190, 160)), ('light tan', (222, 170, 128)),
        ('olive', (198, 150, 104)), ('tan', (182, 126, 84)), ('brown', (146, 96, 62)), ('dark brown', (108, 68, 44)),
        ('deep', (78, 50, 36)), ('rosy', (240, 176, 152))]
HAIR = [('black', (24, 21, 21)), ('dark brown', (58, 38, 27)), ('brown', (98, 66, 44)), ('chestnut', (122, 66, 38)),
        ('auburn', (140, 52, 30)), ('ginger', (196, 96, 44)), ('dark blonde', (160, 124, 80)),
        ('blonde', (222, 188, 126)), ('platinum', (236, 226, 204))]
HAIR_WOMEN_EXTRA = [('pink', (222, 112, 162)), ('blue', (64, 112, 206)), ('lilac', (170, 130, 210))]
HAIR_MEN_EXTRA = [('grey', (150, 148, 146)), ('white', (222, 222, 218))]
EYES = [('brown', (92, 56, 30)), ('hazel', (132, 100, 48)), ('green', (64, 132, 70)), ('blue', (56, 120, 196)),
        ('grey', (112, 138, 156)), ('amber', (176, 112, 34)), ('dark', (40, 28, 22))]


# ------------------------------------------------------------------ colour helpers (numpy, N x 3 arrays)

def to_linear(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def to_srgb(c):
    c = np.clip(c, 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)


def to_lab(c):
    lin = to_linear(c)
    m = np.array([[0.4124, 0.3576, 0.1805], [0.2126, 0.7152, 0.0722], [0.0193, 0.1192, 0.9505]])
    xyz = lin @ m.T / np.array([0.95047, 1.0, 1.08883])
    f = np.where(xyz > 0.008856, np.cbrt(xyz), 7.787 * xyz + 16 / 116)
    return np.stack([116 * f[:, 1] - 16, 500 * (f[:, 0] - f[:, 1]), 200 * (f[:, 1] - f[:, 2])], axis=1)


def luminance(lin):
    return lin @ np.array([0.2126, 0.7152, 0.0722])


def kmeans(x, k, iters=12, seed=0):
    rng = np.random.default_rng(seed)
    centers = x[rng.choice(len(x), k, replace=False)]
    for _ in range(iters):
        label = np.argmin(((x[:, None, :] - centers[None]) ** 2).sum(-1), axis=1)
        centers = np.array([x[label == j].mean(0) if np.any(label == j) else centers[j] for j in range(k)])
    return centers, np.bincount(label, minlength=k) / len(x)


def lab_distance(lab, center, l_weight=0.4):
    d = lab - center
    return np.sqrt((d[:, 0] * l_weight) ** 2 + d[:, 1] ** 2 + d[:, 2] ** 2)


def smooth(u):
    u = np.clip(u, 0.0, 1.0)
    return u * u * (3 - 2 * u)


def rgb_to_hsv(c):
    mx, mn = c.max(1), c.min(1)
    d = mx - mn
    h = np.zeros(len(c))
    nz = d > 1e-6
    r, g, b = c[:, 0], c[:, 1], c[:, 2]
    i_r = nz & (mx == r)
    i_g = nz & (mx == g) & ~i_r
    i_b = nz & ~i_r & ~i_g
    h[i_r] = ((g - b)[i_r] / d[i_r]) % 6
    h[i_g] = (b - r)[i_g] / d[i_g] + 2
    h[i_b] = (r - g)[i_b] / d[i_b] + 4
    s = np.where(mx > 1e-6, d / np.maximum(mx, 1e-6), 0)
    return np.stack([h / 6, s, mx], axis=1)


def hsv_to_rgb(hsv):
    h, s, v = hsv[:, 0] * 6, hsv[:, 1], hsv[:, 2]
    i = np.floor(h).astype(int) % 6
    f = h - np.floor(h)
    p, q, t = v * (1 - s), v * (1 - s * f), v * (1 - s * (1 - f))
    out = np.zeros((len(h), 3))
    for k, (a, b, c) in enumerate(((v, t, p), (q, v, p), (p, v, t), (p, q, v), (t, p, v), (v, p, q))):
        m = i == k
        out[m] = np.stack([a[m], b[m], c[m]], axis=1)
    return out


# ------------------------------------------------------------------ load

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
body = next(o for o in bpy.context.scene.objects if o.type == 'MESH' and o.name.startswith('Body'))
rig = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
for o in list(bpy.context.scene.objects):
    if o not in (body, rig):
        bpy.data.objects.remove(o, do_unlink=True)
mat = body.data.materials[0]
tex_node = next(n for n in mat.node_tree.nodes if n.type == 'TEX_IMAGE' and n.outputs[0].links
                and n.outputs[0].links[0].to_socket.name == 'Base Color')
for n in list(mat.node_tree.nodes):  # the game only uses the colour map: the others would just fatten the files
    if n.type == 'TEX_IMAGE' and n != tex_node:
        mat.node_tree.nodes.remove(n)
base_img = tex_node.image
TW, TH = base_img.size
orig_px = np.empty(TW * TH * 4, dtype=np.float32)
base_img.pixels.foreach_get(orig_px)
orig_px = orig_px.reshape(-1, 4)
rgb = orig_px[:, :3].astype(np.float64)
log(f'texture {TW}x{TH}')

M = body.matrix_world
co_local = np.empty(len(body.data.vertices) * 3)
body.data.vertices.foreach_get('co', co_local)
co_local = co_local.reshape(-1, 3)
co = np.array([tuple(M @ Vector(c)) for c in co_local])
joint = {b.name: (np.array(tuple(rig.matrix_world @ b.head_local)), np.array(tuple(rig.matrix_world @ b.tail_local)))
         for b in rig.data.bones}
H = float(co[:, 2].max())
head_z, top_z = joint['Head'][0][2], H
neck_z = joint['Neck'][0][2]
chest_z = joint['Chest'][0][2]
head_len = top_z - head_z
head_center = np.array([0.0, float(co[co[:, 2] > head_z][:, 1].mean()), head_z + 0.45 * head_len])
face_front_y = float(co[(co[:, 2] > head_z + 0.3 * head_len) & (co[:, 2] < head_z + 0.6 * head_len) &
                        (np.abs(co[:, 0]) < 0.03)][:, 1].min())
log(f'height {H:.3f}, head {head_z:.3f}..{top_z:.3f}, face front y {face_front_y:.3f}')

# ------------------------------------------------------------------ texel -> body position map

RES = 1024
pos = np.full((RES, RES, 3), np.nan)
uv_layer = body.data.uv_layers.active.data
uvs = np.empty(len(uv_layer) * 2)
uv_layer.foreach_get('uv', uvs)
uvs = uvs.reshape(-1, 2)
body.data.calc_loop_triangles()
for t in body.data.loop_triangles:
    l = t.loops
    p = uvs[[l[0], l[1], l[2]]] * RES
    x0, x1 = int(max(0, p[:, 0].min())), int(min(RES, p[:, 0].max() + 1))
    y0, y1 = int(max(0, p[:, 1].min())), int(min(RES, p[:, 1].max() + 1))
    if x1 <= x0 or y1 <= y0 or (x1 - x0) * (y1 - y0) > 40000:
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
    region = pos[y0:y1, x0:x1]
    region[inside] = pts[inside]
# Grow the covered area a few texels into the gutters (so seams recolour cleanly).
for _ in range(4):
    empty = np.isnan(pos[..., 0])
    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        shifted = np.roll(pos, (dy, dx), axis=(0, 1))
        take = empty & ~np.isnan(shifted[..., 0])
        pos[take] = shifted[take]
        empty = np.isnan(pos[..., 0])
# Up to the texture's size (nearest).
iy = (np.arange(TH) * RES // TH)
ix = (np.arange(TW) * RES // TW)
P = pos[iy][:, ix].reshape(-1, 3)
covered = ~np.isnan(P[:, 0])
Px, Py, Pz = (np.nan_to_num(P[:, k], nan=-9) for k in range(3))
log(f'{covered.mean() * 100:.0f}% of the texture maps onto the body')

lab = to_lab(rgb)
lin = to_linear(rgb)


def seg_distance(p, a, b):
    ab = b - a
    t = np.clip(((p - a) @ ab) / max(1e-9, ab @ ab), 0, 1)
    return np.linalg.norm(p - (a + t[:, None] * ab), axis=1)


# ------------------------------------------------------------------ masks

# Skin: colours on the hands/forearms and the middle of the face (cheeks, forehead).
Pc = np.stack([Px, Py, Pz], axis=1)
ref = np.zeros(len(P), dtype=bool)
for sd in 'LR':
    a, _ = joint['Forearm' + sd]
    _, b = joint['Hand' + sd]
    ref |= covered & (seg_distance(Pc, a, b) < 0.06)
ref |= covered & (Pz > head_z + 0.15 * head_len) & (Pz < head_z + 0.6 * head_len) & (np.abs(Px) < 0.045) & \
    (Py < face_front_y + 0.03)
skin_samples = lab[ref]
centers, share = kmeans(skin_samples[:: max(1, len(skin_samples) // 20000)], 4, seed=1)
# The skin: the biggest cluster plus those close to it (sunburn); a stray cluster (sleeve, bracelet, lips) drops out.
order = np.argsort(-share)
main = centers[order[0]]
skin_centers = [c for c, s in zip(centers, share) if s > 0.08 and np.linalg.norm((c - main)[1:]) < 22]
d_skin = np.min([lab_distance(lab, c) for c in skin_centers], axis=0)
skin_ref_lin = np.median(lin[ref & (d_skin < 8)], axis=0)
log('skin clusters', [np.round(c, 1).tolist() for c in skin_centers], 'share', np.round(share, 2).tolist())

# Hair: head texels (above the ears) that aren't skin; then everything of that colour above the chest.
head_zone = covered & (Pz > head_z + 0.35 * head_len)
hair_samples = lab[head_zone & (d_skin > 16)]
if len(hair_samples) > 200:
    hc, hs = kmeans(hair_samples[:: max(1, len(hair_samples) // 20000)], 3, seed=2)
    hair_center = hc[np.argmax(hs)]
else:
    hair_center = None
if hair_center is not None:
    d_hair = lab_distance(lab, hair_center, 0.5)
    hair_zone = ~covered | (Pz > chest_z)
    hair = smooth((20 - d_hair) / 8) * (d_hair < d_skin) * hair_zone
    hair_ref_lin = np.median(lin[hair > 0.8], axis=0) if np.any(hair > 0.8) else lin[0]
    log('hair', np.round(hair_center, 1).tolist(), f'{(hair > 0.5).mean() * 100:.1f}% of texels')
else:
    hair = np.zeros(len(P))
    hair_ref_lin = None
    log('no hair found (bald?)')

skin = smooth((18 - d_skin) / 8) * (1 - hair)
# Sunburn and blush (pinker than the reference skin): skin-like colours on the upper body and face count as skin, or
# they would be turned like clothes. Bikini tops are far more saturated (red) or not warm at all (blue, purple).
warm = (lab[:, 1] > 8) & (lab[:, 1] < 58) & (lab[:, 2] > 4) & (lab[:, 2] < 48) & (lab[:, 0] > 40) & (lab[:, 1] < 2.6 * lab[:, 2] + 12)
burn = covered & (Pz > joint['Spine'][0][2]) & warm & (lab_distance(lab, main, 0.2) < 38)
skin = np.maximum(skin, burn * (1 - hair))
d_skin = np.where(burn, np.minimum(d_skin, 12), d_skin)
log(f'{int(burn.sum())} sunburn/blush texels joined the skin')

# Eyes: front of the face at eye height, not skin, not hair, not the white, not the lips.
eye_zone = covered & (Pz > head_z + 0.3 * head_len) & (Pz < head_z + 0.62 * head_len) & (np.abs(Px) < 0.065) & \
    (Py < face_front_y + 0.035)
Y = luminance(lin)
lipish = (lab[:, 1] > 18) & (lab[:, 1] > lab[:, 2] * 0.9)
eyes = eye_zone & (d_skin > 20) & (hair < 0.5) & (Y < 0.45) & ~lipish
eyes = eyes.astype(np.float64)
log(f'eye texels {int(eyes.sum())}')

# Clothes: whatever is left below the neck.
clothes = (1 - skin) * (1 - hair) * (1 - eyes) * ((~covered) | (Pz < neck_z))
clothes *= (d_skin > 10)

if opts['masks']:
    for name, m in (('skin', skin), ('hair', hair), ('eyes', eyes), ('clothes', clothes), ('ref', ref.astype(float))):
        img = bpy.data.images.new('mask_' + name, TW, TH)
        px = np.ones((TW * TH, 4), dtype=np.float32)
        px[:, :3] = (0.25 * rgb + 0.75 * m[:, None]).astype(np.float32)
        img.pixels.foreach_set(px.ravel())
        img.filepath_raw = f"{opts['masks']}_{name}.png"
        img.file_format = 'PNG'
        img.save()

# ------------------------------------------------------------------ shape

base_co = co_local.copy()
groups = {g.index: g.name for g in body.vertex_groups}
vw = np.zeros((len(co), len(groups)))
for v in body.data.vertices:
    for g in v.groups:
        vw[v.index, g.group] = g.weight
gi = {name: idx for idx, name in groups.items()}


def reshape(p):
    """New rest positions (world = local here, the importer leaves the objects unrotated)."""
    out = co.copy()
    # Build: every vertex pushed out from (or pulled towards) the bones it follows.
    radial = np.zeros_like(co)
    weights = {name: vw[:, idx] for name, idx in gi.items()}
    for sd in 'LR':  # shoulder helpers (prepare_character.py) share the skin between the arm and the chest
        helper = weights.pop('Shoulder' + sd, None)
        if helper is not None:
            weights['UpperArm' + sd] = weights['UpperArm' + sd] + 0.5 * helper
            weights['Chest'] = weights['Chest'] + 0.5 * helper
    for name, w in weights.items():
        if not w.any() or name not in joint:
            continue
        f = p['limbs'] if name.startswith(('UpperArm', 'Forearm', 'Thigh', 'Shin')) else \
            p['torso'] if name in ('Hips', 'Spine', 'Chest') else 1.0
        a, b = joint[name]
        ab = b - a
        t = np.clip(((co - a) @ ab) / max(1e-9, ab @ ab), 0, 1)
        proj = a + t[:, None] * ab
        off = co - proj
        if name in ('Hips', 'Spine', 'Chest'):
            off[:, 2] = 0.0  # wider/deeper, not taller
        radial += w[:, None] * (off * (f - 1))
    out += radial
    # Belly: the front of the waist.
    if 'Spine' in gi:
        spine = joint['Spine'][0]
        w = vw[:, gi['Spine']] + vw[:, gi.get('Hips', gi['Spine'])] * 0.5
        front = smooth((spine[1] - co[:, 1]) / 0.05)
        band = smooth(1 - np.abs(co[:, 2] - (spine[2] + 0.04)) / 0.16)
        out[:, 1] -= (p['belly'] - 1) * 0.08 * w * front * band
    # Bust.
    for sd in 'LR':
        if 'Bust' + sd in gi:
            w = vw[:, gi['Bust' + sd]]
            c = joint['Bust' + sd][0]
            out += w[:, None] * (co - c) * (p['bust'] - 1)
    # Head: size, face width, jaw, how far the face stands out.
    wh = smooth((co[:, 2] - neck_z) / max(0.02, head_z - neck_z))
    rel = co - head_center
    new = rel * p['head']
    new[:, 0] *= p['face_w']
    lower = smooth((head_center[2] - co[:, 2]) / (0.35 * head_len))
    new[:, 0] *= 1 + (p['jaw'] - 1) * lower
    frontness = smooth((head_center[1] - co[:, 1]) / 0.06)
    new[:, 1] = rel[:, 1] + (new[:, 1] - rel[:, 1]) + rel[:, 1] * (p['face_d'] - 1) * frontness
    # Chin/nose: the lower front of the face a touch longer or shorter.
    new[:, 2] += (p['chin'] - 1) * 0.05 * lower * frontness * -1
    out = out * (1 - wh[:, None]) + (head_center + new + (out - co)) * wh[:, None]
    return out


def world_to_local(pts):
    inv = M.inverted()
    return np.array([tuple(inv @ Vector(q)) for q in pts])


# ------------------------------------------------------------------ recolour

def recolour(p):
    out = lin.copy()
    # Skin: per-channel ratio to the new tone (keeps freckles, sunburn, shading).
    k = to_linear(srgb(p['skin'])) / np.maximum(skin_ref_lin, 1e-4)
    out = out * (1 - skin[:, None]) + (lin * k) * skin[:, None]
    # Hair: the new colour times the original's brightness pattern (softened, so black -> blonde keeps its strands).
    if hair_ref_lin is not None:
        yr = max(1e-4, float(luminance(hair_ref_lin[None])[0]))
        detail = np.clip(Y / yr, 0, 6) ** 0.55
        new_hair = to_linear(srgb(p['hair']))[None] * detail[:, None]
        out = out * (1 - hair[:, None]) + new_hair * hair[:, None]
    # Eyes: the iris takes the new colour, pupils stay dark.
    iris = to_linear(srgb(p['eyes']))[None] * np.clip(Y / 0.12, 0.15, 1.6)[:, None]
    out = out * (1 - eyes[:, None]) + iris * eyes[:, None]
    result = to_srgb(out)
    # Clothes: hue turned (only coloured parts; white/black stay), a bit more or less saturated.
    if p['cloth_hue'] != 0:
        hsv = rgb_to_hsv(result)
        colourful = smooth((hsv[:, 1] - 0.15) / 0.2) * smooth((hsv[:, 2] - 0.08) / 0.1)
        w = clothes * colourful
        turned = hsv.copy()
        turned[:, 0] = (turned[:, 0] + p['cloth_hue'] / 360) % 1
        turned[:, 1] = np.clip(turned[:, 1] * p['cloth_sat'], 0, 1)
        result = result * (1 - w[:, None]) + hsv_to_rgb(turned) * w[:, None]
    return result


# ------------------------------------------------------------------ variants

rng = random.Random(opts['seed'])
fem = bool(opts['feminine'])
hair_pool = HAIR + (HAIR_WOMEN_EXTRA if fem else HAIR_MEN_EXTRA)
skin_order = list(range(len(SKIN)))
rng.shuffle(skin_order)
variants = []
for n in range(opts['count']):
    sk = SKIN[skin_order[n % len(SKIN)]]
    hr = rng.choice(hair_pool if rng.random() < 0.25 else HAIR)
    ey = rng.choice(EYES)
    p = {
        'skin_name': sk[0], 'skin': sk[1], 'hair_name': hr[0], 'hair': hr[1], 'eyes_name': ey[0], 'eyes': ey[1],
        'cloth_hue': 0 if n % 4 == 0 else rng.choice([40, 80, 120, 160, 200, 240, 280, 320]) + rng.uniform(-12, 12),
        'cloth_sat': rng.uniform(0.85, 1.15),
        'head': rng.uniform(0.95, 1.05), 'face_w': rng.uniform(0.93, 1.07), 'jaw': rng.uniform(0.9, 1.1),
        'face_d': rng.uniform(0.94, 1.07), 'chin': rng.uniform(0.85, 1.15),
        'torso': rng.uniform(0.93, 1.1), 'limbs': rng.uniform(0.92, 1.1),
        'belly': rng.uniform(0.9, 1.25) if not fem else rng.uniform(0.9, 1.08),
        'bust': rng.uniform(0.9, 1.18) if fem else 1.0,
        'height': rng.uniform(0.955, 1.045),
    }
    variants.append(p)

manifest = []
rig_rest = {b.name: (b.head_local.copy(), b.tail_local.copy()) for b in rig.data.bones}
for n, p in enumerate(variants):
    num = n + opts['start']
    out_path = f'{PREFIX}_v{num:02d}.glb'
    # Texture.
    new_rgb = recolour(p)
    img = bpy.data.images.new(f'var_{num}', TW, TH)
    px = np.ones((TW * TH, 4), dtype=np.float32)
    px[:, :3] = new_rgb.astype(np.float32)
    img.pixels.foreach_set(px.ravel())
    tex_node.image = img
    # Shape (rest pose; the bones stay where they are, only the height scales everything).
    shaped = reshape(p)
    body.data.vertices.foreach_set('co', world_to_local(shaped).ravel())
    body.data.update()
    rig.scale = (p['height'],) * 3
    body.scale = (1.0, 1.0, 1.0)
    bpy.context.view_layer.update()

    bpy.ops.object.select_all(action='DESELECT')
    body.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.gltf(filepath=out_path, export_format='GLB', use_selection=True, export_skins=True,
                              export_animations=False, export_apply=False, export_yup=True,
                              export_image_format='JPEG', export_jpeg_quality=90)
    desc = {k: (round(v, 3) if isinstance(v, float) else v) for k, v in p.items() if not isinstance(v, tuple)}
    manifest.append({'file': out_path, **desc})
    log(f'wrote {out_path}: skin {p["skin_name"]}, hair {p["hair_name"]}, eyes {p["eyes_name"]}, '
        f'outfit hue {p["cloth_hue"]:+.0f}, height x{p["height"]:.3f}')
    if opts['preview']:
        p['_image'] = img
        p['_co'] = shaped
    else:
        bpy.data.images.remove(img)
    # Back to the original for the next one.
    body.data.vertices.foreach_set('co', co_local.ravel())
    body.data.update()
    rig.scale = (1, 1, 1)

if opts['preview']:
    json.dump(manifest, open(opts['preview'] + '_variants.json', 'w'), indent=1)

# ------------------------------------------------------------------ preview lineup

if opts['preview']:
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'FLAT'
    scene.display.shading.color_type = 'TEXTURE'
    scene.world = bpy.data.worlds.new('w')
    scene.world.color = (0.8, 0.85, 0.9)
    body.modifiers.clear()
    body.parent = None
    figures = [(body, 1.0)]
    for n, p in enumerate(variants):
        o = body.copy()
        o.data = body.data.copy()
        o.data.vertices.foreach_set('co', world_to_local(p['_co']).ravel())
        m = mat.copy()
        next(nd for nd in m.node_tree.nodes if nd.type == 'TEX_IMAGE').image = p['_image']
        o.data.materials[0] = m
        scene.collection.objects.link(o)
        figures.append((o, p['height']))
    tex_node.image = base_img
    gap = 1.05
    for k, (o, h) in enumerate(figures):
        o.location = (k * gap, 0, 0)
        o.scale = (h, h, h)
    bpy.data.objects.remove(rig, do_unlink=True)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam'))
    cam.data.type = 'ORTHO'
    scene.collection.objects.link(cam)
    scene.camera = cam
    width = len(figures) * gap
    cam.data.ortho_scale = max(width, H * 1.15)
    scene.render.resolution_x = int(220 * len(figures))
    scene.render.resolution_y = int(scene.render.resolution_x * H * 1.15 / cam.data.ortho_scale)
    cam.location = (width / 2 - gap / 2, -6, H * 0.52)
    cam.rotation_euler = (math.pi / 2, 0, 0)
    scene.render.filepath = opts['preview'] + '_lineup.png'
    bpy.ops.render.render(write_still=True)
    # Faces.
    cam.data.ortho_scale = width
    scene.render.resolution_x = 360 * len(figures)
    scene.render.resolution_y = int(scene.render.resolution_x * 0.42 / width)
    cam.location = (width / 2 - gap / 2, -6, head_z + 0.35 * head_len)
    scene.render.filepath = opts['preview'] + '_faces.png'
    bpy.ops.render.render(write_still=True)
    bpy.ops.wm.save_as_mainfile(filepath=opts['preview'] + '_lineup.blend')
