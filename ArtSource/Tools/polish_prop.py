"""Cleans a raw Meshy prop that keeps a painted texture (unlike stylize_prop.py, which repaints in flat colours).

blender -b --factory-startup -P ArtSource/Tools/polish_prop.py -- <in.glb> <out.glb> --height 0.46 [options]
  --height 0.46        final height in metres
  --turn 180           degrees about the up axis first (Blender -Y = Unity +Z = "forward" in the game)
  --strip-thin 0.035   delete everything thinner than twice this fraction of the height (loose straps, handles,
                       tags) and close the holes; 0 = keep everything
  --flat-back 0.15     after --strip-thin: slice this fraction of the depth off the -Y side (the side worn against
                       a body, where the straps were rooted) and close it with a flat panel
  --tris 3500          triangle budget
  --texture 1024       size of the new texture
  --flatten 0.3        pull the paint's big light/dark smears toward the average (0 = off): Meshy paints fake
                       highlights and shadows in, which look wrong under the game's real light
  --normals 1          also bake a normal map of the detail the low-poly mesh lost
  --umbrella r,g,b     beach umbrella: repaint the canopy's panels alternately this colour and white (the ribs are
                       found from the shape); the pole and cap keep their paint
  --preview <prefix>   render front/side/back/quarter pictures of the result

How: the Meshy mesh (150-250k triangles, texture cut into hundreds of scraps) is only the source. A cleaned,
decimated copy gets one tidy UV layout, and the colour (and normals) are baked onto it from the source, so the
texture survives any triangle count. Result: one mesh "Body" standing on z = 0, centred on its bulk.
"""
import argparse
import math
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

argv = sys.argv[sys.argv.index('--') + 1:]
ap = argparse.ArgumentParser()
ap.add_argument('src'); ap.add_argument('dst')
ap.add_argument('--height', type=float, required=True)
ap.add_argument('--turn', type=float, default=0.0)
ap.add_argument('--strip-thin', type=float, default=0.0)
ap.add_argument('--flat-back', type=float, default=0.0)
ap.add_argument('--tris', type=int, default=3500)
ap.add_argument('--texture', type=int, default=1024)
ap.add_argument('--flatten', type=float, default=0.0)
ap.add_argument('--normals', type=int, default=1)
ap.add_argument('--umbrella', default='')
ap.add_argument('--name', default='Body')
ap.add_argument('--preview', default='')
a = ap.parse_args(argv)


def log(*x):
    print('[polish]', *x)


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=a.src)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
source = bpy.context.view_layer.objects.active
source.parent = None
source.name = source.data.name = 'Source'
source.data.transform(source.matrix_world)
source.matrix_world = Matrix.Identity(4)
for o in list(bpy.context.scene.objects):
    if o != source:
        bpy.data.objects.remove(o)
if a.turn:
    source.data.transform(Matrix.Rotation(math.radians(a.turn), 4, 'Z'))


def coords(o):
    co = np.empty(len(o.data.vertices) * 3)
    o.data.vertices.foreach_get('co', co)
    return co.reshape(-1, 3)


def tri_count(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


co = coords(source)
lo, hi = co.min(axis=0), co.max(axis=0)
source.data.transform(Matrix.Translation((-(lo[0] + hi[0]) / 2, -(lo[1] + hi[1]) / 2, -lo[2])))
source.data.transform(Matrix.Scale(a.height / (hi[2] - lo[2]), 4))

# The working copy that becomes the game mesh.
obj = source.copy()
obj.data = source.data.copy()
obj.name = obj.data.name = a.name
bpy.context.scene.collection.objects.link(obj)
before = tri_count(obj)
scar_points = np.zeros((0, 3))   # where ribbons were torn off (game mesh space)

# ------------------------------------------------------------------ loose ribbons off
if a.strip_thin > 0:
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=a.height * 1e-5)
    bm.normal_update()
    bvh = BVHTree.FromBMesh(bm)
    co = np.array([v.co[:] for v in bm.verts])
    lo, hi = co.min(axis=0), co.max(axis=0)

    # A solid picture of the model in voxels (each column is filled between the surfaces a ray crosses)...
    voxel = a.height / 96.0
    pad = 6
    dims = np.ceil((hi - lo) / voxel).astype(int) + pad * 2
    origin = lo - pad * voxel
    solid = np.zeros(dims, dtype=bool)
    up = Vector((0, 0, 1))
    for ix in range(dims[0]):
        for iy in range(dims[1]):
            x, y = origin[0] + (ix + 0.5) * voxel, origin[1] + (iy + 0.5) * voxel
            z, hits = origin[2], []
            for _ in range(64):
                hit = bvh.ray_cast(Vector((x, y, z)), up)
                if hit[0] is None:
                    break
                hits.append(hit[0].z)
                z = hit[0].z + voxel * 0.01
            for k in range(0, len(hits) - 1, 2):
                z0 = int(round((hits[k] - origin[2]) / voxel))
                z1 = int(round((hits[k + 1] - origin[2]) / voxel))
                solid[ix, iy, z0:max(z1, z0 + 1)] = True

    def grow(grid, steps, value):
        """Dilate (value True) or erode (value False) by `steps` voxels, round-ish (alternating 6/18 neighbours)."""
        g = grid if value else ~grid
        for step in range(steps):
            n = g.copy()
            for axis in range(3):
                n |= np.roll(g, 1, axis) | np.roll(g, -1, axis)
            if step % 2 == 1:
                for a0, a1 in ((0, 1), (0, 2), (1, 2)):
                    for s0 in (1, -1):
                        for s1 in (1, -1):
                            n |= np.roll(np.roll(g, s0, a0), s1, a1)
            g = n
        return g if value else ~g

    # ...worn down until anything thin is gone, then grown back: the bulk without its ribbons.
    radius = max(1, int(round(a.strip_thin * a.height / voxel)))
    worn = grow(solid, radius, False)
    bulk = grow(worn, radius + 2, True)
    clear = ~grow(worn, radius + 5, True)      # well clear of the bulk: certainly a ribbon, not a nibbled edge
    idx = np.clip(np.floor((co - origin) / voxel).astype(int), 0, dims - 1)
    keep = bulk[idx[:, 0], idx[:, 1], idx[:, 2]]
    ribbon = clear[idx[:, 0], idx[:, 1], idx[:, 2]]
    bm.verts.ensure_lookup_table()
    doomed = [bm.verts[i] for i in np.nonzero(~keep)[0]]
    strap = [bm.verts[i] for i in np.nonzero(ribbon)[0]]
    log(f'voxel {voxel * 1000:.1f} mm, worn down by {radius}: removing {len(doomed)} of {len(bm.verts)} vertices ({len(strap)} of them ribbon)')
    gone = KDTree(len(strap))
    for i, v in enumerate(strap):
        gone.insert(v.co, i)
    gone.balance()
    bmesh.ops.delete(bm, geom=doomed, context='VERTS')

    # Whatever came loose with them: only the main body stays.
    seen, parts = set(), []
    for v in bm.verts:
        if v in seen:
            continue
        stack, part = [v], []
        seen.add(v)
        while stack:
            x = stack.pop()
            part.append(x)
            for e in x.link_edges:
                y = e.other_vert(x)
                if y not in seen:
                    seen.add(y)
                    stack.append(y)
        parts.append(part)
    parts.sort(key=len, reverse=True)
    for part in parts[1:]:
        bmesh.ops.delete(bm, geom=part, context='VERTS')
    rim = [e for e in bm.edges if e.is_boundary]
    faces_before = len(bm.faces)
    bmesh.ops.holes_fill(bm, edges=rim, sides=0)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    # The scars: everything close to where a ribbon was (its torn root, the stubs that stayed, where it lay
    # against the body). Ironed flat here, and painted over in plain cloth after the bake.
    near = a.height * 0.05
    scar = [v for v in bm.verts if gone.find(v.co)[2] < near]
    for _ in range(14):
        bmesh.ops.smooth_vert(bm, verts=scar, factor=0.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)
    scar_points = np.array([v.co[:] for v in scar])
    if a.flat_back > 0:
        ys = [v.co.y for v in bm.verts]
        cut = min(ys) + (max(ys) - min(ys)) * a.flat_back
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=Vector((0, cut, 0)), plane_no=Vector((0, -1, 0)), clear_outer=True, dist=a.height * 1e-5)
        panel_rim = [e for e in bm.edges if e.is_boundary]
        bmesh.ops.holes_fill(bm, edges=panel_rim, sides=0)
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        scar_points = np.array([v.co[:] for v in scar if v.is_valid])
        log(f'back sliced flat at y {cut:.3f} ({len(panel_rim)} rim edges)')
    log(f'parts {len(parts)}, kept the biggest; {len(rim)} open edges closed with {len(bm.faces) - faces_before} faces, {len(scar)} scar vertices ironed')
    bm.normal_update()
    bm.to_mesh(obj.data)
    bm.free()
    # Stand the bulk (not the straps) on the origin; the source moves with it so the bake lines up.
    co = coords(obj)
    lo, hi = co.min(axis=0), co.max(axis=0)
    shift = Matrix.Translation((-(lo[0] + hi[0]) / 2, -(lo[1] + hi[1]) / 2, -lo[2]))
    scale = Matrix.Scale(a.height / (hi[2] - lo[2]), 4)
    for o in (obj, source):
        o.data.transform(shift)
        o.data.transform(scale)
    if len(scar_points):
        k = a.height / (hi[2] - lo[2])
        scar_points = (scar_points + np.array([-(lo[0] + hi[0]) / 2, -(lo[1] + hi[1]) / 2, -lo[2]])) * k

# ------------------------------------------------------------------ triangles down, one tidy UV layout
bpy.ops.object.select_all(action='DESELECT')
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
bm = bmesh.new()
bm.from_mesh(obj.data)
bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=a.height * 1e-5)
bm.to_mesh(obj.data)
bm.free()
now = tri_count(obj)
if now > a.tris:
    mod = obj.modifiers.new('Decimate', 'DECIMATE')
    mod.decimate_type = 'COLLAPSE'
    mod.ratio = a.tris / now
    mod.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=mod.name)
for p in obj.data.polygons:
    p.use_smooth = True
while obj.data.uv_layers:
    obj.data.uv_layers.remove(obj.data.uv_layers[0])
obj.data.uv_layers.new(name='UVMap')
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.012)
bpy.ops.object.mode_set(mode='OBJECT')
co = coords(obj)
lo, hi = co.min(axis=0), co.max(axis=0)
log(f'{a.src}: {before} -> {tri_count(obj)} triangles, size x {hi[0] - lo[0]:.3f} y {hi[1] - lo[1]:.3f} z {hi[2] - lo[2]:.3f}')

# ------------------------------------------------------------------ the source's paint
src_mat = source.data.materials[0]
src_nodes, src_links = src_mat.node_tree.nodes, src_mat.node_tree.links
src_bsdf = next(n for n in src_nodes if n.type == 'BSDF_PRINCIPLED')
src_tex = src_bsdf.inputs['Base Color'].links[0].from_node

if a.umbrella:
    colour = [float(c) for c in a.umbrella.split(',')]
    sco = coords(source)
    r = np.hypot(sco[:, 0], sco[:, 1])
    rmax = r.max()
    ring = (r > rmax * 0.45) & (r < rmax * 0.8) & (sco[:, 2] > a.height * 0.6)
    theta = np.arctan2(sco[ring, 1], sco[ring, 0])
    z = sco[ring, 2] - sco[ring, 2].mean()
    # The ribs are the high lines of the canopy: the panel count is the strongest wave round the ring.
    power = {n: abs(np.sum(z * np.exp(-1j * n * theta))) for n in range(6, 17)}
    panels = max(power, key=power.get)
    phase = np.angle(np.sum(z * np.exp(-1j * panels * theta)))   # z ~ cos(n*theta + phase): ribs where that is 0
    rib = -phase / panels
    log(f'umbrella: {panels} panels (wave strengths {[round(power[n] / power[panels], 2) for n in sorted(power)]}), first rib at {math.degrees(rib):.1f} deg')

    def node(kind, **props):
        n = src_nodes.new(kind)
        for k, v in props.items():
            setattr(n, k, v)
        return n

    def math_node(op, x, y=None):
        n = node('ShaderNodeMath', operation=op)
        for i, v in enumerate((x, y)):
            if v is None:
                continue
            if isinstance(v, (int, float)):
                n.inputs[i].default_value = v
            else:
                src_links.new(v, n.inputs[i])
        return n.outputs[0]

    def socket(n, identifier, outputs=False):
        return next(s for s in (n.outputs if outputs else n.inputs) if s.identifier == identifier)

    pos = node('ShaderNodeSeparateXYZ')
    src_links.new(node('ShaderNodeNewGeometry').outputs['Position'], pos.inputs[0])
    angle = math_node('ARCTAN2', pos.outputs['Y'], pos.outputs['X'])
    turn = math_node('DIVIDE', math_node('ADD', angle, 8 * math.pi - rib), 2 * math.pi / panels)
    stripe = math_node('MODULO', math_node('FLOOR', turn), 2.0)
    radius = math_node('SQRT', math_node('ADD', math_node('MULTIPLY', pos.outputs['X'], pos.outputs['X']), math_node('MULTIPLY', pos.outputs['Y'], pos.outputs['Y'])))
    rgb = node('ShaderNodeSeparateColor')
    src_links.new(src_tex.outputs['Color'], rgb.inputs[0])
    redness = math_node('SUBTRACT', rgb.outputs[0], math_node('MAXIMUM', rgb.outputs[1], rgb.outputs[2]))
    canopy = math_node('MULTIPLY', math_node('GREATER_THAN', redness, 0.07), math_node('GREATER_THAN', radius, rmax * 0.06))
    paint = node('ShaderNodeMix', data_type='RGBA')
    src_links.new(stripe, socket(paint, 'Factor_Float'))
    socket(paint, 'A_Color').default_value = (*colour, 1.0)
    socket(paint, 'B_Color').default_value = (0.96, 0.94, 0.88, 1.0)
    final = node('ShaderNodeMix', data_type='RGBA')
    src_links.new(canopy, socket(final, 'Factor_Float'))
    src_links.new(src_tex.outputs['Color'], socket(final, 'A_Color'))
    src_links.new(socket(paint, 'Result_Color', True), socket(final, 'B_Color'))
    src_links.new(socket(final, 'Result_Color', True), src_bsdf.inputs['Base Color'])

# ------------------------------------------------------------------ bake onto the new mesh
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 4
mat = bpy.data.materials.new(a.name)
mat.use_nodes = True
nodes, links = mat.node_tree.nodes, mat.node_tree.links
bsdf = next(n for n in nodes if n.type == 'BSDF_PRINCIPLED')
bsdf.inputs['Metallic'].default_value = 0.0
bsdf.inputs['Roughness'].default_value = 0.85
obj.data.materials.clear()
obj.data.materials.append(mat)
colour_image = bpy.data.images.new(a.name + '_Color', a.texture, a.texture, alpha=False)
colour_node = nodes.new('ShaderNodeTexImage')
colour_node.image = colour_image
normal_image = normal_node = None
if a.normals:
    normal_image = bpy.data.images.new(a.name + '_Normal', a.texture, a.texture, alpha=False)
    normal_image.colorspace_settings.name = 'Non-Color'
    normal_node = nodes.new('ShaderNodeTexImage')
    normal_node.image = normal_image

bpy.ops.object.select_all(action='DESELECT')
source.select_set(True)
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
reach = a.height * 0.03
nodes.active = colour_node
bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'}, use_selected_to_active=True, cage_extrusion=reach, max_ray_distance=reach * 2, margin=8)
if a.normals:
    nodes.active = normal_node
    bpy.ops.object.bake(type='NORMAL', use_selected_to_active=True, cage_extrusion=reach, max_ray_distance=reach * 2, margin=8)

# Texels the bake found nothing for come out black: give them the paint's average instead.
w = h = a.texture
px = np.empty(w * h * 4, dtype=np.float32)
colour_image.pixels.foreach_get(px)
px = px.reshape(h, w, 4)
missed = px[..., :3].max(axis=2) < 0.004
cloth = np.median(px[~missed][:, :3], axis=0) if not missed.all() else np.array([0.5, 0.5, 0.5])
px[missed, :3] = cloth

# Scars: the bake put the ribbon's colour (and its relief) there. Plain cloth and a flat normal instead.
scar_mask = np.zeros((h, w), dtype=bool)
if len(scar_points):
    tree = KDTree(len(scar_points))
    for i, pnt in enumerate(scar_points):
        tree.insert(pnt, i)
    tree.balance()
    uv = obj.data.uv_layers.active.data
    near = a.height * 0.02
    painted = 0
    for poly in obj.data.polygons:
        if tree.find(poly.center)[2] > near:
            continue
        painted += 1
        pts = np.array([[uv[li].uv.x * w, uv[li].uv.y * h] for li in poly.loop_indices])
        x0, y0 = np.floor(pts.min(axis=0)).astype(int) - 2
        x1, y1 = np.ceil(pts.max(axis=0)).astype(int) + 2
        x0, y0, x1, y1 = max(x0, 0), max(y0, 0), min(x1, w - 1), min(y1, h - 1)
        gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        (ax, ay), (bx, by), (cx, cy) = pts[0], pts[1], pts[2]
        det = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        if abs(det) < 1e-9:
            continue
        l0 = ((by - cy) * (gx - cx) + (cx - bx) * (gy - cy)) / det
        l1 = ((cy - ay) * (gx - cx) + (ax - cx) * (gy - cy)) / det
        l2 = 1 - l0 - l1
        slack = 1.5 / max(1.0, math.sqrt(abs(det)))   # a texel and a half past the edges
        scar_mask[y0:y1 + 1, x0:x1 + 1] |= (l0 > -slack) & (l1 > -slack) & (l2 > -slack)
    # The colour of the cloth round the scars, not of the whole bag.
    ring = scar_mask.copy()
    for _ in range(24):
        ring[1:, :] |= ring[:-1, :]; ring[:-1, :] |= ring[1:, :]; ring[:, 1:] |= ring[:, :-1]; ring[:, :-1] |= ring[:, 1:]
    ring &= ~scar_mask & ~missed
    patch = np.median(px[ring][:, :3], axis=0) if ring.any() else cloth
    px[scar_mask, :3] = patch
    log(f'scars: {painted} faces repainted in cloth colour {[round(float(c), 2) for c in patch]}')
colour_image.pixels.foreach_set(px.ravel())
if normal_image is not None and scar_mask.any():
    npx = np.empty(w * h * 4, dtype=np.float32)
    normal_image.pixels.foreach_get(npx)
    npx = npx.reshape(h, w, 4)
    npx[scar_mask, :3] = (0.5, 0.5, 1.0)
    normal_image.pixels.foreach_set(npx.ravel())
log(f'bake: {100.0 * missed.mean():.1f}% of the texture empty or missed')

if a.flatten > 0:
    rgb = px[..., :3]
    lum = rgb @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
    k = max(3, w // 20) | 1
    blur = lum.copy()
    for _ in range(3):
        for axis in (0, 1):
            pad = [(k // 2 + 1, k // 2) if ax == axis else (0, 0) for ax in (0, 1)]
            c = np.cumsum(np.pad(blur, pad, mode='edge'), axis=axis)
            blur = (np.take(c, range(k, c.shape[axis]), axis=axis) - np.take(c, range(0, c.shape[axis] - k), axis=axis)) / k
    # The local average moves toward the overall one: big smears go, small painted detail (stitches) stays.
    target = blur + (float(blur.mean()) - blur) * a.flatten
    gain = np.clip(target / np.maximum(blur, 1e-4), 0.6, 1.8)
    px[..., :3] = np.clip(rgb * gain[..., None], 0.0, 1.0)
    colour_image.pixels.foreach_set(px.ravel())
    log(f'paint evened out by {a.flatten}')

links.new(colour_node.outputs['Color'], bsdf.inputs['Base Color'])
if a.normals:
    bump = nodes.new('ShaderNodeNormalMap')
    links.new(normal_node.outputs['Color'], bump.inputs['Color'])
    links.new(bump.outputs['Normal'], bsdf.inputs['Normal'])
colour_image.pack()
if normal_image is not None:
    normal_image.pack()

bpy.data.objects.remove(source)
bpy.ops.object.select_all(action='DESELECT')
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
bpy.ops.export_scene.gltf(filepath=a.dst, export_format='GLB', export_image_format='AUTO', use_selection=True, export_apply=True)

if a.preview:
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 600
    scene.render.resolution_y = 700
    scene.world = bpy.data.worlds.new('w')
    scene.world.use_nodes = True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value = (0.62, 0.72, 0.85, 1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value = 0.9
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN'))
    scene.collection.objects.link(sun)
    sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(50), 0, math.radians(-30))
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam'))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = max(hi - lo) * 1.2
    centre = Vector(((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2))
    for name, d in (('front', (0, -1, 0)), ('side', (1, 0, 0)), ('back', (0, 1, 0)), ('quarter', (0.7, 0.7, 0.4)), ('under', (0.6, -0.6, -0.5))):
        v = Vector(d).normalized()
        cam.location = centre + v * 10
        cam.rotation_euler = (-v).to_track_quat('-Z', 'Y').to_euler()
        sun.rotation_euler = (Vector((0.3, -0.2, -1)) + (-v) * 0.9).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = f'{a.preview}_{name}.png'
        bpy.ops.render.render(write_still=True)
