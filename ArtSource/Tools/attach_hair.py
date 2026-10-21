"""Puts a Tripo hairstyle on a rigged game body (the new game's shared bases and their variants), as part of the body.

blender -b --factory-startup -P ArtSource/Tools/attach_hair.py -- <body.glb> <hair.glb> <out.glb> [options]
  --tint r,g,b      recolour the hair (sRGB 0-255), keeping its light and shade: one hairstyle, many girls
  --hint r,g,b      the hair's colour (sRGB 0-255): tells the hair from the head Tripo modelled it on
  --turn 0          extra degrees about the vertical (the facing is found from the model's face, or a wig's fringe)
  --width 1.1       extra scale on top of the fit (1 = the generated head as wide as the body's: a little more covers the hairline)
  --lift -0.03      metres to move the hair up after the fit (down a little: the hairline onto the forehead)
  --preview <png>   also render front / side / back of the result

Tripo can't make hair on its own: it models a head wearing it. That head is the guide: the hair is split from its skin
by colour, the whole thing scaled and moved so the generated head sits on the body's head (as wide, the same top), the
generated head thrown away, any hair that dips inside the body's head pushed out, every hair vertex weighted to the
Head bone. The hair keeps its own texture: the body's picture gets a strip under it (the body's part keeps its full
resolution and its texels, so the game's face bake is unchanged) and the hair's picture goes in that strip.
The body mesh itself is not touched: the face (eyes, lids, mouth) stays the base's.
"""
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index('--') + 1:]
BODY, HAIR, OUT = argv[0], argv[1], argv[2]
opts = {'tint': '', 'hint': '60,40,30', 'turn': 0.0, 'width': 1.1, 'lift': -0.03, 'preview': '', 'facex': 0.42, 'fill': 0}
i = 3
while i < len(argv):
    k = argv[i].lstrip('-')
    opts[k] = type(opts[k])(argv[i + 1]) if not isinstance(opts[k], str) else argv[i + 1]
    i += 2
HINT = np.array([float(c) for c in opts['hint'].split(',')]) / 255.0


def log(*a):
    print('[hair]', *a)


bpy.ops.wm.read_factory_settings(use_empty=True)

# ------------------------------------------------------------------ the body
bpy.ops.import_scene.gltf(filepath=BODY)
rig = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
body = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
dg = bpy.context.evaluated_depsgraph_get()
bw = body.matrix_world
head_group = body.vertex_groups.get('Head')
assert head_group is not None, 'no Head vertex group on the body'
head_pts = []
for v in body.data.vertices:
    w = next((g.weight for g in v.groups if g.group == head_group.index), 0.0)
    if w > 0.5:
        head_pts.append(bw @ v.co)
head_pts = np.array([tuple(p) for p in head_pts])
top = head_pts[:, 2].max()
chin = head_pts[:, 2].min()
hh = top - chin
band = head_pts[(head_pts[:, 2] > top - 0.55 * hh) & (head_pts[:, 2] < top - 0.25 * hh)]
body_w = band[:, 0].max() - band[:, 0].min()
body_cx = (band[:, 0].max() + band[:, 0].min()) / 2
body_cy = (band[:, 1].max() + band[:, 1].min()) / 2
log(f'body head: top {top:.3f}, height {hh:.3f}, width {body_w:.3f}, centre ({body_cx:.3f}, {body_cy:.3f})')

# The body's head as a surface, to keep the hair outside it.
bm_body = bmesh.new()
bm_body.from_object(body, dg)
bm_body.transform(bw)
bvh = BVHTree.FromBMesh(bm_body)

# ------------------------------------------------------------------ the hair (and the head it came on)
before = set(bpy.context.scene.objects)
bpy.ops.import_scene.gltf(filepath=HAIR)
hair_objs = [o for o in bpy.context.scene.objects if o not in before and o.type == 'MESH']
assert len(hair_objs) == 1, f'expected one mesh in the hair file, got {len(hair_objs)}'
hair = hair_objs[0]
hair.parent = None
hair.matrix_world = Matrix.Rotation(np.radians(opts['turn']), 4, 'Z') @ hair.matrix_world  # (then turned by its face, below)
bpy.context.view_layer.update()
bpy.ops.object.select_all(action='DESELECT')
hair.select_set(True)
bpy.context.view_layer.objects.active = hair
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

mat = hair.data.materials[0]
img = next(n.image for n in mat.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image is not None)
iw, ih = img.size
px = np.array(img.pixels[:], dtype=np.float32).reshape(ih, iw, 4)[:, :, :3]

me = hair.data
uv = me.uv_layers.active.data
face_col = np.zeros((len(me.polygons), 3))
for f in me.polygons:
    us = [uv[li].uv for li in f.loop_indices]
    u = sum(x[0] for x in us) / len(us)
    v = sum(x[1] for x in us) / len(us)
    face_col[f.index] = px[min(ih - 1, max(0, int(v * ih))), min(iw - 1, max(0, int(u * iw)))]

def lab(c):
    c = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    m = np.array([[0.4124, 0.3576, 0.1805], [0.2126, 0.7152, 0.0722], [0.0193, 0.1192, 0.9505]])
    xyz = c @ m.T / np.array([0.9505, 1.0, 1.089])
    f = np.where(xyz > 0.008856, np.cbrt(xyz), 7.787 * xyz + 16 / 116)
    return np.stack([116 * f[..., 1] - 16, 500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2])], axis=-1)


def split(face_lab, c0, c1):
    """Two colour groups, started at c0 (hair) and c1 (skin): True for the hair's faces."""
    for _ in range(12):
        a = np.linalg.norm(face_lab - c0, axis=1) < np.linalg.norm(face_lab - c1, axis=1)
        if a.any(): c0 = face_lab[a].mean(axis=0)
        if (~a).any(): c1 = face_lab[~a].mean(axis=0)
    return np.linalg.norm(face_lab - c0, axis=1) < np.linalg.norm(face_lab - c1, axis=1)


# Colours told apart in Lab with the red-green axis weighted up (skin is redder than blonde or ginger hair).
W = np.array([0.5, 2.0, 1.0])
face_lab = lab(face_col) * W
hint_lab = lab(HINT[None])[0] * W
areas = np.array([f.area for f in me.polygons])
centres = np.array([tuple(f.center) for f in me.polygons])
rough = split(face_lab, hint_lab, face_lab[np.argmax(np.linalg.norm(face_lab - hint_lab, axis=1))])
skin_share = areas[~rough].sum() / areas.sum()
wig = skin_share < 0.04 or skin_share > 0.96  # Tripo made the hair alone, no head in it (one colour all over)

# Which way it faces (Tripo isn't consistent): the face is where the skin is; a bare wig faces where it hangs
# shortest (the fringe over the forehead, the back down to the neck). Turned to look along -y, as the body does.
verts = np.array([tuple(v.co) for v in me.vertices])
mid = verts.mean(axis=0)
if not wig:
    # The face's skin (above the middle of the skin: not the neck at the back) points the way the head looks.
    normals = np.array([tuple(f.normal) for f in me.polygons])
    sk = ~rough
    upper = sk & (centres[:, 2] > np.median(centres[sk, 2]))
    d = (normals[upper] * areas[upper, None]).sum(axis=0)
    facing = np.arctan2(d[1], d[0])
else:
    # A wig faces where it hangs shortest (the fringe over the forehead; the back goes down to the neck).
    rel = verts - mid
    ang = np.arctan2(rel[:, 1], rel[:, 0])
    rad = np.hypot(rel[:, 0], rel[:, 1])
    best, facing = -1e9, 0.0
    for k in range(16):
        a0 = -np.pi + k * np.pi / 8
        sel = (ang >= a0) & (ang < a0 + np.pi / 8) & (rad > 0.5 * rad.max())
        if sel.sum() > 20 and verts[sel, 2].min() > best:
            best, facing = verts[sel, 2].min(), a0 + np.pi / 16
turn = -np.pi / 2 - facing
turn = round(turn / (np.pi / 2)) * (np.pi / 2)  # (Tripo's models stand square to the axes: a quarter turn at a time)
R = Matrix.Rotation(turn, 4, 'Z')
for v in me.vertices:
    v.co = R @ (v.co - Vector(tuple(mid))) + Vector(tuple(mid))
verts = np.array([tuple(v.co) for v in me.vertices])
centres = np.array([tuple(f.center) for f in me.polygons])
log(f'{"a bare wig" if wig else "hair on a head"} ({skin_share * 100:.0f}% skin), turned {np.degrees(turn):.0f} degrees')

all_top = verts[:, 2].max()
if not wig:
    # Skin from the tip of the nose (the front-most face at mid height), then the split again from there.
    zmid = (centres[:, 2].min() + centres[:, 2].max()) / 2
    near_mid = np.abs(centres[:, 2] - zmid) < 0.15 * (centres[:, 2].max() - centres[:, 2].min())
    nose = np.argmin(np.where(near_mid & ~rough, centres[:, 1], 1e9))
    is_hair = split(face_lab, hint_lab, face_lab[nose].copy())
    # (Whichever split leaves a believable share of hair: the rough one by the hint colour, unless it's off.)
    share = lambda m: areas[m].sum() / areas.sum()
    if not (0.6 <= share(is_hair) <= 0.97) and 0.6 <= share(rough) <= 0.97:
        is_hair = rough
    skin_v = np.unique(np.concatenate([np.array(me.polygons[k].vertices) for k in np.where(~is_hair)[0]]))
    skin = verts[skin_v]
    # Its head: the skin from the top of the hair down to the chin (where the skin narrows most under its widest).
    zs = np.linspace(skin[:, 2].min(), skin[:, 2].max(), 60)
    widths = []
    for z in zs:
        sl = skin[np.abs(skin[:, 2] - z) < (zs[1] - zs[0])]
        widths.append(sl[:, 0].max() - sl[:, 0].min() if len(sl) > 3 else 0)
    widths = np.array(widths)
    wide_k = int(np.argmax(widths))
    neck_k = int(np.argmin(np.where(np.arange(60) < wide_k, widths + 1e3 * (widths == 0), 1e9)))
    gen_chin = zs[neck_k]
    skin_top = skin[:, 2].max()  # (measured from the skin: a big bun on top would put the band on the forehead)
    gen_hh = skin_top - gen_chin
    gband = skin[(skin[:, 2] > skin_top - 0.45 * gen_hh) & (skin[:, 2] < skin_top - 0.15 * gen_hh)]
    gen_w = gband[:, 0].max() - gband[:, 0].min()
    gen_cx = (gband[:, 0].max() + gband[:, 0].min()) / 2
    gen_cy = (gband[:, 1].max() + gband[:, 1].min()) / 2
    hair_on_top = all_top - skin[:, 2].max()
    s = body_w / gen_w * opts['width']
    log(f'generated head: top {all_top:.3f}, chin {gen_chin:.3f}, width {gen_w:.3f}')
else:
    # A wig: its own width round the temples is the head's plus the hair's thickness either side.
    is_hair = np.ones(len(me.polygons), dtype=bool)
    hh_w = all_top - verts[:, 2].min()
    band = verts[(verts[:, 2] > all_top - 0.4 * hh_w) & (verts[:, 2] < all_top - 0.2 * hh_w)]
    gen_w = band[:, 0].max() - band[:, 0].min()
    gen_cx = (band[:, 0].max() + band[:, 0].min()) / 2
    gen_cy = (band[:, 1].max() + band[:, 1].min()) / 2
    s = body_w * 1.22 / gen_w * opts['width'] / 1.1
    hair_on_top = 0.03 * gen_w
    log(f'wig: top {all_top:.3f}, width {gen_w:.3f}')
log(f'{is_hair.sum()} hair faces, {(~is_hair).sum()} head faces')

offset = Vector((body_cx - gen_cx * s, body_cy - gen_cy * s, top + hair_on_top * s - all_top * s + opts['lift']))
for v in me.vertices:
    v.co = Vector((v.co.x * s, v.co.y * s, v.co.z * s)) + offset

# Drop the generated head (and its neck and shoulders): only hair stays.
bm = bmesh.new()
bm.from_mesh(me)
bm.faces.ensure_lookup_table()
bmesh.ops.delete(bm, geom=[bm.faces[k] for k in np.where(~is_hair)[0]], context='FACES')
loose = [v for v in bm.verts if not v.link_faces]
bmesh.ops.delete(bm, geom=loose, context='VERTS')
# Below the chin, hair close to the body is the generated bust's shoulders and neck in shadow (or hair lying on the
# back, which would only go through it as the body bends): dropped.
bm.faces.ensure_lookup_table()
flat = []
for f in bm.faces:
    c = f.calc_center_median()
    if c.z > chin + 0.03:
        continue
    loc, nrm, _, dist = bvh.find_nearest(c)
    if loc is not None and dist < 0.06:
        flat.append(f)
bmesh.ops.delete(bm, geom=flat, context='FACES')
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
log(f'{len(flat)} faces lying on the neck and shoulders dropped')
# On the face below the brows: the generated head's own eyebrows and eyes (dark like the hair), or hair hanging into
# the eyes. Dropped: the face stays the base's.
eyes_z = top - 0.48 * hh
bm.faces.ensure_lookup_table()
on_face = []
for f in bm.faces:
    c = f.calc_center_median()
    if c.z > eyes_z + 0.035 or c.z < chin or c.y > body_cy - 0.02:
        continue  # (above the brows, below the chin, or not in front)
    if abs(c.x - body_cx) > float(opts['facex']) * body_w:
        continue  # (out at the sides: hair framing the face; --facex 0.3 keeps strands over the cheeks, e.g. a bob)
    loc, nrm, _, dist = bvh.find_nearest(c)
    if loc is not None and dist < 0.09:
        on_face.append(f)
bmesh.ops.delete(bm, geom=on_face, context='FACES')
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
log(f'{len(on_face)} faces on the face dropped')
# (Tripo splits the mesh along every texture seam: welded, the pieces are the real ones. UVs live on the loops and stay.)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
# Holes where the generated head's ear or skin poked through the hair (a bob over the ears): small open loops are
# closed (the face opening and the hair's lower rim are long loops and stay open). New faces take the UV of the
# nearest old corner, so they get the hair's colour.
if int(opts['fill']) > 0:
    uv = bm.loops.layers.uv.active
    old = set(bm.faces)
    bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=int(opts['fill']))
    new = [f for f in bm.faces if f not in old]
    if uv is not None:
        for f in new:
            for l in f.loops:
                src = [m for m in l.vert.link_loops if m.face in old]
                if src:
                    l[uv].uv = src[0][uv].uv.copy()
    log(f'{len(new)} faces fill holes in the hair')
# Small loose pieces are not hair: the generated face's eyebrows and lashes (dark like the hair), bits of shadow on its
# neck. Only pieces with at least 3% of the hair's faces stay.
bm.faces.ensure_lookup_table()
seen, pieces = set(), []
for f in bm.faces:
    if f.index in seen:
        continue
    stack, piece = [f], []
    seen.add(f.index)
    while stack:
        g = stack.pop()
        piece.append(g)
        for e in g.edges:
            for h in e.link_faces:
                if h.index not in seen:
                    seen.add(h.index)
                    stack.append(h)
    pieces.append(piece)
total = len(bm.faces)
small_pieces = [f for p in pieces if len(p) < 0.03 * total for f in p]
bmesh.ops.delete(bm, geom=small_pieces, context='FACES')
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
log(f'{len(pieces)} pieces, {len(small_pieces)} faces in small ones dropped')
# Out of the body's head: any hair vertex inside it goes to its surface, 4 mm out.
pushed = 0
for v in bm.verts:
    loc, nrm, _, dist = bvh.find_nearest(v.co)
    if loc is None or dist > 0.08:
        continue
    if (v.co - loc).dot(nrm) < 0.004:
        v.co = loc + nrm * 0.004
        pushed += 1
bm.to_mesh(me)
bm.free()
log(f'scale {s:.3f}, {len(me.vertices)} hair vertices, {pushed} pushed out of the head')

# ------------------------------------------------------------------ one texture: the body's picture with a strip under it
bmat = body.data.materials[0]
bimg = next(n.image for n in bmat.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image is not None)
bw_, bh_ = bimg.size
strip = bw_ // 4  # the hair picture: a square a quarter of the width, under the body's picture
nh = bh_ + strip
canvas = np.zeros((nh, bw_, 4), dtype=np.float32)
canvas[:, :, 3] = 1
canvas[strip:, :, :] = np.array(bimg.pixels[:], dtype=np.float32).reshape(bh_, bw_, 4)
small = bpy.data.images.new('hair_small', iw, ih)
small.pixels.foreach_set(img.pixels[:])
small.scale(strip, strip)
hair_px = np.array(small.pixels[:], dtype=np.float32).reshape(strip, strip, 4)
if opts['tint']:
    # The hair's brightness pattern times the new colour (in linear light), its average landing on the colour.
    t = np.array([float(c) for c in opts['tint'].split(',')]) / 255.0
    t_lin = np.where(t <= 0.04045, t / 12.92, ((t + 0.055) / 1.055) ** 2.4)
    rgb_lin = np.where(hair_px[:, :, :3] <= 0.04045, hair_px[:, :, :3] / 12.92, ((hair_px[:, :, :3] + 0.055) / 1.055) ** 2.4)
    y = rgb_lin @ np.array([0.2126, 0.7152, 0.0722])
    y_ref = max(1e-4, float(np.median(y)))
    new_lin = np.clip(t_lin[None, None, :] * (y / y_ref)[:, :, None] ** 0.8, 0, 1)
    hair_px[:, :, :3] = np.where(new_lin <= 0.0031308, new_lin * 12.92, 1.055 * new_lin ** (1 / 2.4) - 0.055)
canvas[:strip, :strip, :] = hair_px
new_img = bpy.data.images.new(bimg.name + '_hair', bw_, nh)
new_img.pixels.foreach_set(canvas.ravel())
for n in bmat.node_tree.nodes:
    if n.type == 'TEX_IMAGE' and n.image is bimg:
        n.image = new_img
# Body UVs: into the top part (v measured from the bottom in Blender); hair UVs: into the square.
for l in body.data.uv_layers.active.data:
    l.uv = (l.uv[0], (l.uv[1] * bh_ + strip) / nh)
for l in hair.data.uv_layers.active.data:
    l.uv = (l.uv[0] * strip / bw_, l.uv[1] * strip / nh)
hair.data.materials.clear()
hair.data.materials.append(bmat)

# ------------------------------------------------------------------ weights and join
for g in list(hair.vertex_groups):
    hair.vertex_groups.remove(g)
hg = hair.vertex_groups.new(name='Head')
hg.add(list(range(len(hair.data.vertices))), 1.0, 'REPLACE')
bpy.ops.object.select_all(action='DESELECT')
hair.select_set(True)
body.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.object.join()
body = bpy.context.view_layer.objects.active
body.data.materials.clear()
body.data.materials.append(bmat)
log(f'joined: {len(body.data.vertices)} vertices')

bpy.ops.object.select_all(action='DESELECT')
body.select_set(True)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', use_selection=True, export_skins=True,
                          export_animations=False, export_apply=False, export_yup=True,
                          export_image_format='JPEG', export_jpeg_quality=90)
log(f'wrote {OUT}')

# ------------------------------------------------------------------ preview
if opts['preview']:
    scene = bpy.context.scene
    engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
    scene.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in engines else 'BLENDER_EEVEE'
    scene.render.resolution_x, scene.render.resolution_y = 500, 640
    world = bpy.data.worlds.new('w')
    world.use_nodes = True
    world.node_tree.nodes['Background'].inputs[0].default_value = (0.75, 0.75, 0.78, 1)
    world.node_tree.nodes['Background'].inputs[1].default_value = 1.3
    scene.world = world
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN'))
    sun.data.energy = 3
    sun.rotation_euler = (0.9, 0.1, 0.5)
    scene.collection.objects.link(sun)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam'))
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = hh * 3.2
    scene.collection.objects.link(cam)
    scene.camera = cam
    centre = Vector((body_cx, body_cy, top - hh * 0.9))
    for name, d in (('front', Vector((0, -1, 0))), ('side', Vector((1, 0, 0))), ('back', Vector((0, 1, 0))), ('three', Vector((0.7, -0.7, 0.15)))):
        cam.location = centre + d.normalized() * 4
        cam.rotation_euler = (centre - cam.location).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = f"{opts['preview']}_{name}.png"
        bpy.ops.render.render(write_still=True)
    log(f'preview {opts["preview"]}_*.png')
