"""Polish for characters made from million-triangle Tripo models (prepare_character.py's reduce-and-rebake path): the
face and the head get most of the texture, the colour is baked again from the full-detail model, and its shape too, as
a normal map. The shape, the rig and the weights of the game body stay exactly as they are.

What was wrong: prepare_character.py gave the reduced body Smart UV Project islands (hundreds of small pieces round
every hair groove and fold, the face cut through the nose and the eyes, as many texels per centimetre as the shins)
and baked only the colour, at 2048. Up close the face was soft, with streaks where the islands met, the black gutters
crept into the island edges as the texture got smaller (mipmaps), and the hair strands, lids, lips and folds of the
source were gone.

Here the islands are cut on a smoothed copy of the body (no pieces round every groove): the front of the face in one
piece, the rest of the head and the body by Smart UV Project; then every island is flattened on the real surface
(Minimum Stretch, no flips). Every island gets the same texel density, then the face --face times and the rest of the
head --head times, and they are packed (any face still overlapping another is cut out on its own). Colour and
tangent-space normals are baked from the full-detail source, placed exactly as prepare_character.py placed it, at
--bake pixels (its metal turned off first: Cycles bakes metal's colour black), with several cages (see the bake
below); texels no ray got right, and face texels whose ray overshot, take the colour of the nearest full-detail
surface. polish_character_texture.py then makes the game textures from the bakes (down to 2048, gutters filled) and
points the GLB at the colour; MeshyCharacters picks up the normal map.

blender -b --factory-startup -P ArtSource/Tools/polish_tripo_character.py -- <game.glb> <source.glb> <out.glb> <bake.png>
    [--turn -90] [--bake 4096] [--face 3.0] [--head 1.8] [--cages 0.0012,0.003,0.006] [--review <prefix>]
--review: also render the source, the body as it was and as polished (face front and three-quarter, torso, back);
--look x,y,z adds a close look at one spot. --debug <png>: which cage each texel took; --probe x0,x1,y0,y1,z0,z1: what
the rays met in a box.
"""
import math
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index('--') + 1:]
GAME, SOURCE, OUT, BAKE = argv[:4]
opts = {'turn': -90.0, 'bake': 4096, 'face': 3.0, 'head': 1.8, 'review': '', 'margin': 0.003, 'samples': 4,
        'smooth': 20, 'angle': 66.0, 'cages': '0.0012,0.003,0.006', 'reach': 0.004,
        'debug': '', 'probe': '', 'look': '',
        'facing': 0.2, 'iterations': 10, 'slack': 0.0007, 'dark': 0.13,
        'nostrils': 0.0}
i = 4
while i < len(argv):
    key = argv[i].lstrip('-')
    opts[key] = type(opts[key])(argv[i + 1])
    i += 2


def log(*a):
    print('[polish]', *a, flush=True)


def world_verts(o):
    co = np.empty(len(o.data.vertices) * 3)
    o.data.vertices.foreach_get('co', co)
    m = np.array(o.matrix_world)
    return co.reshape(-1, 3) @ m[:3, :3].T + m[:3, 3]


bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
view_layer = bpy.context.view_layer

# ------------------------------------------------------------------ the game body (seams welded: new UVs to come)

bpy.ops.import_scene.gltf(filepath=GAME, merge_vertices=True)
rig = next(o for o in scene.objects if o.type == 'ARMATURE')
body = next(o for o in scene.objects if o.type == 'MESH')
gv = world_verts(body)
top = float(gv[:, 2].max())
H = top - float(gv[:, 2].min())
log(f'game body {body.name}: {len(gv)} vertices, {len(body.data.polygons)} faces, top {top:.4f}')

# ------------------------------------------------------------------ the full-detail source, placed as before

before = set(scene.objects)
bpy.ops.import_scene.gltf(filepath=SOURCE)
new = [o for o in scene.objects if o not in before]
parts = [o for o in new if o.type == 'MESH']
for o in parts:
    o.data.transform(o.matrix_world)
for o in parts:
    o.parent = None
    o.matrix_world = Matrix.Identity(4)
for o in new:
    if o.type != 'MESH':
        bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.object.select_all(action='DESELECT')
for o in parts:
    o.select_set(True)
view_layer.objects.active = parts[0]
if len(parts) > 1:
    bpy.ops.object.join()
detail = view_layer.objects.active
detail.name = '_Detail'
if opts['turn']:
    detail.data.transform(Matrix.Rotation(math.radians(opts['turn']), 4, 'Z'))
sv = world_verts(detail)
slo, shi = sv.min(0), sv.max(0)
# prepare_character.py: centred on x and y, feet on 0, scaled to the height (the game body's top: decimation keeps it).
detail.data.transform(Matrix.Translation(Vector((-(slo[0] + shi[0]) / 2, -(slo[1] + shi[1]) / 2, -slo[2]))))
detail.data.transform(Matrix.Scale(top / (shi[2] - slo[2]), 4))
detail.data.update()
log(f'source: {len(detail.data.vertices)} vertices, {len(detail.data.polygons)} faces')

depsgraph = bpy.context.evaluated_depsgraph_get()
surface = BVHTree.FromObject(detail, depsgraph)
sample = gv[:: max(1, len(gv) // 4000)]
dist = np.array([surface.find_nearest(Vector(p))[3] or 0.0 for p in sample])
log(f'fit to the source: mean {dist.mean() * 1000:.2f} mm, 95% {np.percentile(dist, 95) * 1000:.2f} mm, max {dist.max() * 1000:.1f} mm')
if np.percentile(dist, 95) > 0.01:
    raise SystemExit('[polish] the source does not sit on the game body: wrong --turn or a different model?')

# Metal bakes black (Cycles' diffuse colour is the base colour times one minus metallic): the colour as painted.
for mat in detail.data.materials:
    if mat and mat.node_tree:
        for node in mat.node_tree.nodes:
            if node.type == 'BSDF_PRINCIPLED':
                for link in list(node.inputs['Metallic'].links):
                    mat.node_tree.links.remove(link)
                node.inputs['Metallic'].default_value = 0.0

# ------------------------------------------------------------------ regions: the face, the rest of the head, the body

me = body.data
old = body.copy()
old.data = me.copy()
old.data.materials[0] = me.materials[0].copy()
old.name = '_Before'
scene.collection.objects.link(old)

# The islands are chosen on a smoothed copy: Smart UV Project on the real surface (hair grooves, cloth folds, the
# reduction's own facets) cut it into ~1800 pieces, most of the texture going on the gaps between them.
smooth = body.copy()
smooth.data = me.copy()
smooth.name = '_Smooth'
scene.collection.objects.link(smooth)
for mod in list(smooth.modifiers):
    smooth.modifiers.remove(mod)
bpy.ops.object.select_all(action='DESELECT')
smooth.select_set(True)
view_layer.objects.active = smooth
mod = smooth.modifiers.new('Smooth', 'SMOOTH')
mod.factor = 1.0
mod.iterations = opts['smooth']
bpy.ops.object.modifier_apply(modifier=mod.name)
sme = smooth.data

mw = body.matrix_world
nm = mw.to_3x3()
head_group = body.vertex_groups['Head'].index
head_w = np.zeros(len(me.vertices))
for v in me.vertices:
    for g in v.groups:
        if g.group == head_group:
            head_w[v.index] = g.weight
head_pts = gv[head_w > 0.5]
head_mid = head_pts.mean(0)
head_half = (np.percentile(head_pts[:, 0], 98) - np.percentile(head_pts[:, 0], 2)) / 2
head_joint = rig.matrix_world @ rig.data.bones['Head'].head_local  # (where the neck meets the head)
eyes = head_joint.z + 0.13 * H / 1.7
brow_top = head_joint.z + 0.21 * H / 1.7
region = np.zeros(len(me.polygons), np.int8)  # 0 body, 1 head, 2 face
for p in me.polygons:
    if np.mean([head_w[v] for v in p.vertices]) < 0.5:
        continue
    c = mw @ p.center
    n = (nm @ sme.polygons[p.index].normal).normalized()
    # (Below the top of the forehead: a hat's front isn't face.)
    front = -n.y > 0.3 and c.y < head_mid[1] and abs(c.x - head_mid[0]) < 0.8 * head_half and c.z < brow_top
    region[p.index] = 2 if front else 1
face_pts = np.array([mw @ me.polygons[k].center for k in np.nonzero(region == 2)[0]])
face_mid = face_pts.mean(0)
log(f'regions: face {int((region == 2).sum())}, head {int((region == 1).sum())}, body {int((region == 0).sum())} faces')

# ------------------------------------------------------------------ new UVs

scene.tool_settings.use_uv_select_sync = True
for mesh in (me, sme):
    for uv in list(mesh.uv_layers):
        mesh.uv_layers.remove(uv)
    mesh.uv_layers.new(name='UVMap')


def edit(o):
    if bpy.context.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    view_layer.objects.active = o
    bpy.ops.object.mode_set(mode='EDIT')
    bm_ = bmesh.from_edit_mesh(o.data)
    bm_.faces.ensure_lookup_table()
    return bm_


def pick(bm_, mesh, regions):
    for f in bm_.faces:
        f.select_set(False)
    for f in bm_.faces:
        if region[f.index] in regions:
            f.select_set(True)
    bm_.select_flush_mode()
    bmesh.update_edit_mesh(mesh)


def read_uvs(mesh=None):
    mesh = mesh or me
    out = np.empty(len(mesh.loops) * 2)
    mesh.uv_layers.active.data.foreach_get('uv', out)
    return out.reshape(-1, 2)


def island_ids(uvs):
    """Each face's UV island: faces joined through edges whose two sides share their UVs (body and copy alike)."""
    parent = list(range(len(me.polygons)))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    corner = {}  # (face, vertex) -> loop
    sides = {}
    for p in me.polygons:
        for li in p.loop_indices:
            loop = me.loops[li]
            corner[p.index, loop.vertex_index] = li
            sides.setdefault(loop.edge_index, []).append(p.index)
    for e, faces in sides.items():
        if len(faces) != 2:
            continue
        fa, fb = faces
        if all(np.allclose(uvs[corner[fa, v]], uvs[corner[fb, v]], atol=1e-6) for v in me.edges[e].vertices):
            parent[find(fa)] = find(fb)
    return np.array([find(k) for k in range(len(me.polygons))])


def choose(mesh, faces):
    bm_ = bmesh.from_edit_mesh(mesh)
    bm_.faces.ensure_lookup_table()
    for f in bm_.faces:
        f.select_set(False)
    for k in faces:
        bm_.faces[k].select_set(True)
    bm_.select_flush_mode()
    bmesh.update_edit_mesh(mesh)
    return bm_


def lying_over(mesh):
    """The islands packed apart as they are; the faces then lying over others (folds and overhangs)."""
    choose(mesh, range(len(mesh.polygons)))
    bpy.ops.uv.select_all(action='SELECT')  # (Blender 5 keeps the UVs' own selection, even in sync mode)
    bpy.ops.uv.pack_islands(rotate=False, scale=True, margin_method='FRACTION', margin=0.002)
    bpy.ops.uv.select_overlap(extend=False)
    return [f.index for f in bmesh.from_edit_mesh(mesh).faces if f.select]


def cut_out(mesh, faces):
    """Each of these faces an island of its own (one triangle can't fold)."""
    bm_ = choose(mesh, faces)
    for k in faces:
        for e in bm_.faces[k].edges:
            e.seam = True
    bmesh.update_edit_mesh(mesh)
    bpy.ops.uv.unwrap(method='ANGLE_BASED', margin=0.0)


# The islands are cut on the smoothed copy (no piece round every groove): the front of the face in one piece,
# everything else by Smart UV Project; a projected piece that overhangs itself (an ear, a lid, a curl of hair) is
# flattened instead, and the odd triangle that still folds over is cut out on its own. Then every island is flattened
# again on the real surface (Minimum Stretch, no flips) below.
# Tried and dropped: angle-based flattening (folds and slivers: the shirt's white on a collar ribbon, the pirate
# king's pupil smeared across his eye white); projecting the face (the lids landed on the eyes); keeping the layout
# from the smoothed copy (the eyes and nose got the texels of a smoothed-away bump); cutting out every overlapping
# face (a face came out a smeared mosaic of single triangles).
FLATTEN = dict(method='MINIMUM_STRETCH', no_flip=True, iterations=opts['iterations'], margin=0.0)
ebm = edit(smooth)
pick(ebm, sme, {2})
bpy.ops.uv.unwrap(**FLATTEN)
for r in (1, 0):
    pick(bmesh.from_edit_mesh(sme), sme, {r})
    bpy.ops.uv.smart_project(angle_limit=math.radians(opts['angle']), island_margin=0.0, area_weight=0.0,
                             correct_aspect=True, scale_to_bounds=False)
choose(sme, range(len(sme.polygons)))
bpy.ops.uv.select_all(action='SELECT')
bpy.ops.uv.seams_from_islands(mark_seams=True, mark_sharp=False)
over = lying_over(sme)
if over:
    bpy.ops.object.mode_set(mode='OBJECT')
    island = island_ids(read_uvs(sme))
    redo = {island[k] for k in over if region[k] != 2}
    bpy.ops.object.mode_set(mode='EDIT')
    if redo:
        choose(sme, [k for k in range(len(sme.polygons)) if island[k] in redo])
        bpy.ops.uv.unwrap(**FLATTEN)
    log(f'{len(over)} faces lay over others: {len(redo)} projected islands flattened instead')
    over = lying_over(sme)
    if over:
        cut_out(sme, over)
        log(f'{len(over)} faces folded: cut out, each an island of its own')
bpy.ops.object.mode_set(mode='OBJECT')
uv = np.empty(len(me.loops) * 2)
sme.uv_layers.active.data.foreach_get('uv', uv)
me.uv_layers.active.data.foreach_set('uv', uv)
bpy.data.objects.remove(smooth, do_unlink=True)

# The islands flattened again on the real surface (Minimum Stretch, no flips): laid out on the smoothed copy, the eyes
# and the nose got the texels of a smoothed-away bump (the pirate king's pupil at 0.1 texel per mm: smeared).
ebm = edit(body)
choose(me, range(len(me.polygons)))
bpy.ops.uv.select_all(action='SELECT')
bpy.ops.uv.seams_from_islands(mark_seams=True, mark_sharp=False)
bpy.ops.uv.unwrap(**FLATTEN)
bpy.ops.object.mode_set(mode='OBJECT')


def pack_all():
    """Every island at the same texel density, the face and head scaled up, packed; returns the faces overlapping."""
    bm_ = edit(body)
    pick(bm_, me, {0, 1, 2})
    bpy.ops.uv.select_all(action='SELECT')  # (Blender 5 keeps the UVs' own selection, even in sync mode)
    bpy.ops.uv.average_islands_scale()
    bm_ = bmesh.from_edit_mesh(me)
    uvl = bm_.loops.layers.uv.active
    scale = {2: opts['face'], 1: opts['head']}
    for f in bm_.faces:
        k = scale.get(int(region[f.index]))
        if k:
            for loop in f.loops:
                loop[uvl].uv *= k
    bmesh.update_edit_mesh(me)
    bpy.ops.uv.select_all(action='SELECT')
    pack = dict(rotate=True, scale=True, margin_method='FRACTION', margin=opts['margin'], shape_method='CONCAVE')
    try:
        bpy.ops.uv.pack_islands(**pack)
    except TypeError:
        pack.pop('shape_method')
        bpy.ops.uv.pack_islands(**pack)
    bpy.ops.uv.select_overlap(extend=False)
    return [f.index for f in bmesh.from_edit_mesh(me).faces if f.select]


for attempt in range(3):
    overlapping = pack_all()
    if not overlapping:
        break
    ebm = bmesh.from_edit_mesh(me)
    ebm.faces.ensure_lookup_table()
    for k in overlapping:
        for e in ebm.faces[k].edges:
            e.seam = True
    bmesh.update_edit_mesh(me)
    bpy.ops.uv.unwrap(method='ANGLE_BASED', margin=0.004)  # (the overlapping faces, still selected)
    log(f'{len(overlapping)} faces overlapping after packing: cut out, each an island of its own')
else:
    log(f'WARNING: {len(pack_all())} faces still overlap others in the packed UVs')
bpy.ops.object.mode_set(mode='OBJECT')

# The texture each region got.
uv = read_uvs()
islands = len(set(island_ids(uv)))
area = np.zeros(3)
for p in me.polygons:
    t = uv[list(p.loop_indices)]
    area[region[p.index]] += 0.5 * abs(np.dot(t[:, 0], np.roll(t[:, 1], 1)) - np.dot(t[:, 1], np.roll(t[:, 0], 1)))
log(f'UVs: {islands} islands; texture used: face {area[2]:.1%}, head {area[1]:.1%}, body {area[0]:.1%} '
    f'(all {area.sum():.1%})')

# ------------------------------------------------------------------ colour and shape baked from the full-detail model

# Each texel's ray starts a little outside the reduced body (the cage) and runs back in to the full-detail surface.
# No one cage is right everywhere: a big one starts, in a narrow gap (the cleavage, between the thighs), inside the
# body across the gap, and takes the colour and shape of that side seen from within (a dark cleavage, a dark line
# between the legs); a small one starts behind a thin flap that the reduction sank into the body (a collar ribbon)
# and takes the shirt under it. So colour, normals and where each ray met the surface are baked with several cages,
# and each texel keeps what is seen from outside: of the hits that faced it (the normal's z, in tangent space, well
# above 0) and lie within --reach of the reduced surface, the outermost. Texels none of them got are left out
# (alpha 0): the texture step paints them in.
size = opts['bake']
mat = me.materials[0]
nodes = mat.node_tree.nodes
scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = opts['samples']
bk = scene.render.bake
bk.use_pass_direct = False
bk.use_pass_indirect = False
bk.use_pass_color = True
bk.max_ray_distance = 0.012 * H
bk.margin = 16
bk.target = 'IMAGE_TEXTURES'
bk.normal_space = 'TANGENT'


def emitting(name, output, with_target):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    tree = m.node_tree
    for node in list(tree.nodes):
        tree.nodes.remove(node)
    emit = tree.nodes.new('ShaderNodeEmission')
    tree.links.new(tree.nodes.new('ShaderNodeNewGeometry').outputs[output], emit.inputs['Color'])
    tree.links.new(emit.outputs['Emission'], tree.nodes.new('ShaderNodeOutputMaterial').inputs['Surface'])
    if with_target:
        tree.nodes.active = tree.nodes.new('ShaderNodeTexImage')
    return m


target_mat = emitting('_BakeTarget', 'Position', True)  # (the body's, while baking: the image node bakes go into)
position_mat = emitting('_Position', 'Position', False)
target = target_mat.node_tree.nodes.active
detail_mats = list(detail.data.materials)
me.materials[0] = target_mat


def new_image(name, data):
    img = bpy.data.images.new(name, size, size, alpha=True, float_buffer=data)
    if data:
        img.colorspace_settings.name = 'Non-Color'
    return img


colour_img = new_image('baked_color', False)
normal_img = new_image('baked_normal', False)
normal_img.colorspace_settings.name = 'Non-Color'
position_img = new_image('_hit', True)


def bake(img, kind, from_detail):
    target.image = img
    bk.use_selected_to_active = from_detail
    bpy.ops.object.select_all(action='DESELECT')
    detail.select_set(from_detail)
    body.select_set(True)
    view_layer.objects.active = body
    bpy.ops.object.bake(type=kind)
    out = np.empty(size * size * 4, np.float32)
    img.pixels.foreach_get(out)
    return out.reshape(size, size, 4)


own = bake(position_img, 'EMIT', False)[..., :3]  # the reduced body's own surface under each texel...
nodes_t = target_mat.node_tree
nodes_t.links.new(next(n for n in nodes_t.nodes if n.type == 'NEW_GEOMETRY').outputs['Normal'],
                  next(n for n in nodes_t.nodes if n.type == 'EMISSION').inputs['Color'])
facing = bake(position_img, 'EMIT', False)[..., :3]  # ... and which way it faces
facing /= np.maximum(np.linalg.norm(facing, axis=2, keepdims=True), 1e-6)
reach = opts['reach'] * H
colour = np.zeros((size, size, 4), np.float32)
normal = np.zeros((size, size, 4), np.float32)
normal[..., :3] = (0.5, 0.5, 1.0)
outermost = np.full((size, size), -np.inf, np.float32)
hit_dist = np.full((size, size), np.inf, np.float32)
baked_any = np.zeros((size, size), bool)
which = np.full((size, size), -1, np.int8)
last = None
for k_cage, cage in enumerate([float(c) for c in opts['cages'].split(',')]):
    bk.cage_extrusion = cage * H
    c = bake(colour_img, 'DIFFUSE', True)
    n = bake(normal_img, 'NORMAL', True)
    for k in range(len(detail_mats)):
        detail.data.materials[k] = position_mat
    p = bake(position_img, 'EMIT', True)
    for k, m in enumerate(detail_mats):
        detail.data.materials[k] = m
    baked = c[..., 3] > 0.5  # (only the colour bake leaves the gutters clear: the normal and hit images come back opaque)
    offset = p[..., :3] - own
    out = (offset * facing).sum(2)  # how far outside the reduced surface the ray met the full-detail one
    near = np.linalg.norm(offset, axis=2) < reach
    better = baked & (n[..., 2] * 2 - 1 > opts['facing']) & near & (out > outermost + 1e-5)
    if opts['probe']:  # what the rays met in a box (x0,x1,y0,y1,z0,z1, metres)
        x0, x1, y0, y1, z0, z1 = (float(v) for v in opts['probe'].split(','))
        box = baked & (own[..., 0] > x0) & (own[..., 0] < x1) & (own[..., 1] > y0) & (own[..., 1] < y1) & \
            (own[..., 2] > z0) & (own[..., 2] < z1)
        zz = n[..., 2][box] * 2 - 1
        lum = c[..., :3][box].mean(1)
        log(f'probe cage {k_cage}: {box.sum()} texels; facing {(zz > 0.2).mean():.2f}; near {near[box].mean():.2f}; '
            f'out p10/50/90 {np.percentile(out[box], 10) * 1000:.1f}/{np.percentile(out[box], 50) * 1000:.1f}/'
            f'{np.percentile(out[box], 90) * 1000:.1f} mm; bright {(lum > 0.5).mean():.2f}; '
            f'bright&facing&near {((lum > 0.5) & (zz > 0.2) & near[box]).mean():.2f}; '
            f'dark&facing&near {((lum < 0.2) & (zz > 0.2) & near[box]).mean():.2f}; dark&!facing {((lum < 0.2) & (zz <= 0.2)).mean():.2f}')
    colour[better], normal[better], outermost[better] = c[better], n[better], out[better]
    hit_dist[better] = np.linalg.norm(offset, axis=2)[better]
    which[better] = k_cage
    baked_any |= baked
    last = c
    log(f'cage {cage * H * 1000:.1f} mm: {better.sum() / max(1, baked.sum()):.1%} of the texels took its hit')
    del c, n, p, offset, out, near
chosen = np.isfinite(outermost)
missing = baked_any & ~chosen
log(f'{missing.sum() / max(1, baked_any.sum()):.2%} of the texels met no surface facing them; '
    f'hits {np.percentile(outermost[chosen], 1) * 1000:.1f} .. {np.percentile(outermost[chosen], 99) * 1000:.1f} mm '
    f'outside the body (1%..99%)')
if opts['debug']:  # which cage each texel took: red, green, blue for the first three; white: none
    tint = np.array([(1, 0, 0, 1), (0, 1, 0, 1), (0, 0, 1, 1), (1, 1, 0, 1)], np.float32)
    shown = np.zeros((size, size, 4), np.float32)
    shown[which >= 0] = tint[which[which >= 0]]
    shown[missing] = 1
    debug_img = new_image('_which', False)
    debug_img.pixels.foreach_set(shown.ravel())
    debug_img.filepath_raw = opts['debug']
    debug_img.file_format = 'PNG'
    debug_img.save()
    del shown
colour[missing, :3] = last[missing, :3]  # (seen in the review renders until they're painted in; alpha 0 below)
colour[..., 3] = chosen
normal[..., 3] = chosen

# The full-detail surface nearest a point, and its colour there.
dm = detail.data
bsdf_d = next(n for n in detail_mats[0].node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
node = bsdf_d.inputs['Base Color'].links[0].from_node
while node.type != 'TEX_IMAGE':
    node = next(i for i in node.inputs if i.is_linked).links[0].from_node
src = node.image
sw, sh = src.size
spx = np.empty(sw * sh * 4, np.float32)
src.pixels.foreach_get(spx)
spx = spx.reshape(sh, sw, 4)
loop_uv = np.empty(len(dm.loops) * 2, np.float32)
dm.uv_layers.active.data.foreach_get('uv', loop_uv)
loop_uv = loop_uv.reshape(-1, 2)
loop_vert = np.empty(len(dm.loops), np.int64)
dm.loops.foreach_get('vertex_index', loop_vert)
start = np.empty(len(dm.polygons), np.int64)
dm.polygons.foreach_get('loop_start', start)
vco = np.empty(len(dm.vertices) * 3, np.float32)
dm.vertices.foreach_get('co', vco)
vco = vco.reshape(-1, 3)


def nearest_colour(points):
    """For each point: whether the full-detail surface is within reach, how far, and its colour there."""
    found = np.zeros(len(points), bool)
    dist = np.full(len(points), np.inf)
    where = np.zeros((len(points), 3))
    face = np.zeros(len(points), np.int64)
    for k, p in enumerate(points):
        loc, _, f, d = surface.find_nearest(Vector(p), reach)
        if f is not None:
            found[k], dist[k], where[k], face[k] = True, d, loc, f
    out = np.zeros((len(points), 3), np.float32)
    if found.any():
        corners = start[face[found]][:, None] + np.arange(3)
        a, b, c = (vco[loop_vert[corners[:, i]]].astype(np.float64) for i in range(3))
        v0, v1, v2 = b - a, c - a, where[found] - a
        d00, d01, d11 = (v0 * v0).sum(1), (v0 * v1).sum(1), (v1 * v1).sum(1)
        d20, d21 = (v2 * v0).sum(1), (v2 * v1).sum(1)
        den = np.maximum(d00 * d11 - d01 * d01, 1e-18)
        wb = (d11 * d20 - d01 * d21) / den
        wc = (d00 * d21 - d01 * d20) / den
        wa = 1 - wb - wc
        uvs = (wa[:, None] * loop_uv[corners[:, 0]] + wb[:, None] * loop_uv[corners[:, 1]] +
               wc[:, None] * loop_uv[corners[:, 2]])
        px = np.clip((uvs[:, 0] % 1.0) * sw, 0, sw - 1).astype(int)
        py = np.clip((uvs[:, 1] % 1.0) * sh, 0, sh - 1).astype(int)
        out[found] = spx[py, px, :3]
    return found, dist, out


# Texels whose rays only met surfaces facing away (the crease round an eyeball, the inside of a nostril) take the
# colour of the full-detail surface nearest them (painted in from round about, a pupil smeared over the eye white).
# So do the face's texels whose ray went on past the nearest surface by more than --slack: the reduced face's few
# normals fan the rays out over a bulge (an eyeball), the pupil came out smeared; the nearest point can't miss.
head_box = ((own[..., 2] > head_joint.z - 0.06 * H / 1.7) & (own[..., 2] < brow_top) & (own[..., 1] < head_mid[1]) &
            (np.abs(own[..., 0] - head_mid[0]) < 0.8 * head_half))
look = missing | (chosen & head_box)
ys, xs = np.nonzero(look)
found, dist, near_colour = nearest_colour(own[ys, xs])
use = found & (missing[ys, xs] | (hit_dist[ys, xs] > dist + opts['slack']))
colour[ys[use], xs[use], :3] = near_colour[use]
colour[ys[use], xs[use], 3] = 1
log(f'{(use & missing[ys, xs]).sum()} unmet texels and {(use & ~missing[ys, xs]).sum()} face texels coloured from the '
    f'nearest full-detail surface')
del spx

# The face keeps its colour's detail but not the full-detail shape: under the game's sun the sculpted brows stood out
# as strips with a lit rim and a shadow under them, and the nostrils as rings (Sandro wants smooth faces, no straps).
# Only what's nearly black there keeps its shape: hair across the face, a beard, sunglasses.
lum = colour[..., 0] * 0.2126 + colour[..., 1] * 0.7152 + colour[..., 2] * 0.0722
smooth_face = head_box & (lum > opts['dark'])  # (whichever way it faces: the nostrils face down)
normal[smooth_face, :3] = (0.5, 0.5, 1.0)
log(f'face: {smooth_face.sum()} texels smooth (no shape from the normal map)')

# --nostrils k: the full-detail nose has holes, dark inside and hidden from the side behind the nose's wings; a small
# reduced nose has only a dent, so their inside painted on it read as a ring (a nose piercing) from the side. Within
# the nostrils, what's darker than the skin round them is taken k of the way back to it. (For small noses: the
# attendants'. The nose tip is found as the front-most point near the middle a little above the head joint.)
if opts['nostrils'] > 0:
    sc = H / 1.7
    near_nose = ((gv[:, 2] > head_joint.z) & (gv[:, 2] < head_joint.z + 0.16 * sc) &
                 (np.abs(gv[:, 0] - head_mid[0]) < 0.02 * sc))
    tip = gv[near_nose][np.argmin(gv[near_nose][:, 1])]
    centre = tip + np.array([0.0, 0.008, -0.010]) * sc
    reach_n = 0.015 * sc
    d = np.linalg.norm(own - centre, axis=2)
    lum = colour[..., 0] * 0.2126 + colour[..., 1] * 0.7152 + colour[..., 2] * 0.0722
    round_about = baked_any & (d >= reach_n) & (d < 1.8 * reach_n)
    skin = np.median(colour[round_about & (lum > np.percentile(lum[round_about], 40)), :3], axis=0)
    skin_lum = skin[0] * 0.2126 + skin[1] * 0.7152 + skin[2] * 0.0722
    weight = np.clip((reach_n - d) / (0.35 * reach_n), 0, 1) * baked_any * (lum < skin_lum - 0.02)
    colour[..., :3] += (skin - colour[..., :3]) * (weight * opts['nostrils'])[..., None]
    log(f'nostrils: tip at {tip.round(3)}, {int((weight > 0).sum())} texels taken towards the skin {skin.round(2)}')
colour_img.pixels.foreach_set(colour.ravel())
normal_img.pixels.foreach_set(normal.ravel())
del colour, normal, own, facing, last
me.materials[0] = mat
NORMAL = BAKE.replace('.png', '_normal.png')
for image, path in ((colour_img, BAKE), (normal_img, NORMAL)):
    image.filepath_raw = path
    image.file_format = 'PNG'
    image.save()
log(f'baked {size}x{size} -> {BAKE}, {NORMAL}')
tex = nodes.new('ShaderNodeTexImage')
tex.image = colour_img
ntex = nodes.new('ShaderNodeTexImage')
ntex.image = normal_img

bsdf = next(n for n in nodes if n.type == 'BSDF_PRINCIPLED')
for link in list(bsdf.inputs['Base Color'].links):
    mat.node_tree.links.remove(link)
mat.node_tree.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
normal_map = nodes.new('ShaderNodeNormalMap')
mat.node_tree.links.new(ntex.outputs['Color'], normal_map.inputs['Color'])
mat.node_tree.links.new(normal_map.outputs['Normal'], bsdf.inputs['Normal'])

# ------------------------------------------------------------------ the face, before and after, beside the source

if opts['review']:
    scene.cycles.samples = 48
    scene.cycles.use_denoising = True
    scene.render.image_settings.file_format = 'PNG'
    world = bpy.data.worlds.new('Sky')
    world.use_nodes = True
    world.node_tree.nodes['Background'].inputs[0].default_value = (0.55, 0.68, 0.85, 1)
    world.node_tree.nodes['Background'].inputs[1].default_value = 0.9
    scene.world = world
    sun = bpy.data.objects.new('Sun', bpy.data.lights.new('Sun', 'SUN'))
    sun.data.energy = 3.0
    sun.data.angle = math.radians(8)
    sun.rotation_euler = (math.radians(50), 0, math.radians(-30))
    scene.collection.objects.link(sun)
    cam = bpy.data.objects.new('Camera', bpy.data.cameras.new('Camera'))
    cam.data.lens_unit = 'FOV'
    cam.data.angle = math.radians(28)
    scene.collection.objects.link(cam)
    scene.camera = cam
    centre = Vector((0.0, float(face_mid[1]), eyes - 0.02))
    chest = rig.matrix_world @ rig.data.bones['Chest'].head_local
    quarter = Matrix.Rotation(math.radians(35), 3, 'Z')
    views = [('front', centre, Vector((0, -0.78, 0.04)), 28, 640, 640),
             ('quarter', centre, quarter @ Vector((0, -0.78, 0.04)), 28, 640, 640),
             ('torso', Vector((0, 0, chest.z)), Vector((0, -2.4, 0.1)), 22, 640, 640),
             ('back', Vector((0, 0, top * 0.5)), Vector((0, 6.0, 0.4)), 18, 600, 800)]
    if opts['look']:  # a close look at one spot (x,y,z metres), from the front
        views.append(('look', Vector([float(v) for v in opts['look'].split(',')]), Vector((0, -0.45, 0.05)), 20, 640, 640))
    subjects = {'source': detail, 'before': old, 'after': body}
    for view, target, d, fov, w, h in views:
        scene.render.resolution_x, scene.render.resolution_y = w, h
        cam.data.angle = math.radians(fov)
        cam.location = target + d
        cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        for name, subject in subjects.items():
            for o in subjects.values():
                o.hide_render = o is not subject
            scene.render.filepath = f"{opts['review']}_{name}_{view}.png"
            bpy.ops.render.render(write_still=True)
    log('review renders written')

# ------------------------------------------------------------------ export (the texture is pointed at afterwards)

bpy.data.objects.remove(detail, do_unlink=True)
bpy.data.objects.remove(old, do_unlink=True)
bpy.ops.object.select_all(action='DESELECT')
body.select_set(True)
rig.select_set(True)
# (Tangents written: the normal map was baked against Blender's, which the game then uses as they are.)
export = dict(filepath=OUT, export_format='GLB', use_selection=True, export_skins=True, export_animations=False,
              export_apply=False, export_yup=True, export_tangents=True)
try:
    bpy.ops.export_scene.gltf(export_image_format='NONE', **export)
except TypeError:
    bpy.ops.export_scene.gltf(export_image_format='JPEG', **export)
log('wrote', OUT)
