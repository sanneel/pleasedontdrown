"""Tripo Smart Mesh characters -> a smooth, denser mesh for prepare_character.py, texture untouched.

Smart Mesh exports a low-poly quad mesh (about 5,800 quads, triangulated in the GLB). Up close the faceted jaw and
the brows caught the light: the brows are sculpted as raised strips (and painted on them too), so lit they read as
stickers with a pale rim. This welds the UV-split vertices back together, turns the triangles back into the quads
they came from, subdivides once (Catmull-Clark, UVs kept on their islands). More vertices also make
the CPR bust jiggle bend smoothly instead of in a few big facets.

--brow-flatten N (off by default): pressed flat, the brows lost their rim but their paint, which lies on the strip's
sides too, was squashed into thin broken lines and the ring round them pulled the hairline down. Kept for trying.
Tried and dropped: relaxing every forehead vertex up to the hairline (tore the hairline), smoothed lighting normals
over the brows (the raised strip still showed).

Blender -b --factory-startup -P ArtSource/Tools/tripo_smooth_character.py -- <in.glb> <out.glb> [--levels 1] [--brow-flatten 40] [--brows-from <glb>]

--brows-from: find the brows in that model's texture instead (the same Smart Mesh, other paint): girls that share
a mesh must stay vertex for vertex the same (MeshyCharacters.SameMeshAs), so all of them flatten the red girl's brows.
"""
import sys

import bmesh
import bpy
import numpy as np

argv = sys.argv[sys.argv.index('--') + 1:]
SRC, DST = argv[0], argv[1]
opts = {'levels': 1, 'brow-flatten': 0, 'brows-from': ''}
i = 2
while i < len(argv):
    key = argv[i].lstrip('-')
    opts[key] = argv[i + 1] if key == 'brows-from' else int(argv[i + 1])
    i += 2

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC, merge_vertices=True)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
assert len(meshes) == 1, f'expected one mesh, found {len(meshes)}'
body = meshes[0]
bpy.context.view_layer.objects.active = body
body.select_set(True)
tris_before = sum(len(p.vertices) - 2 for p in body.data.polygons)

bm = bmesh.new()
bm.from_mesh(body.data)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
bmesh.ops.join_triangles(bm, faces=bm.faces, cmp_seam=False, cmp_sharp=False, cmp_uvs=True, cmp_vcols=False,
                         cmp_materials=True, angle_face_threshold=0.7, angle_shape_threshold=0.9)
bm.verts.ensure_lookup_table()

# ------------------------------------------------------------------ the brows, pressed flat
image = next(n.image for m in body.data.materials if m and m.node_tree for n in m.node_tree.nodes if n.type == 'TEX_IMAGE')
if opts['brows-from']:
    before = set(bpy.data.images)
    bpy.ops.import_scene.gltf(filepath=opts['brows-from'])
    image = next(img for img in bpy.data.images if img not in before and img.size[0] > 0)
    for o in [o for o in bpy.context.scene.objects if o != body]:
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.context.view_layer.objects.active = body
    body.select_set(True)
w, h = image.size
pixels = np.empty(w * h * 4, np.float32)
image.pixels.foreach_get(pixels)
pixels = pixels.reshape(h, w, 4)[..., :3]
uv_layer = bm.loops.layers.uv.active


def colour(v):
    u, t = v.link_loops[0][uv_layer].uv
    return pixels[min(h - 1, max(0, int(t * h))), min(w - 1, max(0, int(u * w)))]


mw = body.matrix_world
co = {v: mw @ v.co for v in bm.verts}
lo = min(p.z for p in co.values())
H = max(p.z for p in co.values()) - lo
front_y = min(p.y for p in co.values() if p.z > lo + 0.84 * H)
# Over the eyes and under the hairline, on the front of the face.
band = [v for v, p in co.items() if 0.922 < (p.z - lo) / H < 0.956 and abs(p.x) < 0.065 * H and p.y < front_y + 0.05 * H]
brows = []
if band:
    lum = {v: float(colour(v) @ np.array([0.299, 0.587, 0.114])) for v in band}
    skin = float(np.percentile(list(lum.values()), 75))  # most of the band is forehead skin
    brows = [v for v in band if lum[v] < skin - 0.08]
flat = set(brows)
for _ in range(2):  # and a ring round them, so the strip's sides go down with it
    flat |= {e.other_vert(v) for v in list(flat) for e in v.link_edges}
for _ in range(opts['brow-flatten'] if brows else 0):
    bmesh.ops.smooth_vert(bm, verts=list(flat), factor=0.6, use_axis_x=True, use_axis_y=True, use_axis_z=True)
bm.to_mesh(body.data)
bm.free()
quads = sum(1 for p in body.data.polygons if len(p.vertices) == 4)
print(f'[smooth] {tris_before} triangles -> {len(body.data.polygons)} faces ({quads} quads); '
      f'brows: {len(brows)} vertices found' + (f', {len(flat)} pressed flat' if opts['brow-flatten'] else ''))

if opts['levels'] > 0:
    mod = body.modifiers.new('subdiv', 'SUBSURF')
    mod.levels = mod.render_levels = opts['levels']
    mod.uv_smooth = 'PRESERVE_BOUNDARIES'
    mod.boundary_smooth = 'ALL'
    bpy.ops.object.modifier_apply(modifier=mod.name)
for p in body.data.polygons:
    p.use_smooth = True
tris_after = sum(len(p.vertices) - 2 for p in body.data.polygons)
print(f'[smooth] subdivided x{opts["levels"]}: {tris_after} triangles')
bpy.ops.export_scene.gltf(filepath=DST, export_format='GLB', use_selection=False, export_draco_mesh_compression_enable=False,
                          export_image_format='AUTO')
print(f'[smooth] wrote {DST}')
