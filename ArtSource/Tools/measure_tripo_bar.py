from pathlib import Path
import bpy,json
ROOT=Path(__file__).resolve().parents[2]
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'ArtSource/Resort/TripoBar/analysis.blend'))
o=next(o for o in bpy.context.scene.objects if o.type=='MESH')
bins={}
for f in o.data.polygons:
    p=f.center
    if abs(p.x)<.26 and -.36<p.y<.3 and p.z<.22 and f.normal.z>.8:
        key=round(p.z/.002)*.002;bins[key]=bins.get(key,0)+f.area
print('HORIZONTAL HEIGHTS '+json.dumps(sorted(bins.items(),key=lambda p:p[1],reverse=True)[:20]))
scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=1200;scene.render.resolution_y=1200
scene.world=bpy.data.worlds.new('White');scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs[1].default_value=.7
bpy.ops.object.light_add(type='SUN');bpy.context.object.data.energy=2
from mathutils import Vector
bpy.ops.object.camera_add(location=(0,-.002,1.8));cam=bpy.context.object;cam.data.type='ORTHO';cam.data.ortho_scale=1.05
cam.rotation_euler=(Vector((0,0,0))-cam.location).to_track_quat('-Z','Y').to_euler();scene.camera=cam
# Hide roof for a readable furniture plan.
for f in o.data.polygons:
    if f.center.z>.27:f.material_index=0
import bmesh
bm=bmesh.new();bm.from_mesh(o.data)
bmesh.ops.delete(bm,geom=[f for f in bm.faces if f.calc_center_median().z>.27],context='FACES')
bm.to_mesh(o.data);bm.free()
scene.render.filepath=str(ROOT/'Screenshots/Review/ResortBlender/tripo_bar_plan.png');bpy.ops.render.render(write_still=True)
