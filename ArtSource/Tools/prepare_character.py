"""Turns a Meshy character (A-pose, no rig) into a game-ready skinned GLB.

blender -b --factory-startup -P ArtSource/Tools/prepare_character.py -- <in.glb> <out.glb> [options]
  --pick left|right|only   which figure to keep when the picture had several side by side (default: only)
  --height 1.72            final height in metres (top of the head, hair included)
  --tris 14000             triangle budget after decimation
  --texture 2048           largest texture size
  --bust 1                 women: add BustL/BustR spring bones (the game jiggles them, e.g. during CPR)
  --preview <prefix>       also render front/side pictures with the skeleton drawn in

What it does: keeps one figure (drops other figures and floating text), stands it on the origin facing -Y
(glTF +Z), finds the joints from the front silhouette, builds a skeleton named like AvatarRig.Bone
(Hips, Spine, Chest, Neck, Head, UpperArmL, ForearmL, HandL, ... ThighL, ShinL, FootL ...), skins the mesh with
automatic weights (4 bones per vertex max) and exports a GLB with the skin. The game maps those bones onto its
own procedural skeleton (Assets/_Game/Editor/MeshyCharacters.cs).

Character's left is +X here (it faces -Y), which becomes -X in Unity: the "L" bones sit on +X.
"""
import json
import math
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.kdtree import KDTree

argv = sys.argv[sys.argv.index('--') + 1:]
SRC, DST = argv[0], argv[1]
opts = {'pick': 'only', 'height': 1.72, 'tris': 14000, 'texture': 2048, 'bust': 0, 'preview': ''}
i = 2
while i < len(argv):
    key = argv[i].lstrip('-')
    opts[key] = type(opts[key])(argv[i + 1]) if not isinstance(opts[key], str) else argv[i + 1]
    i += 2


def log(*a):
    print('[prepare]', *a)


# ------------------------------------------------------------------ load and pick the figure

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
body = bpy.context.view_layer.objects.active
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in list(bpy.context.scene.objects):
    if o != body:
        bpy.data.objects.remove(o, do_unlink=True)

bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.remove_doubles(threshold=0.0001)  # Meshy splits vertices along UV seams: stitch them back
bpy.ops.mesh.separate(type='LOOSE')
bpy.ops.object.mode_set(mode='OBJECT')
parts = [o for o in bpy.context.scene.objects if o.type == 'MESH']


def verts_of(o):
    co = np.empty(len(o.data.vertices) * 3)
    o.data.vertices.foreach_get('co', co)
    return co.reshape(-1, 3)


info = []
for o in parts:
    v = verts_of(o)
    info.append({'obj': o, 'v': v, 'n': len(v), 'cx': float(v[:, 0].mean())})
info.sort(key=lambda p: -p['n'])
extent = max(float(np.ptp(np.vstack([p['v'] for p in info])[:, d])) for d in range(3))

# Seeds: the biggest part on each side (one figure: just the biggest).
if opts['pick'] == 'only':
    seeds = [info[0]]
else:
    lefts = [p for p in info if p['cx'] < 0]
    rights = [p for p in info if p['cx'] >= 0]
    seeds = [(lefts if opts['pick'] == 'left' else rights)[0]]
# Grow the figure: parts that touch it (within 3% of the model size) belong to it; floating text doesn't.
keep = list(seeds)
rest = [p for p in info if p not in keep]
touch = extent * 0.03
changed = True
while changed:
    changed = False
    tree = KDTree(sum(p['n'] for p in keep))
    k = 0
    for p in keep:
        for co in p['v'][:: max(1, p['n'] // 20000)]:
            tree.insert(Vector(co), k)
            k += 1
    tree.balance()
    for p in list(rest):
        step = max(1, p['n'] // 400)
        if min(tree.find(Vector(co))[2] for co in p['v'][::step]) < touch:
            keep.append(p)
            rest.remove(p)
            changed = True
log(f'parts {len(info)}: keeping {len(keep)} ({sum(p["n"] for p in keep)} verts), dropping {len(rest)}')
for p in rest:
    bpy.data.objects.remove(p['obj'], do_unlink=True)
bpy.ops.object.select_all(action='DESELECT')
for p in keep:
    p['obj'].select_set(True)
bpy.context.view_layer.objects.active = keep[0]['obj']
if len(keep) > 1:
    bpy.ops.object.join()
body = bpy.context.view_layer.objects.active
body.name = 'Body'

# ------------------------------------------------------------------ stand it on the origin, scale to height

v = verts_of(body)
lo, hi = v.min(axis=0), v.max(axis=0)
s = opts['height'] / (hi[2] - lo[2])
offset = np.array([-(lo[0] + hi[0]) / 2, -(lo[1] + hi[1]) / 2, -lo[2]])
body.data.transform(__import__('mathutils').Matrix.Translation(Vector(offset)))
body.data.transform(__import__('mathutils').Matrix.Scale(s, 4))
body.data.update()
v = verts_of(body)
H = float(v[:, 2].max())
log(f'height {H:.3f} m, width {np.ptp(v[:, 0]):.3f}, depth {np.ptp(v[:, 1]):.3f}')

# ------------------------------------------------------------------ joints from the front silhouette

CELL = 0.005
tri = np.array([list(p.vertices) for p in body.data.polygons if len(p.vertices) == 3] +
               [[p.vertices[0], p.vertices[j], p.vertices[j + 1]] for p in body.data.polygons if len(p.vertices) > 3
                for j in range(1, len(p.vertices) - 1)])
xmin = float(v[:, 0].min()) - 0.02
W = int((float(v[:, 0].max()) + 0.02 - xmin) / CELL) + 1
R = int((H + 0.02) / CELL) + 1
mask = np.zeros((R, W), dtype=bool)  # rows = z, columns = x
px = (v[:, 0] - xmin) / CELL
pz = v[:, 2] / CELL
for a, b, c in tri:
    x0, x1 = int(min(px[a], px[b], px[c])), int(max(px[a], px[b], px[c])) + 1
    z0, z1 = int(min(pz[a], pz[b], pz[c])), int(max(pz[a], pz[b], pz[c])) + 1
    if x1 - x0 > 200 or z1 - z0 > 200:
        continue
    gx, gz = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(z0, z1) + 0.5)
    ax, az, bx, bz, cx, cz = px[a], pz[a], px[b], pz[b], px[c], pz[c]
    d = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz)
    if abs(d) < 1e-12:
        mask[z0:z1, x0:x1] |= False
        continue
    l1 = ((bz - cz) * (gx - cx) + (cx - bx) * (gz - cz)) / d
    l2 = ((cz - az) * (gx - cx) + (ax - cx) * (gz - cz)) / d
    inside = (l1 >= -0.05) & (l2 >= -0.05) & (1 - l1 - l2 >= -0.05)
    mask[z0:z1, x0:x1] |= inside
center_col = int((0 - xmin) / CELL)


def runs(row):
    """(start, end) column ranges of filled cells in a row."""
    r = mask[row].astype(np.int8)
    edges = np.flatnonzero(np.diff(np.concatenate([[0], r, [0]])))
    return list(zip(edges[::2], edges[1::2]))


def col_x(c):
    return xmin + (c + 0.5) * CELL


# Armpits: from mid height go up until the arms join the torso.
row = int(0.55 * H / CELL)
while len(runs(row)) < 3 and row > int(0.3 * H / CELL):
    row -= 1  # hands hang a little lower on short-armed figures
if len(runs(row)) < 3:
    raise SystemExit('[prepare] arms not apart from the body at mid height: not an A-pose, cannot rig')
while row < R - 1 and len(runs(row + 1)) >= 3:
    row += 1
armpit_row = row
armpit_z = (armpit_row + 0.5) * CELL


def flood(seed_row, seed_col, max_row):
    """Connected cells from a seed, only below max_row (cuts the arm off at the armpit)."""
    seen = np.zeros_like(mask)
    stack = [(seed_row, seed_col)]
    seen[seed_row, seed_col] = True
    while stack:
        r, c = stack.pop()
        for dr, dc in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            rr, cc = r + dr, c + dc
            if 0 <= rr <= max_row and 0 <= cc < W and mask[rr, cc] and not seen[rr, cc]:
                seen[rr, cc] = True
                stack.append((rr, cc))
    return seen


def depth_at(x, z, radius=0.035):
    """Middle of the body's front-back extent near (x, z)."""
    near = v[(np.abs(v[:, 0] - x) < radius) & (np.abs(v[:, 2] - z) < radius)]
    return float((near[:, 1].min() + near[:, 1].max()) / 2) if len(near) else 0.0


joints = {}
arm_runs = runs(armpit_row - 2)
arms = np.zeros_like(mask)
for side, run in (('R', arm_runs[0]), ('L', arm_runs[-1])):  # -X is the character's right
    arm = flood(armpit_row - 2, (run[0] + run[1]) // 2, armpit_row - 1)
    arms |= arm
    rz, cxs = np.nonzero(arm)
    pts = np.stack([xmin + (cxs + 0.5) * CELL, (rz + 0.5) * CELL], axis=1)
    thickness = (run[1] - run[0]) * CELL
    # Arm axis (principal direction of the arm's silhouette).
    mean = pts.mean(axis=0)
    _, _, vt = np.linalg.svd(pts - mean)
    axis = vt[0] if vt[0][1] < 0 else -vt[0]  # pointing down the arm
    sz = armpit_z + 0.6 * thickness
    shoulder = mean + axis * ((sz - mean[1]) / axis[1])
    tip = pts[np.argmax(((pts - shoulder) ** 2).sum(axis=1))]
    length = float(np.linalg.norm(tip - shoulder))
    along = (tip - shoulder) / length
    elbow, wrist = shoulder + along * length * 0.40, shoulder + along * length * 0.74
    for name, p in (('UpperArm', shoulder), ('Forearm', elbow), ('Hand', wrist), ('HandTip', tip)):
        joints[name + side] = [float(p[0]), depth_at(p[0], p[1]), float(p[1])]

# Legs: where the body (arms taken out) splits in two below the armpits. Skirts and aprons hide the real
# crotch, so the hip joint is never put lower than 48% of the height.
mask &= ~arms


def wide_runs(r):
    """Runs at least 4 cm wide: legs, not the gap inside a key ring."""
    return [q for q in runs(r) if (q[1] - q[0]) * CELL >= 0.04]


row = armpit_row - 2
while row > 0 and len(wide_runs(row)) < 2:
    row -= 1
split_z = (row + 0.5) * CELL
unit = H / 1.8  # the procedural avatar is ~1.8 m tall: its proportions, scaled
hip_z = max(split_z + 0.05 * unit, 0.48 * H)
ankle_z = 0.075 * unit
leg_rows = [r for r in range(int(ankle_z / CELL) + 4, row - 2)]
for side, pick in (('R', 0), ('L', -1)):
    xs, zs = [], []
    for r in leg_rows:
        legs = wide_runs(r)
        if len(legs) >= 2:
            q = legs[pick]
            xs.append(col_x((q[0] + q[1]) / 2))
            zs.append((r + 0.5) * CELL)
    fit = np.polyfit(zs, xs, 1)  # x as a function of z along the leg
    hip = np.array([np.polyval(fit, hip_z), hip_z])
    ankle = np.array([np.polyval(fit, ankle_z), ankle_z])
    knee = ankle + (hip - ankle) * (0.39 / 0.80)
    toe_y = float(v[(np.abs(v[:, 0] - ankle[0]) < 0.06) & (v[:, 2] < ankle_z)][:, 1].min())
    for name, p in (('Thigh', hip), ('Shin', knee), ('Foot', ankle)):
        joints[name + side] = [float(p[0]), depth_at(p[0], p[1]), float(p[1])]
    joints['FootTip' + side] = [float(ankle[0]), toe_y, 0.02 * unit]

shoulder_z = (joints['UpperArmL'][2] + joints['UpperArmR'][2]) / 2
hips_z = hip_z + 0.04 * unit
k = (shoulder_z - hips_z) / 0.43  # procedural avatar: shoulders 0.43 above the hips
# The neck: the narrowest row above the shoulders (hair hanging over it: fall back to proportions).
search = range(int((shoulder_z + 0.03 * unit) / CELL), int((shoulder_z + 0.4 * (H - shoulder_z)) / CELL))
widths = [(sum(b - a for a, b in runs(r)) * CELL, r) for r in search]
neck_w, neck_row = min(widths)
head_w = max(w for w, _ in widths)
neck_z = (neck_row + 0.5) * CELL if neck_w < 0.6 * head_w else shoulder_z + 0.16 * k
log('neck', round(neck_z, 3), 'width', round(neck_w, 3), 'head width', round(head_w, 3))
for name, z in (('Hips', hips_z), ('Spine', hips_z + 0.10 * k), ('Chest', shoulder_z - 0.17 * k),
                ('Neck', (shoulder_z + neck_z) / 2), ('Head', neck_z + 0.03 * unit)):
    joints[name] = [0.0, depth_at(0.0, z, 0.06), float(z)]
joints['HeadTop'] = [0.0, joints['Head'][1], H]

# Bust: the front-most point of each side of the chest (between the shoulders and the waist, inside the torso's
# outline so arms don't count). The bone sits half a radius behind it, inside the breast.
bust_radius = {}
if opts['bust']:
    top_z, bottom_z = shoulder_z - 0.04 * unit, shoulder_z - 0.24 * unit
    torso = [q for q in runs(int((top_z + bottom_z) / 2 / CELL)) if col_x(q[0]) <= 0.0 <= col_x(q[1])]
    half = (col_x(torso[0][1]) - col_x(torso[0][0])) / 2 if torso else 0.15 * unit
    for side, sign in (('L', 1.0), ('R', -1.0)):
        band = v[(v[:, 2] > bottom_z) & (v[:, 2] < top_z) & (v[:, 0] * sign > 0.02 * unit) & (v[:, 0] * sign < 0.8 * half)]
        apex = band[np.argmin(band[:, 1])]
        middle = v[(np.abs(v[:, 0]) < 0.015 * unit) & (np.abs(v[:, 2] - apex[2]) < 0.03 * unit)]
        stands_out = (float(middle[:, 1].min()) - float(apex[1])) if len(middle) else 0.0
        r = float(np.clip(abs(apex[0]) * 0.95, 0.045 * unit, 0.1 * unit))
        bust_radius[side] = r
        joints['Bust' + side] = [float(apex[0]), float(apex[1]) + 0.5 * r, float(apex[2])]
        joints['BustTip' + side] = [float(apex[0]), float(apex[1]) - 0.02, float(apex[2])]
        log(f'bust {side}: radius {r:.3f}, stands out {stands_out * 100:.1f} cm in front of the breastbone')
        if stands_out < 0.012 * unit:
            log(f'WARNING bust {side}: the chest is almost flat, the jiggle will hardly show (regenerate with a fuller bust)')
log('armpit', round(armpit_z, 3), 'legs split', round(split_z, 3), 'shoulders', round(shoulder_z, 3))
for name, p in joints.items():
    log(f'  {name:10s} {p[0]:+.3f} {p[1]:+.3f} {p[2]:.3f}')

# ------------------------------------------------------------------ skeleton + skin

arm_data = bpy.data.armatures.new('Rig')
rig = bpy.data.objects.new('Rig', arm_data)
bpy.context.scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode='EDIT')
BONES = [  # name, parent, tail joint
    ('Hips', None, 'Spine'), ('Spine', 'Hips', 'Chest'), ('Chest', 'Spine', 'Neck'), ('Neck', 'Chest', 'Head'),
    ('Head', 'Neck', 'HeadTop'),
]
for sd in 'LR':
    BONES += [(f'UpperArm{sd}', 'Chest', f'Forearm{sd}'), (f'Forearm{sd}', f'UpperArm{sd}', f'Hand{sd}'),
              (f'Hand{sd}', f'Forearm{sd}', f'HandTip{sd}'), (f'Thigh{sd}', 'Hips', f'Shin{sd}'),
              (f'Shin{sd}', f'Thigh{sd}', f'Foot{sd}'), (f'Foot{sd}', f'Shin{sd}', f'FootTip{sd}')]
for name, parent, tail in BONES:
    b = arm_data.edit_bones.new(name)
    b.head = Vector(joints[name])
    b.tail = Vector(joints[tail])
    if parent:
        b.parent = arm_data.edit_bones[parent]
bpy.ops.object.mode_set(mode='OBJECT')

bpy.ops.object.select_all(action='DESELECT')
body.select_set(True)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.parent_set(type='ARMATURE_AUTO')

# Heat weighting leaves some vertices of messy meshes empty: give those to the nearest bone.
groups = {g.index: g.name for g in body.vertex_groups}
segs = {name: (np.array(joints[name]), np.array(joints[tail])) for name, _, tail in BONES}
empty = 0
for vert in body.data.vertices:
    if sum(g.weight for g in vert.groups) > 1e-4:
        continue
    p = np.array(vert.co)
    best, dist = None, 1e9
    for name, (a, b) in segs.items():
        ab = b - a
        t = np.clip(np.dot(p - a, ab) / max(1e-9, np.dot(ab, ab)), 0, 1)
        d = np.linalg.norm(p - (a + ab * t))
        if d < dist:
            best, dist = name, d
    body.vertex_groups[best].add([vert.index], 1.0, 'REPLACE')
    empty += 1
log(f'{empty} vertices had no weight (given to the nearest bone)')

if opts['bust']:
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode='EDIT')
    for side in 'LR':
        b = arm_data.edit_bones.new('Bust' + side)
        b.head = Vector(joints['Bust' + side])
        b.tail = Vector(joints['BustTip' + side])
        b.parent = arm_data.edit_bones['Chest']
    bpy.ops.object.mode_set(mode='OBJECT')
    arm_groups = {g.index for g in body.vertex_groups if g.name.startswith(('UpperArm', 'Forearm', 'Hand'))}
    for side, sign in (('L', 1.0), ('R', -1.0)):
        group = body.vertex_groups.new(name='Bust' + side)
        center, r = np.array(joints['Bust' + side]), bust_radius[side]
        count = 0
        for vert in body.data.vertices:
            p = np.array(vert.co)
            if p[0] * sign < 0.005 or p[1] > center[1] + 0.3 * r:
                continue  # other side, or the back
            d = np.linalg.norm((p - center) / np.array([1.0, 1.0, 1.15]))
            w = 1.0 - d / (1.3 * r)
            if w <= 0.0 or sum(g.weight for g in vert.groups if g.group in arm_groups) > 0.2:
                continue
            w = w * w * (3.0 - 2.0 * w) * 0.9  # smooth falloff to the chest
            for g in vert.groups:
                g.weight *= 1.0 - w
            group.add([vert.index], w, 'REPLACE')
            count += 1
        log(f'bust {side}: {count} vertices')

bpy.context.view_layer.objects.active = body
bpy.ops.object.select_all(action='DESELECT')
body.select_set(True)
bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)

# ------------------------------------------------------------------ decimate (weights and UVs carry over)

before = len(body.data.polygons)
tris_now = sum(len(p.vertices) - 2 for p in body.data.polygons)
dec = body.modifiers.new('Decimate', 'DECIMATE')
dec.ratio = min(1.0, opts['tris'] / max(1, tris_now))
dec.use_collapse_triangulate = True
body.modifiers.move(body.modifiers.find('Decimate'), 0)  # before the armature modifier
bpy.ops.object.modifier_apply(modifier='Decimate')
log(f'triangles {tris_now} -> {sum(len(p.vertices) - 2 for p in body.data.polygons)}')

for img in bpy.data.images:
    if img.size[0] > opts['texture']:
        img.scale(opts['texture'], opts['texture'] * img.size[1] // img.size[0])
        log(f'texture {img.name} -> {img.size[0]}x{img.size[1]}')

# ------------------------------------------------------------------ export

bpy.ops.object.select_all(action='DESELECT')
body.select_set(True)
rig.select_set(True)
bpy.ops.export_scene.gltf(filepath=DST, export_format='GLB', use_selection=True, export_skins=True,
                          export_animations=False, export_apply=False, export_yup=True,
                          export_image_format='JPEG', export_jpeg_quality=90)
log('wrote', DST)

# ------------------------------------------------------------------ preview (model + skeleton)

if opts['preview']:
    json.dump({'height': H, 'joints': joints}, open(opts['preview'] + '_joints.json', 'w'), indent=1)
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.color_type = 'TEXTURE'
    scene.display.shading.show_xray = True
    scene.display.shading.xray_alpha = 0.6
    scene.render.resolution_x, scene.render.resolution_y = 800, 800
    scene.world = bpy.data.worlds.new('w')
    scene.world.color = (0.85, 0.88, 0.92)
    # Joints as little red spheres (armatures don't render).
    mat = bpy.data.materials.new('joint')
    mat.diffuse_color = (1, 0, 0, 1)
    for name, p in joints.items():
        bpy.ops.mesh.primitive_uv_sphere_add(radius=0.018, location=p)
        bpy.context.active_object.data.materials.append(mat)
    scene.display.shading.color_type = 'MATERIAL'
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam'))
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = H * 1.1
    scene.collection.objects.link(cam)
    scene.camera = cam
    for view, d in (('front', (0, -1, 0)), ('side', (1, 0, 0))):
        cam.location = Vector((0, 0, H / 2)) + Vector(d) * 5
        cam.rotation_euler = (Vector((0, 0, H / 2)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = f"{opts['preview']}_{view}.png"
        bpy.ops.render.render(write_still=True)
