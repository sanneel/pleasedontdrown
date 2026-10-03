"""Renders a GLB orthographically from the front and the side with a 5 cm grid behind it, so points on the model can be
read off in Blender coordinates (Z up, the model faces -Y).

blender -b --factory-startup -P ArtSource/Tools/ortho_grid.py -- <in.glb> <out prefix> [--centre x,y,z] [--size metres]

Writes <prefix>_front.png (x right, z up) and <prefix>_side.png (looking from +X: -y right... see the printed
mapping) and prints how pixels map to coordinates.
"""
import sys
import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
SRC, PREFIX = argv[0], argv[1]
opts = dict(zip(argv[2::2], argv[3::2]))
RES = 1000

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
lo = Vector((1e9, 1e9, 1e9)); hi = -lo
for o in meshes:
    for c in o.bound_box:
        w = o.matrix_world @ Vector(c)
        lo = Vector(map(min, lo, w)); hi = Vector(map(max, hi, w))
centre = Vector(map(float, opts['--centre'].split(','))) if '--centre' in opts else (lo + hi) / 2
size = float(opts.get('--size', max(hi - lo) * 1.05))
print(f'[grid] bounds {tuple(round(v, 3) for v in lo)} .. {tuple(round(v, 3) for v in hi)}')

scene = bpy.context.scene
scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x = scene.render.resolution_y = RES
scene.world = bpy.data.worlds.new('w')
scene.world.color = (0.6, 0.6, 0.6)
sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN'))
sun.data.energy = 3
sun.rotation_euler = (0.7, 0.2, 0.4)
scene.collection.objects.link(sun)

# A grid of thin bars every 5 cm (every 10 cm thicker), well behind the model for each view.
grid_mat = bpy.data.materials.new('grid')
grid_mat.use_nodes = True
grid_mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.05, 0.05, 0.05, 1)
def bar(loc, dims, coll):
    bpy.ops.mesh.primitive_cube_add(location=loc)
    b = bpy.context.object
    b.dimensions = dims
    b.data.materials.append(grid_mat)
    for c in b.users_collection: c.objects.unlink(b)
    coll.objects.link(b)
    return b

def render(name, cam_loc, cam_rot, plane_axes, depth_axis, depth):
    coll = bpy.data.collections.new(name)
    scene.collection.children.link(coll)
    half = size / 2
    a, b = plane_axes
    steps = int(size / 0.05) + 2
    for i in range(-steps, steps + 1):
        for axis, other in ((a, b), (b, a)):
            v = round((centre[axis] // 0.05) * 0.05 + i * 0.05, 3)
            if abs(v - centre[axis]) > half: continue
            thick = 0.004 if abs(round(v * 10) - v * 10) < 1e-3 else 0.0015
            loc = [0, 0, 0]; dims = [0, 0, 0]
            loc[axis] = v; loc[other] = centre[other]; loc[depth_axis] = depth
            dims[axis] = thick; dims[other] = size; dims[depth_axis] = 0.001
            bar(loc, dims, coll)
    cam = bpy.data.objects.new('cam_' + name, bpy.data.cameras.new('cam_' + name))
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = size
    cam.location = cam_loc
    cam.rotation_euler = cam_rot
    scene.collection.objects.link(cam)
    scene.camera = cam
    scene.render.filepath = f'{PREFIX}_{name}.png'
    bpy.ops.render.render(write_still=True)
    bpy.data.collections.remove(coll)

import math
c = centre
# Front: camera at -Y looking +Y; image x = world x, image up = world z.
render('front', (c.x, c.y - 5, c.z), (math.pi / 2, 0, 0), (0, 2), 1, c.y + 1.0)
# Side: camera at +X looking -X; image x = world y, image up = world z.
render('side', (c.x + 5, c.y, c.z), (math.pi / 2, 0, math.pi / 2), (1, 2), 0, c.x - 1.0)
# Other side: camera at -X looking +X; image x = world -y.
render('left', (c.x - 5, c.y, c.z), (math.pi / 2, 0, -math.pi / 2), (1, 2), 0, c.x + 1.0)
print(f'[grid] front: px -> x = {c.x - size/2:.4f} + px*{size/RES:.5f}, z = {c.z + size/2:.4f} - py*{size/RES:.5f}')
print(f'[grid] left:  px -> y = {c.y + size/2:.4f} - px*{size/RES:.5f}, z = {c.z + size/2:.4f} - py*{size/RES:.5f}')
print(f'[grid] side:  px -> y = {c.y - size/2:.4f} + px*{size/RES:.5f}, z = {c.z + size/2:.4f} - py*{size/RES:.5f}')
