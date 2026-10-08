"""Reimport every polished GLB and verify topology, skins, textures and finite data."""
import json
import sys
from pathlib import Path
import bpy
import numpy as np

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Screenshots/Review/BlenderPolish'
SOURCE=ROOT/'ArtSource/Polish'

def inspect(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(path))
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
    tris=0
    weights=[]
    for ob in meshes:
        ob.data.calc_loop_triangles()
        tris+=len(ob.data.loop_triangles)
        assert all(np.isfinite(tuple(v.co)).all() for v in ob.data.vertices),path
        if ob.vertex_groups:
            weights.extend(sum(g.weight for g in v.groups) for v in ob.data.vertices)
    if weights:
        assert max(abs(w-1) for w in weights)<.002,(path,'skin weights')
    bones={b.name:[float(x) for row in (o.matrix_world @ b.matrix_local) for x in row]
           for o in bpy.context.scene.objects if o.type=='ARMATURE' for b in o.data.bones}
    textures=sorted(tuple(i.size) for i in bpy.data.images if i.name!='Render Result')
    return {'triangles':tris,'bones':bones,'textures':textures}

results=[]
for original in ([] if '--review-only' in sys.argv else sorted((SOURCE/'Originals').glob('*.glb'))):
    tourist=original.stem.startswith('tourist')
    target=ROOT/'Assets/_Game/Art'/('Characters' if tourist else 'Weapons')/original.name
    before=inspect(original)
    after=inspect(target)
    assert before['bones'].keys()==after['bones'].keys(),(target,'changed bone names')
    bone_error=max((max(abs(a-b) for a,b in zip(before['bones'][k],after['bones'][k])) for k in before['bones']),default=0)
    # glTF quaternion/matrix round-trips accumulate a few float32 ULPs.
    assert bone_error<.0001,(target,'changed rig',bone_error)
    if tourist:
        assert before['triangles']==after['triangles'],(target,'changed topology')
        assert before['textures']==after['textures'],(target,'changed texture resolution')
    assert after['triangles']<40000,(target,'triangle budget')
    results.append({'file':target.name,'triangles':after['triangles'],'bones':len(after['bones']),'max_bone_matrix_error':bone_error,'passed':True})
if results:
    (OUT/'validation.json').write_text(json.dumps(results,indent=2))
    print('VALIDATED',len(results),'assets')

# A single editable Blender review file, with one scene per base model.
bpy.ops.wm.read_factory_settings(use_empty=True)
empty=bpy.context.scene
for path in sorted(SOURCE.glob('*_polished.blend')):
    with bpy.data.libraries.load(str(path),link=False) as (src,dst):
        dst.scenes=src.scenes
    for scene in dst.scenes:
        scene.name=path.stem.replace('_polished','')
        for ob in scene.objects:
            if ob.type=='ARMATURE': ob.hide_set(True,view_layer=scene.view_layers[0])
bpy.context.window.scene=bpy.data.scenes['tourist_bikini_red']
bpy.data.scenes.remove(empty)
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.region_3d.view_perspective='CAMERA'
            area.spaces.active.shading.type='MATERIAL'
            area.spaces.active.overlay.show_overlays=False
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'Tourists_and_Guns.blend'))
