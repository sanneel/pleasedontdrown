"""Standalone Blender visual review of the authored resort assets."""
from pathlib import Path
import bpy
from mathutils import Vector
ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Screenshots/Review/ResortBlender'; OUT.mkdir(parents=True, exist_ok=True)
for asset in ('resort_beach_bar', 'resort_cabana', 'hotel_polished'):
    bpy.ops.wm.open_mainfile(filepath=str(ROOT / 'ArtSource/Resort' / (asset + '.blend')))
    objects = [o for o in bpy.context.scene.objects if o.type == 'MESH' and not o.hide_render]
    points = [o.matrix_world @ Vector(p) for o in objects for p in o.bound_box]
    low = Vector(tuple(min(p[i] for p in points) for i in range(3)))
    high = Vector(tuple(max(p[i] for p in points) for i in range(3)))
    target = (low + high) * .5; size = max(high - low)
    scene = bpy.context.scene; scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 1440; scene.render.resolution_y = 1000; scene.render.resolution_percentage = 100
    scene.world = bpy.data.worlds.new('Warm review sky'); scene.world.use_nodes = True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value = (.20, .26, .31, 1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value = .7
    bpy.ops.object.light_add(type='SUN', location=target + Vector((-size, -size, size * 2)))
    bpy.context.object.rotation_euler = (.45, -.5, -.5); bpy.context.object.data.energy = 2.2
    bpy.ops.object.light_add(type='AREA', location=target + Vector((size, size * .5, size)))
    fill = bpy.context.object; fill.data.energy = size * size * 90; fill.data.shape = 'DISK'; fill.data.size = size
    fill.rotation_euler = (target - fill.location).to_track_quat('-Z', 'Y').to_euler()
    bpy.ops.object.camera_add(location=target + Vector((-size * 1.25, -size * 1.55, size * .85)))
    camera = bpy.context.object; camera.rotation_euler = (target - camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera.data.type = 'ORTHO'; camera.data.ortho_scale = size * 1.5; scene.camera = camera
    scene.view_settings.view_transform = 'AgX'; scene.view_settings.exposure = .2
    scene.render.filepath = str(OUT / (asset + '.png')); bpy.ops.render.render(write_still=True)
