"""Four orthographic side views of a GLB, named by the Blender axis the camera looks FROM (pos_x, neg_x, pos_y, neg_y).

Blender -b --factory-startup -P ArtSource/Tools/tripo_sides.py -- <out_dir> <a.glb> [...]
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
    scene.render.resolution_x = scene.render.resolution_y = 320
    scene.world = bpy.data.worlds.new("w")
    scene.world.color = (0.55, 0.62, 0.7)
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
    sun.rotation_euler = (math.radians(40), 0, math.radians(20))
    scene.collection.objects.link(sun)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = size
    scene.collection.objects.link(cam)
    scene.camera = cam
    name = os.path.splitext(os.path.basename(path))[0]
    for label, d in (("pos_x", (1, 0, 0)), ("neg_x", (-1, 0, 0)), ("pos_y", (0, 1, 0)), ("neg_y", (0, -1, 0))):
        cam.location = centre + Vector(d) * size * 2 + Vector((0, 0, size * 0.25))
        cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(out_dir, f"{name}_{label}.png")
        bpy.ops.render.render(write_still=True)
