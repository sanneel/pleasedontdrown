"""Turn Tripo's web (meshopt-compressed, quantized) GLBs into plain GLBs Unity reads, keeping geometry and textures.

Blender --background --factory-startup --python ArtSource/Tools/tripo_decode.py -- <in.glb> <out.glb> [<in.glb> <out.glb> ...]
"""
import sys

import bpy

args = sys.argv[sys.argv.index("--") + 1:]
for src, dst in zip(args[0::2], args[1::2]):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=src)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in meshes)
    bpy.ops.export_scene.gltf(filepath=dst, export_format="GLB", export_draco_mesh_compression_enable=False)
    print(f"[tripo_decode] {src} -> {dst}: {len(meshes)} meshes, {tris} triangles")
