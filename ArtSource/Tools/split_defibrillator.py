# Splits the defibrillator model into the case (Assets/_Game/Art/Props/defibrillator.glb, same object and mesh name so
# the item prefab keeps its mesh) and one paddle (Assets/_Game/Resources/Props/DefibPaddle.glb, origin at the centre of
# its contact plate, handle up). Run once (2026-10-12) on the original model with both paddles on the lid: get it back
# from git history (git show a0527650:Assets/_Game/Art/Props/defibrillator.glb) before running it again.
# blender -b -P split_defibrillator.py   (writes to F:/temp/defib_out)
import bpy, mathutils
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath="F:/GameDev/PleaseDontDrown/Assets/_Game/Art/Props/defibrillator.glb")
o=[o for o in bpy.context.scene.objects if o.type=='MESH'][0]
mesh_name=o.data.name; obj_name=o.name
print("ORIG", obj_name, mesh_name, o.location[:], o.rotation_mode, o.rotation_quaternion[:], o.scale[:])
bpy.context.view_layer.objects.active=o; o.select_set(True)
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.separate(type='LOOSE'); bpy.ops.object.mode_set(mode='OBJECT')
case=[];L=[];R=[]
for p in list(bpy.context.scene.objects):
    if p.type!='MESH': continue
    vs=[p.matrix_world@v.co for v in p.data.vertices]
    c=sum(vs,mathutils.Vector())/len(vs)
    if abs(abs(c.x)-0.12)<0.04 and abs(c.y+0.09)<0.03 and c.z>0.05: (R if c.x>0 else L).append(p)
    else: case.append(p)
print("counts", len(case), len(L), len(R))
def join(parts,name):
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts: p.select_set(True)
    bpy.context.view_layer.objects.active=parts[0]; bpy.ops.object.join()
    j=bpy.context.view_layer.objects.active; j.name=name; j.data.name=name; return j
caseo=join(case,"tmpcase"); Ro=join(R,"DefibPaddle"); Lo=join(L,"PaddleL")
bpy.data.objects.remove(Lo)
# paddle: origin at the contact face centre (bottom of the steel plate), at the world origin
bpy.ops.object.select_all(action='DESELECT'); Ro.select_set(True); bpy.context.view_layer.objects.active=Ro
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
vs=[v.co for v in Ro.data.vertices]
minz=min(v.z for v in vs); cx=(min(v.x for v in vs)+max(v.x for v in vs))/2; cy=(min(v.y for v in vs)+max(v.y for v in vs))/2
for v in Ro.data.vertices: v.co-=mathutils.Vector((cx,cy,minz))
print("paddle size", [round(max(v.co[i] for v in Ro.data.vertices)-min(v.co[i] for v in Ro.data.vertices),3) for i in range(3)], "contact at", round(cx,3), round(cy,3), round(minz,3))
# export paddle alone
caseo.hide_set(True)
bpy.ops.object.select_all(action='DESELECT'); Ro.select_set(True)
bpy.ops.export_scene.gltf(filepath=r"%s/out/DefibPaddle.glb"%"F:/temp/claude/F--GameDev-PleaseDontDrown/d36ad26f-5443-4e25-8ba2-ac01eed8614f/scratchpad", use_selection=True, export_format='GLB')
# export case with the original object/mesh name
caseo.hide_set(False); bpy.data.objects.remove(Ro)
caseo.name=obj_name; caseo.data.name=mesh_name
bpy.ops.object.select_all(action='DESELECT'); caseo.select_set(True)
bpy.ops.export_scene.gltf(filepath=r"%s/out/defibrillator.glb"%"F:/temp/claude/F--GameDev-PleaseDontDrown/d36ad26f-5443-4e25-8ba2-ac01eed8614f/scratchpad", use_selection=True, export_format='GLB')
