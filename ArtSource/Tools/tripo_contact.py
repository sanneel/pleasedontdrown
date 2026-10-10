"""Review renders of Tripo GLBs: front three-quarter and back three-quarter, one PNG per model.

Blender -b --factory-startup -P ArtSource/Tools/tripo_contact.py -- <out_dir> <a.glb> [<b.glb> ...]
"""
import math
import os
import sys

import bpy
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
out_dir, files = args[0], args[1:]
os.makedirs(out_dir, exist_ok=True)

for path in files:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = scene.render.resolution_y = 512
    scene.world = bpy.data.worlds.new("w")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.62, 0.7, 1)
    scene.world.node_tree.nodes["Background"].inputs[1].default_value = 1.0
    bpy.ops.import_scene.gltf(filepath=path)
    meshes = [o for o in scene.objects if o.type == "MESH"]
    lo = Vector((1e9,) * 3); hi = Vector((-1e9,) * 3)
    for o in meshes:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            lo = Vector(map(min, lo, w)); hi = Vector(map(max, hi, w))
    centre, size = (lo + hi) / 2, (hi - lo).length
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 4
    sun.rotation_euler = (math.radians(50), 0, math.radians(30))
    scene.collection.objects.link(sun)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    name = os.path.splitext(os.path.basename(path))[0]
    for label, yaw in (("front", -35), ("back", 145)):
        a = math.radians(yaw)
        cam.location = centre + Vector((math.sin(a), -math.cos(a), 0.45)) * size * 1.25
        cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(out_dir, f"{name}_{label}.png")
        bpy.ops.render.render(write_still=True)
    print(f"[tripo_contact] {name}: size {tuple(round(v, 2) for v in (hi - lo))}")
