"""Colour an untextured, joined Tripo bar with durable game material regions."""
from pathlib import Path
import bpy, json, math, sys
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
SOURCE=ROOT/'ArtSource/Resort/TripoBar/source.glb'
OUT=ROOT/'Assets/_Game/Art/Props'; REVIEW=ROOT/'Screenshots/Review/ResortBlender'
PALETTE={
 'resort_bar_thatch':(.78,.60,.32), 'resort_bar_thatch_light':(.87,.71,.43),
 'resort_bar_wood':(.57,.34,.17), 'resort_bar_deck':(.69,.47,.27),
 'resort_bar_countertop':(.27,.16,.10), 'resort_bar_ivory':(.96,.91,.80),
 'resort_bar_teal':(.08,.47,.48), 'resort_bar_coral':(.84,.38,.27),
 'resort_bar_brass':(.79,.60,.26), 'resort_bar_lantern':(1,.68,.28),
}
def linear(c):return c/12.92 if c<=.04045 else ((c+.055)/1.055)**2.4
def region(p):
    x,y,z=p; ax=abs(x)
    # Roof follows a pitched envelope; timber rafters remain beneath this envelope.
    if z > .509-ax*.50:return 'resort_bar_thatch'
    if z<.027:return 'resort_bar_deck'
    # Upholstered lounge chairs flank the central bar on both sides.
    chair = .31<ax<.456 and any(abs(y-c)<.068 for c in (.267,.10,-.145,-.327))
    post = abs(ax-.404)<.014 and any(abs(y-c)<.018 for c in (.39,0,-.39))
    if chair and not post and .036<z<.155:
        return 'resort_bar_teal' if y<0 else 'resort_bar_coral'
    # Front stool seats; legs retain timber.
    stool = (ax<.18 and -.34<y<-.29) or (.192<ax<.246 and -.27<y<.01)
    if stool and .079<z<.092:return 'resort_bar_teal'
    if stool and z<.095:return 'resort_bar_wood'
    counter = ax<.205 and -.30<y<.22 and (y<-.245 or ax>.145)
    if counter:
        if .109<z<.155:return 'resort_bar_countertop'
        if .032<z<.109:return 'resort_bar_teal' if z>.096 else 'resort_bar_ivory'
    # Two hanging lamps in the open front gable.
    if .19<ax<.25 and -.41<y<-.30 and .17<z<.25:
        return 'resort_bar_lantern' if z<.225 else 'resort_bar_brass'
    return 'resort_bar_wood'

report=[]
repaint='--repaint' in sys.argv
for level,ratio in enumerate((.10,.035,.01)):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    path=OUT/('resort_tripo_beach_bar_lod'+str(level)+'.glb')
    bpy.ops.import_scene.gltf(filepath=str(path if repaint else SOURCE))
    o=next(o for o in bpy.context.scene.objects if o.type=='MESH');bpy.context.view_layer.objects.active=o
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    if not repaint:
        d=o.modifiers.new('Game geometry', 'DECIMATE');d.ratio=ratio;d.use_collapse_triangulate=True
        bpy.ops.object.modifier_apply(modifier=d.name)
    o.name='CoralBeachClub_LOD'+str(level);o.data.materials.clear()
    indices={}
    for name,rgb in PALETTE.items():
        m=bpy.data.materials.new(name);m.use_nodes=True;p=m.node_tree.nodes.get('Principled BSDF')
        rgba=tuple(linear(c) for c in rgb)+(1,);p.inputs['Base Color'].default_value=rgba;m.diffuse_color=rgba
        p.inputs['Roughness'].default_value=.38 if name.endswith('countertop') else .75 if 'thatch' in name else .55
        if name.endswith('brass'):p.inputs['Metallic'].default_value=.7;p.inputs['Roughness'].default_value=.3
        if name.endswith('lantern'):p.inputs['Emission Color'].default_value=(1,.35,.07,1);p.inputs['Emission Strength'].default_value=1.2
        indices[name]=len(o.data.materials);o.data.materials.append(m)
    counts={name:0 for name in PALETTE}
    for f in o.data.polygons:
        p=f.center
        if repaint:p=Vector((p.x*(.9783934355/26),p.y*(.9381713867/16),p.z*(.5473327637/7.2)))
        name=region(p);f.material_index=indices[name];f.use_smooth=True;counts[name]+=1
    # Match the existing pavilion's 26m width; reduce its exaggerated vertical scale
    # to a ~7.2m roof and a human-height countertop.
    if not repaint:
        for v in o.data.vertices:v.co=Vector((v.co.x*(26/.9783934355),v.co.y*(16/.9381713867),v.co.z*(7.2/.5473327637)))
    o.data.update()
    # Keep shading continuous on the sculpted thatch while restoring crisp large corners.
    import bmesh
    bm=bmesh.new();bm.from_mesh(o.data);bm.normal_update()
    for e in bm.edges:
        if e.is_manifold and e.calc_face_angle(0)>math.radians(50):e.smooth=False
    bm.to_mesh(o.data);bm.free()
    o.data.validate();o.data.update()
    bpy.ops.export_scene.gltf(filepath=str(path),export_format='GLB',export_materials='EXPORT',export_yup=True)
    report.append(dict(lod=level,triangles=len(o.data.polygons),materials=counts,path=str(path)))
    if level==0:
        bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'ArtSource/Resort/tripo_beach_bar_coloured.blend'))
        scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE'
        scene.render.resolution_x=1440;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
        scene.world=bpy.data.worlds.new('Review sky');scene.world.use_nodes=True
        scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.20,.26,.31,1)
        scene.world.node_tree.nodes['Background'].inputs[1].default_value=.7
        bpy.ops.object.light_add(type='SUN');bpy.context.object.rotation_euler=(.45,-.5,-.5);bpy.context.object.data.energy=2.2
        bpy.ops.object.light_add(type='AREA',location=(-10,-12,18));bpy.context.object.data.energy=20000;bpy.context.object.data.size=22
        bpy.context.object.rotation_euler=(Vector((0,0,3))-bpy.context.object.location).to_track_quat('-Z','Y').to_euler()
        bpy.ops.object.camera_add(location=(-27,-31,18));cam=bpy.context.object;cam.data.type='ORTHO';cam.data.ortho_scale=34
        cam.rotation_euler=(Vector((0,0,3.5))-cam.location).to_track_quat('-Z','Y').to_euler();scene.camera=cam
        scene.view_settings.view_transform='AgX';scene.render.filepath=str(REVIEW/'tripo_bar_coloured.png');bpy.ops.render.render(write_still=True)
(ROOT/'Logs/tripo-bar-colour.json').write_text(json.dumps(report,indent=2))
