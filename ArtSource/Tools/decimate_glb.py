# Blender batch script: reduce a Meshy GLB's triangle count, keeping its textures.
# blender -b --factory-startup --python decimate_glb.py -- <in.glb> <out.glb> <target_triangles>
import sys
import bpy

argv = sys.argv[sys.argv.index("--") + 1:]
src, dst, target = argv[0], argv[1], int(argv[2])

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)

meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
def tris(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)
before = sum(tris(o) for o in meshes)
ratio = min(1.0, target / max(1, before))
for o in meshes:
    bpy.context.view_layer.objects.active = o
    mod = o.modifiers.new("Decimate", 'DECIMATE')
    mod.decimate_type = 'COLLAPSE'
    mod.ratio = ratio
    mod.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=mod.name)
after = sum(tris(o) for o in meshes)
print(f"[decimate] {src}: {before} -> {after} triangles (ratio {ratio:.3f})")

bpy.ops.export_scene.gltf(filepath=dst, export_format='GLB', export_image_format='AUTO', export_apply=True)
