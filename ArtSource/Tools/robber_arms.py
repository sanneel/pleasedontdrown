"""The robber's Meshy model (ArtSource/Meshy/raw/robber_raw.glb) stands with his hands in his pockets, so it can't be
rigged as it is. This takes the hands out: it cuts each hand off just past the wrist (and the bit of trouser pocket
it was buried in), closes the pocket and the wrist, swings the arms down into an A-pose (straight, a little away from
the body), and gives him new hands (simple mittens painted with his own skin from the texture). The result goes
through prepare_character.py like every other character.

blender -b --factory-startup -P ArtSource/Tools/robber_arms.py -- ArtSource/Meshy/raw/robber_raw.glb ArtSource/Meshy/raw/robber_apose.glb

Joint positions were read off ortho_grid.py renders of the raw model (Blender coordinates: Z up, he faces -Y).
"""
import sys
import math
import bpy
import bmesh
from mathutils import Vector, Matrix, Quaternion

argv = sys.argv[sys.argv.index('--') + 1:]
SRC, DST = argv[0], argv[1]

# Per arm: shoulder joint, elbow, wrist (middle of the watch on his left), the middle of the hand in the pocket.
ARMS = [
    dict(name='left', side=1.0, S=Vector((0.20, 0.13, 0.38)), E=Vector((0.37, 0.25, 0.20)),
         W=Vector((0.280, 0.088, -0.048)), P=Vector((0.209, 0.033, -0.092))),
    dict(name='right', side=-1.0, S=Vector((-0.234, 0.158, 0.34)), E=Vector((-0.36, 0.235, 0.18)),
         W=Vector((-0.217, 0.087, -0.026)), P=Vector((-0.176, 0.055, -0.092))),
]
CUT_PAST_WRIST = 0.02   # the cut is this far past the wrist point, toward the fingers (past the watch strap)
HAND_RADIUS = 0.062     # everything this close to the hand's axis beyond the cut goes (hand + pocket around it)
A_POSE = 24.0           # degrees the arms hang out from straight down
ELBOW_BEND = 12.0       # degrees the forearm stays bent forward (a dead straight arm looks stiff)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
for o in bpy.context.scene.objects: o.select_set(o in meshes)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1: bpy.ops.object.join()
ob = bpy.context.view_layer.objects.active
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
me = ob.data

# The colour texture, to find skin and trouser colours by looking.
mat = me.materials[0]
image = None
for node in mat.node_tree.nodes:
    if node.type == 'BSDF_PRINCIPLED' and node.inputs['Base Color'].is_linked:
        image = node.inputs['Base Color'].links[0].from_node.image
W_IMG, H_IMG = image.size
PIXELS = list(image.pixels[:])

def colour_at(uv):
    x = min(W_IMG - 1, max(0, int(uv.x % 1.0 * W_IMG)))
    y = min(H_IMG - 1, max(0, int(uv.y % 1.0 * H_IMG)))
    i = (y * W_IMG + x) * 4
    return PIXELS[i], PIXELS[i + 1], PIXELS[i + 2]

def is_skin(c):
    r, g, b = c
    return r > 0.45 and r > g > b and r - b > 0.18

def is_trousers(c):
    r, g, b = c
    return r + g + b < 0.75 and b >= r

bm = bmesh.new()
bm.from_mesh(me)
uv_layer = bm.loops.layers.uv.active
# glTF splits vertices along every UV seam: weld them back so the surface is one piece (the UVs live on the corners
# and stay as they are), otherwise the arm falls apart at its seams and every seam looks like a hole.
before = len(bm.verts)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
print(f"[robber] welded seams: {before} -> {len(bm.verts)} vertices, {sum(1 for e in bm.edges if e.is_boundary)} open edges left")
bm.verts.ensure_lookup_table()

def face_colour(f):
    uv = sum((l[uv_layer].uv for l in f.loops), Vector((0, 0))) / len(f.loops)
    return uv, colour_at(uv)

def uv_like(point, want, radius=0.12):
    """The UV of the nearest face around point whose colour passes want (a patch of that cloth / skin)."""
    best, best_d = None, radius
    for f in bm.faces:
        d = (f.calc_center_median() - point).length
        if d < best_d:
            uv, c = face_colour(f)
            if want(c):
                best, best_d = uv.copy(), d
    return best

def dist_to_segment(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
    return (p - (a + ab * t)).length

# ------------------------------------------------------------------ 1. hands out of the pockets
cuts = []
for arm in ARMS:
    W, P = arm['W'], arm['P']
    d = (P - W).normalized()                       # the hand's way into the pocket
    f = (W - arm['E']).normalized()                # the forearm's own direction: the cut is square across it
    C = W + f * CUT_PAST_WRIST
    arm['C'], arm['d'], arm['f'] = C, d, f
    skin_uv = uv_like(W - (W - arm['E']).normalized() * 0.06, is_skin)       # forearm, above the watch
    cloth_uv = uv_like(P + Vector((arm['side'] * 0.06, 0.0, -0.05)), is_trousers, 0.2)
    arm['skin_uv'], arm['cloth_uv'] = skin_uv, cloth_uv
    print(f"[robber] {arm['name']}: skin uv {skin_uv}, trouser uv {cloth_uv}")
    doomed = [v for v in bm.verts if (v.co - C).dot(f) > 0.0 and dist_to_segment(v.co, C, C + d * 0.22) < HAND_RADIUS]
    print(f"[robber] {arm['name']}: cutting off {len(doomed)} vertices (hand + pocket)")
    bmesh.ops.delete(bm, geom=doomed, context='VERTS')

# (The holes are closed further down, once each arm is cut free of the trousers it rests against.)
def boundary_loops():
    edges = {e for e in bm.edges if e.is_boundary}
    loops = []
    while edges:
        start = edges.pop()
        loop = [start]
        frontier = [start.verts[0], start.verts[1]]
        while frontier:
            v = frontier.pop()
            for e in v.link_edges:
                if e in edges:
                    edges.discard(e)
                    loop.append(e)
                    frontier.extend(e.verts)
        loops.append(loop)
    return loops

bm.verts.ensure_lookup_table()

# ------------------------------------------------------------------ 2. arms down into an A-pose
def rotation_between(a, b):
    return a.normalized().rotation_difference(b.normalized())

def smooth(x):
    x = max(0.0, min(1.0, x))
    return x * x * (3 - 2 * x)

for arm in ARMS:
    S, E, W, C, side = arm['S'], arm['E'], arm['W'], arm['C'], arm['side']
    down = Vector((side * math.sin(math.radians(A_POSE)), 0.0, -math.cos(math.radians(A_POSE))))
    # The forearm keeps a slight bend forward (-Y is his front).
    fore = down.copy()
    fore.rotate(Quaternion(down.cross(Vector((0, -1, 0))).normalized(), math.radians(ELBOW_BEND)))
    R1 = rotation_between(E - S, down)
    E2 = S + R1 @ (E - S)
    R2 = rotation_between(W - E, fore)
    arm['R1'], arm['R2'], arm['E2'], arm['fore'] = R1, R2, E2, fore
    upper = (E - S).normalized()
    plane = S + upper * 0.03   # vertices past this (out along the arm) belong to it

    # Which vertices are the arm: flood out from the elbow along the mesh, staying past the shoulder plane and close
    # to the bones, so the chest and the trousers stay where they are.
    seed = min(bm.verts, key=lambda v: (v.co - E).length)
    arm_verts = {seed}
    frontier = [seed]
    def near_arm(p):
        # (the sleeve hangs 6-7 cm round the bone; any wider takes in his side under the armpit)
        return dist_to_segment(p, S, E) < 0.078 or dist_to_segment(p, E, C) < 0.1
    while frontier:
        v = frontier.pop()
        for e in v.link_edges:
            o = e.other_vert(v)
            if o in arm_verts: continue
            if (o.co - plane).dot(upper) < 0.0 or not near_arm(o.co): continue
            arm_verts.add(o)
            frontier.append(o)

    # The forearm is thin (about 7 cm across at the elbow, 5 at the wrist). Whatever else came along with it, further
    # from its bone, is trousers or belt it rests against: that stays behind, and the skin joining the two is cut and
    # both sides closed (otherwise a flap of trouser would stretch out to the wrist).
    fore_len = (C - E).length
    fore_dir = (C - E) / fore_len
    def stray(p):
        t = (p - E).dot(fore_dir) / fore_len
        if t < 0.25: return False                                     # round the elbow it is all arm
        reach = 0.075 + (0.05 - 0.075) * max(0.0, min(1.0, t))
        return dist_to_segment(p, E, C) > reach
    def skin_vertex(v):
        return sum(1 for f in v.link_faces if is_skin(face_colour(f)[1])) * 2 >= len(v.link_faces)
    strays = {v for v in arm_verts if stray(v.co) and not skin_vertex(v)}
    arm_verts -= strays
    bridge = {f for v in arm_verts for f in v.link_faces
              if any(o not in arm_verts for o in f.verts) and (f.calc_center_median() - E).dot(fore_dir) > 0.25 * fore_len}
    if bridge:
        bmesh.ops.delete(bm, geom=list(bridge), context='FACES')
        arm_verts = {v for v in arm_verts if v.is_valid}
    print(f"[robber] {arm['name']}: {len(arm_verts)} arm vertices ({len(strays)} left behind, {len(bridge)} faces cut)")
    arm['count'] = len(arm_verts)
    arm['verts'], arm['plane'], arm['upper'] = arm_verts, plane, upper

# Leftovers of the old hands: whatever came along with an arm from past the wrist cut (a thumb that stuck out of the
# pocket), skin left lying at the pockets, and any small loose scraps round there.
for arm in ARMS:
    beyond = [v for v in arm['verts'] if (v.co - arm['C']).dot(arm['f']) > 0.004]
    arm['verts'] -= set(beyond)
    bmesh.ops.delete(bm, geom=beyond, context='VERTS')
    arm['verts'] = {v for v in arm['verts'] if v.is_valid}
all_arm = set().union(*(a['verts'] for a in ARMS))
def by_hand(p):
    return min(dist_to_segment(p, a['C'], a['C'] + a['d'] * 0.22) for a in ARMS) < 0.12
skin_bits = [v for v in bm.verts if v not in all_arm and by_hand(v.co) and v.link_faces and
             sum(1 for f in v.link_faces if is_skin(face_colour(f)[1])) * 2 > len(v.link_faces)]
bmesh.ops.delete(bm, geom=skin_bits, context='VERTS')
seen, scraps = set(), []
for v in bm.verts:
    if v in seen or not by_hand(v.co): continue
    island, stack = [], [v]
    seen.add(v)
    while stack:
        u = stack.pop()
        island.append(u)
        for e in u.link_edges:
            o = e.other_vert(u)
            if o not in seen:
                seen.add(o)
                stack.append(o)
    if len(island) < 400: scraps += island
bmesh.ops.delete(bm, geom=scraps, context='VERTS')
for arm in ARMS: arm['verts'] = {v for v in arm['verts'] if v.is_valid}
print(f"[robber] removed leftovers: {len(skin_bits)} skin vertices at the pockets, {len(scraps)} in loose scraps")

# Close every hole the cuts left, now that each arm is free: a lid over each wrist (the new hand covers it), patches of
# trouser over the pockets and where a forearm rested. Each gets the colour of its side (skin or trouser).
all_arm = set().union(*(a['verts'] for a in ARMS))
for loop in boundary_loops():
    verts = {v for e in loop for v in e.verts}
    centre = sum((v.co for v in verts), Vector()) / len(verts)
    arm = min(ARMS, key=lambda a: dist_to_segment(centre, a['E'], a['C'] + a['d'] * 0.2))
    if dist_to_segment(centre, arm['E'], arm['C'] + arm['d'] * 0.2) > 0.15:
        continue  # a hole that was in the download already
    ours = sum(1 for v in verts if v in all_arm) * 2 > len(verts)
    new = bmesh.ops.holes_fill(bm, edges=loop, sides=0)['faces']
    new = bmesh.ops.triangulate(bm, faces=new)['faces']
    for face in new:
        for l in face.loops: l[uv_layer].uv = arm['skin_uv'] if ours else arm['cloth_uv']
    print(f"[robber] {arm['name']}: closed a hole on the {'arm' if ours else 'trousers'} ({len(verts)} edge vertices)")

for arm in ARMS:
    S, E, W, R1, R2, E2 = arm['S'], arm['E'], arm['W'], arm['R1'], arm['R2'], arm['E2']
    plane, upper = arm['plane'], arm['upper']
    for v in arm['verts']:
        p = v.co.copy()
        # How much the upper arm carries it: nothing at the shoulder plane, all of it 8 cm out.
        wu = smooth((p - plane).dot(upper) / 0.08)
        # How much the forearm carries it: blended over 5 cm either side of the elbow.
        t_lower = (p - E).dot((W - E).normalized())
        wl = smooth((t_lower + 0.05) / 0.1)
        p1 = S + R1 @ (p - S)
        p2 = E2 + R2 @ (p - E)
        target = p1.lerp(p2, wl)
        v.co = p.lerp(target, wu)

# ------------------------------------------------------------------ 3. new hands
def ellipsoid(centre, radii, rotation, segments=14, rings=9):
    """An ellipsoid added to bm (rings of quads, a fan at each pole); returns its faces."""
    def point(th, ph):
        local = Vector((math.sin(th) * math.cos(ph) * radii.x, math.sin(th) * math.sin(ph) * radii.y, math.cos(th) * radii.z))
        return bm.verts.new(centre + rotation @ local)
    top = bm.verts.new(centre + rotation @ Vector((0, 0, radii.z)))
    bottom = bm.verts.new(centre + rotation @ Vector((0, 0, -radii.z)))
    rows = [[point(math.pi * i / rings, 2 * math.pi * j / segments) for j in range(segments)] for i in range(1, rings)]
    faces = []
    for j in range(segments):
        k = (j + 1) % segments
        faces.append(bm.faces.new((top, rows[0][j], rows[0][k])))
        faces.append(bm.faces.new((bottom, rows[-1][k], rows[-1][j])))
        for i in range(len(rows) - 1):
            faces.append(bm.faces.new((rows[i][j], rows[i + 1][j], rows[i + 1][k], rows[i][k])))
    return faces

for arm in ARMS:
    side = arm['side']
    C2 = arm['E2'] + arm['R2'] @ (arm['C'] - arm['E'])      # the wrist's cut, where it is now
    axis = arm['fore'].normalized()                          # the hand carries on down the forearm
    inward = Vector((-side, 0, 0))
    palm_normal = (inward - axis * inward.dot(axis)).normalized()   # palm toward the thigh
    across = axis.cross(palm_normal).normalized()
    # Hand frame: z along the fingers, y out of the palm, x across the knuckles.
    frame = Matrix((across, palm_normal, axis)).transposed().to_quaternion()
    created = ellipsoid(C2 + axis * 0.06, Vector((0.046, 0.022, 0.07)), frame)        # palm and fingers, one mitten
    # The thumb, on his front side of the hand, angled out a little.
    front = Vector((0, -1, 0))
    front = (front - axis * front.dot(axis)).normalized()
    thumb_frame = Matrix((axis.cross(front).normalized(), front, axis)).transposed().to_quaternion() @         Quaternion(Vector((1, 0, 0)), math.radians(-30))
    created += ellipsoid(C2 + axis * 0.045 + front * 0.03 + palm_normal * 0.01, Vector((0.015, 0.016, 0.045)), thumb_frame)
    # A cuff of skin bridging the forearm's lid into the hand.
    created += ellipsoid(C2 + axis * 0.005, Vector((0.035, 0.026, 0.03)), frame, 12, 6)
    for face in created:
        face.material_index = 0
        for l in face.loops: l[uv_layer].uv = arm['skin_uv']
    print(f"[robber] {arm['name']}: new hand at {tuple(round(x, 3) for x in C2)} pointing {tuple(round(x, 2) for x in axis)}")

bm.normal_update()
bm.to_mesh(me)
bm.free()
me.update()
for p in me.polygons: p.use_smooth = True

bpy.ops.export_scene.gltf(filepath=DST, export_format='GLB', use_selection=False)
print(f'[robber] wrote {DST}')
