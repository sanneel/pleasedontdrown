"""Paints a work outfit onto a rigged character (a copy: skeleton, skin weights and shape stay), for the hotel staff.

blender -b --factory-startup -P ArtSource/Tools/paint_outfit.py -- <in.glb> <out.glb> <outfit> [--preview <png prefix>]
  outfit: scrubs  - the doctor: teal scrub top (V-neck, short sleeves) and trousers, a stethoscope and an ID badge
          dealer  - the casino dealer: white shirt (long sleeves, collar), black waistcoat, red bow tie, black trousers

Every texel is traced back to the spot on the body it colours (as in make_variants.py), so the edges of the clothes
(neckline, sleeves, hem, cuffs) are clean lines on the body, the same across UV seams. Hair, face, hands and feet stay.
Cloth colour keeps a little of the original shading (folds), not its pattern (bikini edges, belly button).
"""
import math
import sys

import bpy
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
SRC, OUT, OUTFIT = argv[0], argv[1], argv[2]
PREVIEW = argv[argv.index('--preview') + 1] if '--preview' in argv else ''


def log(*a):
    print('[outfit]', *a, flush=True)


def to_linear(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def to_srgb(c):
    c = np.clip(c, 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)


def smooth(u):
    u = np.clip(u, 0.0, 1.0)
    return u * u * (3 - 2 * u)


def srgb(c):
    return np.array(c, dtype=np.float64) / 255.0


# ------------------------------------------------------------------ load (as make_variants.py)

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
for n in list(mat.node_tree.nodes):
    if n.type == 'TEX_IMAGE' and n != tex_node:
        mat.node_tree.nodes.remove(n)
base_img = tex_node.image
TW, TH = base_img.size
px = np.empty(TW * TH * 4, dtype=np.float32)
base_img.pixels.foreach_get(px)
rgb = px.reshape(-1, 4)[:, :3].copy()   # float32 throughout: the textures are 4096 x 4096
del px
lin = to_linear(rgb)
Y = lin @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)

M = body.matrix_world
co_local = np.empty(len(body.data.vertices) * 3)
body.data.vertices.foreach_get('co', co_local)
co = np.array([tuple(M @ Vector(c)) for c in co_local.reshape(-1, 3)])
joint = {b.name: (np.array(tuple(rig.matrix_world @ b.head_local)), np.array(tuple(rig.matrix_world @ b.tail_local)))
         for b in rig.data.bones}
H = float(co[:, 2].max())
head_z = joint['Head'][0][2]
neck_z = joint['Neck'][0][2]
chest_z = joint['Chest'][0][2]
spine_z = joint['Spine'][0][2]
hips_z = joint['Hips'][0][2]
log(f'texture {TW}x{TH}, height {H:.3f}, neck {neck_z:.3f}, chest {chest_z:.3f}, spine {spine_z:.3f}, hips {hips_z:.3f}')

# ------------------------------------------------------------------ texel -> body position (and normal) map

# The body part of every vertex: the bone with the biggest skin weight (shoulder/bust helpers count as the torso).
groups = {g.index: g.name for g in body.vertex_groups}
TORSO_GROUPS = ('Hips', 'Spine', 'Chest', 'ShoulderL', 'ShoulderR', 'BustL', 'BustR')
names = sorted(set(groups.values()))
vlabel = np.zeros(len(co), dtype=np.int32)
for v in body.data.vertices:
    if v.groups:
        g = max(v.groups, key=lambda e: e.weight)
        vlabel[v.index] = names.index(groups[g.group])
RES = 1024
pos = np.full((RES, RES, 3), np.nan)
nrm = np.zeros((RES, RES, 3))
lab_map = np.full((RES, RES), -1, dtype=np.int32)
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
    pos[y0:y1, x0:x1][inside] = pts[inside]
    corner = np.argmax(np.stack([l1, l2, l3]), axis=0)
    lab_map[y0:y1, x0:x1][inside] = vlabel[np.array(t.vertices)][corner][inside]
    n3 = np.cross(v[1] - v[0], v[2] - v[0]); n3 /= max(1e-12, np.linalg.norm(n3))
    nrm[y0:y1, x0:x1][inside] = n3
for _ in range(4):
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
P = pos[iy][:, ix].reshape(-1, 3).astype(np.float32)
L = lab_map[iy][:, ix].reshape(-1)
N = nrm[iy][:, ix].reshape(-1, 3).astype(np.float32)
del pos, nrm
covered = ~np.isnan(P[:, 0])
P = np.nan_to_num(P, nan=-9)
Px, Py, Pz = P[:, 0], P[:, 1], P[:, 2]


def seg_distance(p, a, b):
    a = np.asarray(a, dtype=p.dtype); b = np.asarray(b, dtype=p.dtype)
    ab = b - a
    t = np.clip(((p - a) @ ab) / max(1e-9, ab @ ab), 0, 1)
    return np.linalg.norm(p - (a + t[:, None] * ab), axis=1), t


# Nearest bone for every texel: which part of the body it is on.
bones = [b for b in ('Hips', 'Spine', 'Chest', 'Neck', 'Head', 'UpperArmL', 'UpperArmR', 'ForearmL', 'ForearmR',
                     'HandL', 'HandR', 'ThighL', 'ThighR', 'ShinL', 'ShinR', 'FootL', 'FootR') if b in joint]
best = np.full(len(P), np.inf, dtype=np.float32); best_i = np.zeros(len(P), dtype=np.int8)
for k, b in enumerate(bones):
    d_, _ = seg_distance(P, *joint[b])
    closer = d_ < best; best[closer] = d_[closer]; best_i[closer] = k
del best
along = {b: seg_distance(P, *joint[b])[1].astype(np.float32) for b in ('UpperArmL', 'UpperArmR', 'ForearmL', 'ForearmR') if b in joint}
nearest = np.array(bones)[best_i]
part = np.where(L >= 0, np.array(names + ['?'])[np.clip(L, 0, len(names))], nearest)
part = np.where(np.isin(part, TORSO_GROUPS[3:]), 'Chest', part)
log('parts', {b: int((part == b).sum()) for b in bones})

# Hair stays: dark/coloured texels on the head and anything of that colour hanging below it (long hair, ponytails).
head_tex = covered & (Pz > head_z + 0.4 * (H - head_z))
hair_ref = np.median(lin[head_tex], axis=0) if head_tex.any() else None
if hair_ref is not None:
    d_hair = np.linalg.norm(to_srgb(lin) - to_srgb(hair_ref[None]), axis=1)
    hair = (d_hair < 0.12) & (Pz > chest_z - 0.05) & ~np.isin(part, ['HandL', 'HandR', 'ForearmL', 'ForearmR'])
    # Hair is only hair if it stands off the skin surface region of the torso: it sits behind or beside the neck.
    hair &= (Py > joint['Neck'][0][1] - 0.02) | (np.abs(Px) > 0.09) | (Pz > neck_z - 0.02)
else:
    hair = np.zeros(len(P), dtype=bool)
log(f'hair texels kept {int(hair.sum())}')

front_y = joint['Chest'][0][1]          # the body's front is -y (the skeleton's middle; the skin is further out)
neck_d, _ = seg_distance(P, joint['Neck'][0], joint['Head'][0])
on_neck = covered & (part == 'Neck') & (Pz > neck_z + 0.02) & (Pz < head_z)
neck_r = float(np.percentile(neck_d[on_neck], 20)) if on_neck.any() else 0.06   # the skin, not hair or jaw
collar = smooth((neck_r + 0.03 - neck_d) / 0.008) * (Pz > neck_z - 0.06)   # 1 on the neck, 0 a few cm out
log(f'neck radius {neck_r:.3f}')
skin_ref = np.median(rgb[covered & np.isin(part, ['ForearmL', 'ForearmR', 'HandL', 'HandR'])], axis=0)
not_skin = np.linalg.norm(rgb - skin_ref[None], axis=1) > 0.2
torso = np.isin(part, ['Hips', 'Spine', 'Chest', 'Neck'])
legs = np.isin(part, ['ThighL', 'ThighR', 'ShinL', 'ShinR'])
ankle_z = max(joint['ShinL'][1][2], joint['ShinR'][1][2])
upper_arm = np.isin(part, ['UpperArmL', 'UpperArmR'])
forearm = np.isin(part, ['ForearmL', 'ForearmR'])
arm_t = np.where(part == 'UpperArmL', along.get('UpperArmL', 0), np.where(part == 'UpperArmR', along.get('UpperArmR', 0), 0))
fore_t = np.where(part == 'ForearmL', along.get('ForearmL', 0), np.where(part == 'ForearmR', along.get('ForearmR', 0), 0))
front = Py < front_y


def front_curve(xz, width):
    """0..1: on the front of the body, within width (m) of a polyline in the (x, z) plane (painted on the skin)."""
    d = np.full(len(P), 9.0)
    q = np.stack([Px, Pz], 1)
    for (ax_, az), (bx_, bz) in zip(xz[:-1], xz[1:]):
        a = np.array([ax_, az]); b = np.array([bx_, bz]); ab = b - a
        t = np.clip(((q - a) @ ab) / max(1e-9, ab @ ab), 0, 1)
        d = np.minimum(d, np.linalg.norm(q - (a + t[:, None] * ab), axis=1))
    return smooth((width - d) / 0.003) * (Py < front_y) * keep


def layer(mask, colour, out, shade=0.12, seam=None):
    """mask 0..1: paint colour (sRGB) keeping a little of the original's light and dark (shade)."""
    region = mask > 0.5
    ref = np.median(Y[region]) if region.any() else 0.2
    k = np.clip((Y / max(ref, 1e-4)) ** shade, 0.8, 1.12)
    c = to_linear(srgb(colour))[None] * k[:, None]
    # A little lighter on top-facing cloth, darker underneath (reads as fabric under the game's light).
    c *= (1 + 0.08 * np.clip(N[:, 2], -1, 1))[:, None]
    if seam is not None:
        c *= (1 - 0.35 * seam)[:, None]
    return out * (1 - mask[:, None]) + c * mask[:, None]


def edge(value, width=0.006):
    """0..1 soft step for value > 0 (metres): clean, anti-aliased cloth edges."""
    return smooth(0.5 + value / width)


def band(value, width):
    """1 on a thin line where value crosses 0 (stitching at hems and cuffs)."""
    return np.clip(1 - np.abs(value) / width, 0, 1)


out = lin.copy()
keep = (~hair) & covered
if OUTFIT == 'scrubs':
    hem_z = hips_z - 0.06
    neck_depth = 0.10                                          # V-neck
    v_neck = front & (Pz > neck_z - neck_depth) & (np.abs(Px) < (Pz - (neck_z - neck_depth)) * 0.75)
    top = (torso | (upper_arm & (arm_t < 0.45))) & keep & ~v_neck
    top_m = top * edge(Pz - hem_z) * (1 - collar)
    sleeve = upper_arm & (np.abs(arm_t - 0.45) < 0.02)
    hem = band(Pz - hem_z - 0.01, 0.006)
    shorts = (legs | torso) & keep & not_skin & (Pz < hem_z + 0.06)          # the swim shorts become scrub shorts
    teal = (64, 140, 150)
    out = layer(shorts.astype(float), (52, 118, 128), out)
    out = layer(top_m.astype(float), teal, out, seam=np.maximum(hem, sleeve * 1.0))
    # Stethoscope: two dark tubes from behind the neck down the front of the chest, meeting at a steel chest piece.
    nz = neck_z - 0.02
    tube = np.maximum(front_curve([(-neck_r - .03, nz + .02), (-.06, nz - .1), (-.035, chest_z - .03)], .008),
                      front_curve([(neck_r + .03, nz + .02), (.06, nz - .1), (.035, chest_z - .03), (.03, chest_z - .07)], .008))
    out = out * (1 - tube[:, None]) + to_linear(srgb((38, 38, 42)))[None] * tube[:, None]
    q = np.stack([Px - 0.03, Pz - (chest_z - 0.09)], 1)
    piece = smooth((0.022 - np.linalg.norm(q, axis=1)) / 0.004) * (Py < front_y) * keep
    out = out * (1 - piece[:, None]) + to_linear(srgb((200, 205, 210)))[None] * piece[:, None]
    # ID badge on the left chest.
    badge = front & keep & (np.abs(Px - 0.075) < 0.022) & (np.abs(Pz - (chest_z + 0.02)) < 0.03)
    out = layer(badge.astype(float), (240, 240, 236), out, shade=0.1)
    strip = badge & (np.abs(Pz - (chest_z + 0.04)) < 0.007)
    out = layer(strip.astype(float), (190, 40, 40), out, shade=0.1)
elif OUTFIT == 'dealer':
    hem_z = hips_z - 0.03
    cuff_t = 0.9
    shirt = (torso | upper_arm | (forearm & (fore_t < cuff_t))) & keep
    shirt_m = shirt * edge(Pz - hem_z) * (1 - smooth((neck_r + 0.012 - neck_d) / 0.006) * (Pz > neck_z - 0.06))
    cuffs = forearm & (np.abs(fore_t - cuff_t) < 0.05)
    collar_band = band(neck_d - neck_r - 0.012, 0.006) * (Pz > neck_z - 0.06)
    # Waistcoat: the torso up to under the arms, a deep V at the front (the shirt shows), buttons down the middle.
    arm_hole = (np.abs(Px) > abs(joint['UpperArmL'][0][0]) - 0.035) & (Pz > chest_z - 0.02)
    v_open = front & (Pz > chest_z - 0.07) & (np.abs(Px) < (Pz - (chest_z - 0.07)) * 0.55)
    vest = torso & keep & ~arm_hole & ~v_open & (neck_d > neck_r + 0.05) & (Pz > hem_z - 0.015)
    vest_m = vest * edge(Pz - (hem_z - 0.015))
    trousers = (legs | (torso & (Pz <= hem_z + 0.02))) & keep & (Pz > ankle_z + 0.03)
    tr_m = trousers * edge(Pz - (ankle_z + 0.03))
    out = layer(tr_m.astype(float), (22, 22, 26), out)
    out = layer(shirt_m.astype(float), (238, 238, 232), out, seam=np.maximum(cuffs * 0.5, collar_band * 0.4))
    out = layer(vest_m.astype(float), (18, 18, 22), out, shade=0.25, seam=band(np.abs(Px) - (Pz - (chest_z - 0.07)) * 0.55, 0.004))
    for k in range(4):
        q = np.stack([Px, Pz - (chest_z - 0.09 - k * 0.045)], 1)
        btn = smooth((0.007 - np.linalg.norm(q, axis=1)) / 0.002) * (Py < front_y) * keep
        out = out * (1 - btn[:, None]) + to_linear(srgb((200, 160, 70)))[None] * btn[:, None]
    # Bow tie at the collar.
    bow_z = neck_z - 0.01
    bow = front & keep & (np.abs(Pz - bow_z) < 0.014 + np.abs(Px) * 0.25) & (np.abs(Px) < 0.04) & (neck_d < neck_r + 0.03)
    pinch = np.abs(Px) < 0.006
    out = layer((bow & ~pinch).astype(float), (150, 18, 28), out, shade=0.2)
    out = layer((bow & pinch).astype(float), (110, 12, 20), out, shade=0.2)
else:
    raise SystemExit('unknown outfit ' + OUTFIT)

new_rgb = to_srgb(out)
img = bpy.data.images.new('outfit', TW, TH)
pxo = np.ones((TW * TH, 4), dtype=np.float32)
pxo[:, :3] = new_rgb.astype(np.float32)
img.pixels.foreach_set(pxo.ravel())
tex_node.image = img
bpy.ops.object.select_all(action='DESELECT')
body.select_set(True)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', use_selection=True, export_skins=True,
                          export_animations=False, export_apply=False, export_yup=True,
                          export_image_format='JPEG', export_jpeg_quality=92)
log('wrote', OUT)

if PREVIEW:
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'TEXTURE'
    scene.world = bpy.data.worlds.new('w')
    scene.world.color = (0.8, 0.85, 0.9)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam'))
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = H * 1.12
    scene.collection.objects.link(cam); scene.camera = cam
    scene.render.resolution_x, scene.render.resolution_y = 700, 1000
    for name, angle in (('front', 0), ('side', 90), ('back', 180)):
        a = math.radians(angle)
        cam.location = (6 * math.sin(a), -6 * math.cos(a), H * 0.52)
        cam.rotation_euler = (math.pi / 2, 0, a)
        scene.render.filepath = f'{PREVIEW}_{name}.png'
        bpy.ops.render.render(write_still=True)
    cam.data.ortho_scale = H * 0.45
    cam.location = (0, -6, chest_z)
    cam.rotation_euler = (math.pi / 2, 0, 0)
    scene.render.resolution_x, scene.render.resolution_y = 900, 900
    scene.render.filepath = f'{PREVIEW}_chest.png'
    bpy.ops.render.render(write_still=True)
