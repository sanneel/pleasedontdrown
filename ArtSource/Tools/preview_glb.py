"""Renders quick look pictures of a GLB (front, side, back, 3/4) and prints its size, triangles and skin state.

blender -b --factory-startup -P ArtSource/Tools/preview_glb.py -- <in.glb> <out prefix> [views, e.g. front,side,back,quarter]
"""
import math
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
SRC, PREFIX = argv[0], argv[1]
VIEWS = argv[2].split(',') if len(argv) > 2 else ['front', 'side', 'back', 'quarter']

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
lo = Vector((1e9, 1e9, 1e9)); hi = Vector((-1e9, -1e9, -1e9))
tris = 0
for o in meshes:
    o.data.calc_loop_triangles()
    tris += len(o.data.loop_triangles)
    for c in o.bound_box:
        w = o.matrix_world @ Vector(c)
        lo = Vector(map(min, lo, w)); hi = Vector(map(max, hi, w))
size = hi - lo
centre = (lo + hi) * 0.5
arm = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
print('[preview]', SRC)
print('[preview] meshes', len(meshes), 'tris', tris, 'size x/y/z', [round(v, 3) for v in size], 'min z', round(lo.z, 3),
      'armatures', len(arm), 'bones', sum(len(a.data.bones) for a in arm),
      'materials', sorted({m.name for o in meshes for m in o.data.materials if m}),
      'images', [(i.name, tuple(i.size)) for i in bpy.data.images])

scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'TEXTURE'
scene.display.shading.show_cavity = True
scene.render.resolution_x = 700
scene.render.resolution_y = 900
scene.render.film_transparent = False
scene.world = bpy.data.worlds.new('w')
scene.world.color = (0.55, 0.6, 0.65)
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam'))
scene.collection.objects.link(cam)
scene.camera = cam
cam.data.type = 'ORTHO'
cam.data.ortho_scale = max(size.x, size.y, size.z) * 1.15
dist = max(size) * 4 + 2
dirs = {'front': Vector((0, -1, 0)), 'back': Vector((0, 1, 0)), 'side': Vector((1, 0, 0)), 'left': Vector((-1, 0, 0)),
        'quarter': Vector((0.7, -0.7, 0.35)).normalized(), 'top': Vector((0, -0.001, 1)).normalized()}
for view in VIEWS:
    d = dirs[view]
    cam.location = centre + d * dist
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = f'{PREFIX}_{view}.png'
    bpy.ops.render.render(write_still=True)
