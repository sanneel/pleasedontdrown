"""Inspect the supplied untextured bar; preserve source and render both sides."""
from pathlib import Path
import bpy, json, shutil
from mathutils import Vector
ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'ArtSource/Resort/TripoBar/source.glb'
SOURCE.parent.mkdir(parents=True, exist_ok=True)
if not SOURCE.exists():
    shutil.copyfile('C:/Users/User/Desktop/thatched hut bar 3d model.glb', SOURCE)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(SOURCE))
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
points = [o.matrix_world @ Vector(p) for o in meshes for p in o.bound_box]
lo = Vector(tuple(min(p[i] for p in points) for i in range(3)))
hi = Vector(tuple(max(p[i] for p in points) for i in range(3)))
report = {'bounds_min':list(lo), 'bounds_max':list(hi), 'objects':[]}
for o in meshes:
    report['objects'].append({'name':o.name, 'vertices':len(o.data.vertices), 'faces':len(o.data.polygons)})
    matrix = o.matrix_world.copy()
    for v in o.data.vertices: v.co = (matrix @ v.co - Vector(((lo.x+hi.x)*.5, (lo.y+hi.y)*.5, lo.z))) * (26 / (hi.x-lo.x))
    o.matrix_world.identity()
    bpy.context.view_layer.objects.active = o
    d = o.modifiers.new('Inspection preview only', 'DECIMATE'); d.ratio=.10
    bpy.ops.object.modifier_apply(modifier=d.name)
(ROOT/'Logs/tripo-bar-source.json').write_text(json.dumps(report,indent=2))
scene=bpy.context.scene; scene.render.engine='BLENDER_EEVEE'
scene.render.resolution_x=1400; scene.render.resolution_y=1000; scene.render.resolution_percentage=100
scene.world=bpy.data.worlds.new('Review sky'); scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.2,.26,.31,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value=.7
bpy.ops.object.light_add(type='SUN'); bpy.context.object.rotation_euler=(.45,-.5,-.5); bpy.context.object.data.energy=2.2
bpy.ops.object.light_add(type='AREA', location=(10,-12,18)); bpy.context.object.data.energy=14000; bpy.context.object.data.size=22
bpy.context.object.rotation_euler=(Vector((0,0,3))-bpy.context.object.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(); cam=bpy.context.object; cam.data.type='ORTHO';cam.data.ortho_scale=35;scene.camera=cam
scene.view_settings.view_transform='AgX'
out=ROOT/'Screenshots/Review/ResortBlender';out.mkdir(parents=True,exist_ok=True)
for side, location in [('tripo_bar_before_front',(-28,-36,21)),('tripo_bar_before_back',(28,36,21))]:
    cam.location=location;cam.rotation_euler=(Vector((0,0,5))-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(out/(side+'.png'));bpy.ops.render.render(write_still=True)
