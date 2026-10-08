"""Editable Blender preview of the real reception cut and matching entrance finish."""
from pathlib import Path
import bpy,math
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Screenshots/Review/HotelFinish';OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(ROOT/'Assets/_Game/Art/Props/resort_hotel_reception_lod0.glb'))
facade=bpy.data.materials.new('Clean matte hotel facade');facade.use_nodes=True
shader=facade.node_tree.nodes.get('Principled BSDF');shader.inputs['Roughness'].default_value=.84
tex=facade.node_tree.nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(ROOT/'Assets/TripoModels/apartment_building_3d_model/apartment_building_3d_model.fbm/apartment_building_3d_model_basecolor.JPEG'))
facade.node_tree.links.new(tex.outputs['Color'],shader.inputs['Base Color'])
for o in list(bpy.context.scene.objects):
    if o.type=='MESH':
        o.name='SingleHotel_ReceptionCut';o.scale=(40/.780731201,)*3;o.location.y=4;o.data.materials.clear();o.data.materials.append(facade)
bpy.ops.import_scene.gltf(filepath=str(ROOT/'Assets/_Game/Art/Props/resort_reception_trim.glb'))
def linear(c):return c/12.92 if c<=.04045 else ((c+.055)/1.055)**2.4
def mat(name,colour,rough=.8,metal=0):
    m=bpy.data.materials.new(name);m.use_nodes=True;p=m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value=tuple(linear(c) for c in colour)+(1,);p.inputs['Roughness'].default_value=rough;p.inputs['Metallic'].default_value=metal
    return m
ivory=mat('Finished ivory',(.96,.93,.85));timber=mat('Desk dark timber',(.30,.20,.12),.65)
brass=mat('Entrance brass',(.76,.60,.30),.55,.5);stone=mat('Warm limestone',(.87,.84,.79))
def cube(name,pos,size,m):
    bpy.ops.mesh.primitive_cube_add(size=1,location=(pos[0],-pos[2],pos[1]));o=bpy.context.object;o.name=name
    o.dimensions=(size[0],size[2],size[1]);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);o.data.materials.append(m);return o
cube('LobbyFloor',(0,.15,0),(24,.3,12),stone)
cube('LobbyCeiling',(0,3.65,0),(24.4,.3,12.4),ivory)
cube('LobbyBackWall',(0,1.9,-6),(24,3.2,.3),ivory)
for side in (-1,1):
    cube('LobbySideWall',(side*12,1.9,0),(.3,3.2,12),ivory)
    cube('LobbyFrontWall',(side*6.65,1.9,6),(10.7,3.2,.3),ivory)
cube('DoorLintel',(0,3.2,6),(2.6,.6,.3),ivory)
cube('ReceptionDesk',(-6,.85,2.2),(3.2,1.1,.8),ivory)
cube('ReceptionTop',(-6,1.43,2.25),(3.4,.06,1),timber)
cube('VestibuleDeck',(0,.15,11),(4,.3,10),stone)
for i in range(3):cube('EntranceStep',(0,.05*(3-i),16.3+i*.55),(4.4,.1*(3-i),.65),ivory)
for side in (-1,1):
    cube('VestibuleBase',(side*2.2,.65,11),(.2,.7,10),ivory)
    cube('VestibuleRail',(side*2.2,1.1,11),(.24,.1,10),brass)
    cube('EntrancePortal',(side*2.2,2.05,16),(.3,3.5,.3),ivory)
cube('EntranceLintel',(0,3.75,16),(4.7,.3,.35),ivory)
for pos in ((0,3.15,9),(0,3.15,13),(0,3,0),(-6,3,2)):
    bpy.ops.object.light_add(type='POINT',location=(pos[0],-pos[2],pos[1]));bpy.context.object.data.energy=140;bpy.context.object.data.color=(1,.83,.60);bpy.context.object.data.shadow_soft_size=1
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'ArtSource/Resort/hotel_reception_finished.blend'))
scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=1600;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.world=bpy.data.worlds.new('Resort daylight');scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.22,.28,.34,1);scene.world.node_tree.nodes['Background'].inputs[1].default_value=.65
bpy.ops.object.light_add(type='SUN');bpy.context.object.rotation_euler=(.55,-.5,-.45);bpy.context.object.data.energy=2.2
bpy.ops.object.camera_add();cam=bpy.context.object;scene.camera=cam;cam.data.clip_end=500;scene.view_settings.view_transform='AgX'
for name,loc,target,lens in [('exterior',(-48,-75,30),(0,0,16),40),('entrance',(0,-24,1.8),(0,-6,1.8),25),('reception',(3.5,-4.5,1.8),(-6,-1,1.5),23)]:
    cam.location=loc;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.lens=lens
    scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
