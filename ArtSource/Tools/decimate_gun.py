"""
A Meshy gun for the game: joins it into one mesh, decimates it to a triangle budget (keeping its UVs and texture) and
exports a GLB.

    blender -b --factory-startup -P ArtSource/Tools/decimate_gun.py -- <in.glb> <out.glb> <target_tris>
"""
import bpy, sys

argv = sys.argv[sys.argv.index("--") + 1:]
src, out, target = argv[0], argv[1], int(argv[2])

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active
tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
if tris > target:
    mod = obj.modifiers.new("dec", 'DECIMATE')
    mod.ratio = target / tris
    bpy.ops.object.modifier_apply(modifier="dec")
after = sum(len(p.vertices) - 2 for p in obj.data.polygons)
print(f"DECIMATE {src}: {tris} -> {after} triangles")
bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', use_selection=True)
