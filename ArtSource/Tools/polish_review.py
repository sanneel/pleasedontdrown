"""Renders a character GLB with a colour (and normal) texture from the front, the back and the face, for checking a
polish before it goes into Unity.

blender -b --factory-startup -P ArtSource/Tools/polish_review.py -- <model.glb> <colour> [<normal> | -] <out prefix>
Writes <prefix>_body_front.png, _body_back.png, _torso.png, _face_front.png, _face_quarter.png.
"""
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index('--') + 1:]
GLB, COLOUR, NORMAL, OUT = argv[:4]
COLOUR = os.path.abspath(COLOUR)
NORMAL = os.path.abspath(NORMAL) if NORMAL != '-' else NORMAL

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
bpy.ops.import_scene.gltf(filepath=GLB)
body = next(o for o in scene.objects if o.type == 'MESH')
rig = next(o for o in scene.objects if o.type == 'ARMATURE')
mat = bpy.data.materials.new('Review')
mat.use_nodes = True
nodes = mat.node_tree.nodes
bsdf = nodes['Principled BSDF']
bsdf.inputs['Roughness'].default_value = 0.72
tex = nodes.new('ShaderNodeTexImage')
tex.image = bpy.data.images.load(COLOUR)
mat.node_tree.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
if NORMAL != '-':
    ntex = nodes.new('ShaderNodeTexImage')
    ntex.image = bpy.data.images.load(NORMAL)
    ntex.image.colorspace_settings.name = 'Non-Color'
    nmap = nodes.new('ShaderNodeNormalMap')
    mat.node_tree.links.new(ntex.outputs['Color'], nmap.inputs['Color'])
    mat.node_tree.links.new(nmap.outputs['Normal'], bsdf.inputs['Normal'])
body.data.materials.clear()
body.data.materials.append(mat)

scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 40
scene.cycles.use_denoising = True
scene.render.resolution_x = 720
scene.render.resolution_y = 960
world = bpy.data.worlds.new('Sky')
scene.world = world
world.use_nodes = True
world.node_tree.nodes['Background'].inputs[0].default_value = (0.55, 0.68, 0.85, 1)
world.node_tree.nodes['Background'].inputs[1].default_value = 0.9
sun = bpy.data.objects.new('Sun', bpy.data.lights.new('Sun', 'SUN'))
sun.data.energy = 3.0
sun.data.angle = math.radians(8)
sun.rotation_euler = (math.radians(50), 0, math.radians(-30))
scene.collection.objects.link(sun)
cam = bpy.data.objects.new('Camera', bpy.data.cameras.new('Camera'))
cam.data.lens_unit = 'FOV'
scene.collection.objects.link(cam)
scene.camera = cam

co = np.empty(len(body.data.vertices) * 3)
body.data.vertices.foreach_get('co', co)
co = co.reshape(-1, 3)
top = float(co[:, 2].max())
head = rig.matrix_world @ rig.data.bones['Head'].head_local
face = Vector((0.0, float(co[co[:, 2] > head.z][:, 1].min()), head.z + 0.6 * (top - head.z)))


def shot(name, target, offset, fov, w, h):
    scene.render.resolution_x, scene.render.resolution_y = w, h
    cam.data.angle = math.radians(fov)
    cam.location = target + offset
    cam.rotation_euler = (-offset).to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = f'{OUT}_{name}.png'
    bpy.ops.render.render(write_still=True)


mid = Vector((0, 0, top * 0.5))
shot('body_front', mid, Vector((0, -6.0, 0.4)), 18, 720, 960)
shot('body_back', mid, Vector((0, 6.0, 0.4)), 18, 720, 960)
chest = rig.matrix_world @ rig.data.bones['Chest'].head_local
shot('torso', Vector((0, 0, chest.z)), Vector((0, -2.4, 0.1)), 22, 640, 640)
shot('face_front', face, Vector((0, -0.75, 0.03)), 28, 720, 720)
shot('face_quarter', face, Matrix.Rotation(math.radians(-35), 3, 'Z') @ Vector((0, -0.75, 0.03)), 28, 720, 720)
print('[review] done', OUT, flush=True)
