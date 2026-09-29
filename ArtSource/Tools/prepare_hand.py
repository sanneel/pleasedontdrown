"""
Meshy hand -> first-person hand template for the game (Assets/_Game/Resources/FirstPersonHand.json).

    blender -b --factory-startup -P ArtSource/Tools/prepare_hand.py -- <raw.glb> <out.json> [--preview <prefix>]
        [--shrink 0.02] [--flat 0.88] [--narrow 0.94] [--ratio 0.2]

The raw model is an open hand standing up (fingers +Z, palm facing -Y, thumb toward -X), like
ArtSource/Meshy/raw/hand_raw.glb. The script:
  1. thins it (pulls the surface in along its normals, flattens and narrows a little) and decimates it,
  2. finds the five fingertips and the webs between the fingers, and from them each finger's line and joints,
  3. writes it in HandBones space (right hand: fingers -Y, palm facing -X, thumb side +Z, wrist at the origin,
     scaled to the game's hand size) with per-vertex weights on the wrist and the 15 finger bones.
The game builds both first-person hands from it (the left one mirrored) and moves them with its own finger bones.
"""
import bpy, bmesh, sys, json, math
import numpy as np
from collections import defaultdict
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src, out = argv[0], argv[1]
opts = {argv[i][2:]: argv[i + 1] for i in range(2, len(argv) - 1, 2)}
shrink = float(opts.get("shrink", 0.02))
flat = float(opts.get("flat", 0.88))
narrow = float(opts.get("narrow", 0.94))
ratio = float(opts.get("ratio", 0.2))
preview = opts.get("preview")

# The game's hand (HandBones, right hand, scale 1): the wrist is the origin and the middle fingertip ends about here.
GAME_TIP = 0.19
SEGMENTS = [  # share of each finger's length per segment (HandBones.Lengths)
    [0.032, 0.024, 0.02], [0.037, 0.023, 0.019], [0.04, 0.025, 0.02], [0.037, 0.024, 0.019], [0.029, 0.019, 0.016]]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
obj = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
bpy.context.view_layer.objects.active = obj
obj.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.remove_doubles(threshold=0.0001)
bpy.ops.object.mode_set(mode='OBJECT')
try:
    bpy.ops.mesh.customdata_custom_splitnormals_clear()
except Exception:
    pass

# ---- 1. thinner
me = obj.data
bm = bmesh.new()
bm.from_mesh(me)
bm.normal_update()
groups = defaultdict(list)
for v in bm.verts:
    groups[tuple(round(x, 5) for x in v.co)].append(v)
for vs in groups.values():
    n = Vector()
    for v in vs:
        n += v.normal
    if n.length > 1e-8:
        n.normalize()
        for v in vs:
            v.co -= n * shrink
bm.to_mesh(me)
bm.free()
obj.scale = (narrow, flat, 1.0)
bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
mod = obj.modifiers.new("dec", 'DECIMATE')
mod.ratio = ratio
bpy.ops.object.modifier_apply(modifier="dec")
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.quads_convert_to_tris()
bpy.ops.object.mode_set(mode='OBJECT')
me = obj.data
me.calc_loop_triangles()

P = np.array([v.co[:] for v in me.vertices])
print("verts", len(P), "tris", len(me.loop_triangles))

# ---- 2. fingers: tips are the far points of the fan seen from low in the palm, webs the dips between them
zmin, zmax = P[:, 2].min(), P[:, 2].max()
length = zmax - zmin
upper = P[P[:, 2] > zmin + 0.25 * length]
center = np.array([np.median(upper[:, 0]), 0.0, zmin + 0.42 * length])  # low on the palm
rel = P[:, [0, 2]] - center[[0, 2]]
ang = np.degrees(np.arctan2(rel[:, 0], rel[:, 1]))  # 0 = straight up, - toward the thumb (-X)
rad = np.hypot(rel[:, 0], rel[:, 1])
bins = np.arange(-120, 121, 2)
prof = np.full(len(bins), 0.0)
arg = np.full(len(bins), -1)
for i, b in enumerate(bins):
    m = np.where((ang >= b - 1) & (ang < b + 1))[0]
    if len(m):
        j = m[np.argmax(rad[m])]
        prof[i], arg[i] = rad[j], j
# Peaks: local maxima that stand out; keep the five highest, ordered from the thumb side.
peaks = [i for i in range(2, len(bins) - 2) if prof[i] > 0 and prof[i] == max(prof[i - 2:i + 3])]
peaks = sorted(sorted(peaks, key=lambda i: -prof[i])[:5])
assert len(peaks) == 5, f"found {len(peaks)} fingertips"
tips = [P[arg[i]] for i in peaks]
webs = []
for a, b in zip(peaks, peaks[1:]):
    k = a + int(np.argmin(prof[a:b + 1]))
    webs.append(P[arg[k]])
print("tips", [t.round(3).tolist() for t in tips])
print("webs", [w.round(3).tolist() for w in webs])

# Wrist: the crease above the stump, where the outline widens into the palm.
zs = np.linspace(zmin, zmin + 0.5 * length, 40)
widths = [np.ptp(P[np.abs(P[:, 2] - z) < length * 0.012][:, 0]) if np.any(np.abs(P[:, 2] - z) < length * 0.012) else 0 for z in zs]
stump = np.median(widths[2:10])
wz = next((z for z, w in zip(zs, widths) if w > stump * 1.25), zmin + 0.3 * length)
ring = P[np.abs(P[:, 2] - wz) < length * 0.02]
wrist = np.array([(ring[:, 0].min() + ring[:, 0].max()) / 2, (ring[:, 1].min() + ring[:, 1].max()) / 2, wz])

# Fingers: each finger's skin beyond the webs (in its slice of the fan) gives its axis (principal direction through
# its middle, so the bones run down the centre of the finger in depth too); the knuckle sits a little below the webs.
web_ang = [float(np.degrees(np.arctan2(w[0] - center[0], w[2] - center[2]))) for w in webs]
web_rad = [float(np.hypot(w[0] - center[0], w[2] - center[2])) for w in webs]
fingers = []
for f in range(5):
    lo = web_ang[f - 1] if f > 0 else -180.0
    hi = web_ang[f] if f < 4 else 180.0
    near = [web_rad[i] for i in (f - 1, f) if 0 <= i < 4]
    m = (ang > lo) & (ang < hi) & (rad > min(near) * 0.98)
    pts = P[m]
    mean = pts.mean(axis=0)
    _, _, vt = np.linalg.svd(pts - mean)
    axis = vt[0] if vt[0] @ (tips[f] - mean) > 0 else -vt[0]
    web_proj = min((webs[i] - mean) @ axis for i in (f - 1, f) if 0 <= i < 4)
    tip_proj = (tips[f] - mean) @ axis
    back = {0: 0.22, 4: 0.13}.get(f, 0.05)  # the thumb starts low on the palm; the pinky's web sits high
    start_proj = web_proj - back * length
    base = mean + axis * start_proj
    seg = np.array(SEGMENTS[f]) / sum(SEGMENTS[f])
    L_bone = (tip_proj - start_proj) * 0.93  # the tip point is on the skin: the last bone ends a little short
    if f == 0:
        # The thumb's first bone starts in the ball of the thumb (between the wrist and the thumb web), mid-depth,
        # and runs straight to the tip.
        base = wrist + (webs[0] - wrist) * 0.4
        slab = P[np.linalg.norm(P[:, [0, 2]] - base[[0, 2]], axis=1) < 0.04 * length]
        base[1] = (slab[:, 1].min() + slab[:, 1].max()) / 2
        axis = tips[0] - base
        L_bone = np.linalg.norm(axis) * 0.95
        axis = axis / np.linalg.norm(axis)
    fingers.append([base + axis * L_bone * s for s in np.concatenate([[0], np.cumsum(seg)])])

# ---- 3. into game space
tip_mid = tips[2]
scale = GAME_TIP / (tip_mid[2] - wrist[2])
def to_game(p):
    q = (np.asarray(p) - wrist) * scale
    # blender (x: thumb side is -X, y: palm faces -Y, z: fingers) -> game (palm faces -X, fingers -Y, thumb +Z)
    return [float(q[1]), float(-q[2]), float(-q[0])]
def dir_game(v):
    return [float(v[1]), float(-v[2]), float(-v[0])]

# Weights: nearest bone segment (the palm/wrist counts as a bone too), blended across each joint.
def seg_dist(p, a, b):
    ab = b - a
    t = np.clip(((p - a) @ ab) / (ab @ ab), 0, 1)
    return np.linalg.norm(p - (a + np.outer(t, ab)), axis=1), t

palm_a, palm_b = wrist - np.array([0, 0, 0.15 * length]), (np.mean([f[0] for f in fingers[1:]], axis=0))
d_palm, _ = seg_dist(P, palm_a, palm_b)
best = np.full(len(P), -1)
best_d = d_palm.copy()
best_t = np.zeros(len(P))
for f in range(5):
    for s in range(3):
        d, t = seg_dist(P, fingers[f][s], fingers[f][s + 1])
        if s == 2:
            d, t = seg_dist(P, fingers[f][s], fingers[f][s + 1] + (fingers[f][3] - fingers[f][2]) * 0.6)
        m = d < best_d
        best[m], best_d[m], best_t[m] = f * 3 + s, d[m], t[m]
# Skin before a finger's base belongs to the palm (the knuckles bend, the back of the hand does not).
for f in range(5):
    a, b = fingers[f][0], fingers[f][1]
    ab = b - a
    t0 = ((P - a) @ ab) / (ab @ ab)
    m = (best == f * 3) & (t0 < -0.05)
    best[m] = -1

w_idx = np.zeros((len(P), 2), dtype=int)
w_val = np.zeros((len(P), 2))
for i in range(len(P)):
    b, t = best[i], best_t[i]
    if b < 0:
        w_idx[i] = [-1, -1]; w_val[i] = [1, 0]
        continue
    f, s = divmod(b, 3)
    parent = -1 if s == 0 else b - 1
    child = b + 1 if s < 2 else b
    if t < 0.25:
        k = 0.5 + 0.5 * (t / 0.25)
        w_idx[i] = [b, parent]; w_val[i] = [k, 1 - k]
    elif t > 0.75 and s < 2:
        k = 1 - 0.5 * ((t - 0.75) / 0.25)
        w_idx[i] = [b, child]; w_val[i] = [k, 1 - k]
    else:
        w_idx[i] = [b, b]; w_val[i] = [1, 0]

tris = [list(t.vertices) for t in me.loop_triangles]
# Mirroring y/z swap keeps winding? blender->game map has det +1 and blender is right-handed, Unity left-handed:
# the same numbers read as a mirror image, so flip the winding to keep faces outward.
tris = [[t[0], t[2], t[1]] for t in tris]

# ---- 4. game proportions: the Meshy palm is a thick cushion with the fingers set on its back edge, so a handle
# held in the curled fingers would sit inside the palm. Pull the palm side (and the ball of the thumb) back toward
# the knuckles, and shorten the wrist stump to a stub.
G = np.array([to_game(p) for p in P])
bases_g = np.array([to_game(f[0]) for f in fingers])
knuckle_x = bases_g[1:, 0].mean() - 0.012 / 1.0   # a finger's radius in front of the finger bones
knuckle_y = bases_g[1:, 1].mean()
palm_k = float(opts.get("palm", 0.35))
def smooth(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)
def reshape(G):
    """One smooth field for skin and bones alike, so the bones stay inside the skin they move."""
    G = G.copy()
    blend = smooth(knuckle_y - 0.005, knuckle_y + 0.025, G[:, 1])
    front = np.clip((knuckle_x - G[:, 0]) / 0.004, 0, 1)  # only the palm side, easing in
    squashed = knuckle_x - (knuckle_x - G[:, 0]) * palm_k
    G[:, 0] = G[:, 0] + (squashed - G[:, 0]) * blend * front
    stub = 0.015
    G[:, 1] = np.where(G[:, 1] > stub, stub + (G[:, 1] - stub) * 0.35, G[:, 1])
    return G
G = reshape(G)
J = reshape(np.array([to_game(j) for f in fingers for j in f])).reshape(5, 4, 3)
# Where the palm touches what it holds: the palm's surface below the middle knuckle.
near = (np.abs(G[:, 1] - (knuckle_y + 0.025)) < 0.01) & (np.abs(G[:, 2] - bases_g[2, 2]) < 0.02)
palm_contact = [float(G[near, 0].min()), float(knuckle_y + 0.025), float(bases_g[2, 2])]
print("palm contact", palm_contact, "knuckle x", knuckle_x)

data = {
    "palm": palm_contact,
    "note": "Generated by ArtSource/Tools/prepare_hand.py from a Meshy model. Right hand, HandBones space, scale 1.",
    "vertices": [round(float(c), 5) for c in G.reshape(-1)],
    "triangles": [i for t in tris for i in t],
    "bone0": w_idx[:, 0].tolist(), "bone1": w_idx[:, 1].tolist(),
    "weight0": [round(float(x), 3) for x in w_val[:, 0]],
    "bases": [float(c) for f in range(5) for c in J[f, 0]],
    "lengths": [round(float(np.linalg.norm(J[f, k + 1] - J[f, k])), 5) for f in range(5) for k in range(3)],
    "directions": [float(c) for f in range(5) for c in (J[f, 1] - J[f, 0]) / np.linalg.norm(J[f, 1] - J[f, 0])],
}
with open(out, "w") as fh:
    json.dump(data, fh)
print("wrote", out, "scale", scale)

if preview:
    # joints as spheres over the mesh, front and side
    for f in fingers:
        for j in f:
            bpy.ops.mesh.primitive_uv_sphere_add(radius=0.025, location=Vector(j.tolist()))
    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.04, location=Vector(wrist.tolist()))
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.show_xray = True
    sc.render.resolution_x = sc.render.resolution_y = 600
    cam = bpy.data.objects.new("c", bpy.data.cameras.new("c"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = 2.2
    for name, d in [("front", (0, -1, 0)), ("side", (1, 0, 0))]:
        d = Vector(d)
        cam.location = d * 4
        cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = f"{preview}_{name}.png"
        bpy.ops.render.render(write_still=True)
