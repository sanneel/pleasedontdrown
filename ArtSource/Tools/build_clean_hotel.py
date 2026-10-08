"""Precisely model the resort hotel in metres, then export normalized Unity LODs."""
import bpy, math, json, random
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets/_Game/Art/Props'
MASTER=ROOT/'ArtSource/Resort'
REVIEW=ROOT/'Screenshots/Review/HotelClean'
SCALE=40/.780731201
for directory in (OUT,MASTER,REVIEW,ROOT/'Logs'): directory.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)

def linear(v): return v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4
def material(name,rgb,roughness=.72,metallic=0):
    m=bpy.data.materials.new(name);m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value=tuple(linear(v) for v in rgb)+(1,)
    p.inputs['Roughness'].default_value=roughness;p.inputs['Metallic'].default_value=metallic
    return m
ivory=material('resort_hotel_ivory',(.94,.92,.855),.79)
glass=material('resort_hotel_glass',(.13,.36,.46),.21,.26)
teal=material('resort_hotel_teal',(.14,.51,.52),.53,.05)
brass=material('resort_hotel_brass',(.68,.57,.36),.36,.65)
stone=material('resort_hotel_stone',(.78,.77,.71),.85)
materials=[ivory,glass,teal,brass,stone]

# A restrained UV image has a broad limestone variation and barely visible grain.
# It exports as an embedded base-colour PNG, requiring no procedural support.
random.seed(18)
size=512
for mat,col,label in ((ivory,(.94,.92,.855),'ivory'),(stone,(.78,.77,.71),'stone')):
    im=bpy.data.images.new('Hotel limestone '+label,width=size,height=size,alpha=True)
    pixels=[]
    for y in range(size):
        for x in range(size):
            variation=.005*math.sin(x*.026+y*.014)+.003*math.sin(y*.049-x*.009)+random.uniform(-.0015,.0015)
            pixels.extend([max(0,min(1,c+variation)) for c in col]+[1])
    im.pixels.foreach_set(pixels);im.filepath_raw=str(OUT/('resort_hotel_clean_limestone_'+label+'.png'));im.file_format='PNG';im.save();im.pack()
    node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=im
    mat.node_tree.links.new(node.outputs['Color'],mat.node_tree.nodes.get('Principled BSDF').inputs['Base Color'])

ALL=[]
BOXES=[]
BUFFERS={}
LOD=0
def box(name,pos,dimensions,mat,bevel=0):
    # All public coordinates are Unity metres x,y,z; Blender is x,-z,y.
    x,y,z=pos;dx,dy,dz=dimensions
    # Build buffers directly: thousands of Blender operators are unnecessarily slow.
    data=BUFFERS.setdefault(mat.name,{'verts':[],'faces':[]})
    offset=len(data['verts'])
    for xx,yy,zz in ((-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)):
        data['verts'].append((x+xx*dx/2,-z-zz*dz/2,y+yy*dy/2))
    for face in ((0,3,2,1),(4,5,6,7),(0,4,7,3),(1,2,6,5),(0,1,5,4),(3,7,6,2)):
        data['faces'].append(tuple(offset+i for i in face))
    BOXES.append({'name':name,'min':[x-dx/2,y-dy/2,z-dz/2],'max':[x+dx/2,y+dy/2,z+dz/2]})

def window(x,y,z,width=1.8,height=2.25,side=False,accent=False):
    frame=teal if accent else ivory
    if side:
        direction=1 if x>0 else -1
        box('Side glass',(x+direction*.025,y,z),(.06,height,width),glass)
        if LOD<2:
            for a in (-1,1):
                box('Side upright',(x+direction*.09,y,z+a*(width/2+.045)),(.16,height+.22,.12),frame,.016)
                box('Side sill',(x+direction*.09,y+a*(height/2+.055),z),(.2,.14,width+.22),frame,.016)
            if LOD==0:box('Side mullion',(x+direction*.10,y,z),(.14,height,.06),ivory,.012)
    else:
        direction=1 if z>0 else -1
        box('Glass pane',(x,y,z+direction*.025),(width,height,.06),glass)
        if LOD<2:
            for a in (-1,1):
                box('Frame upright',(x+a*(width/2+.045),y,z+direction*.09),(.12,height+.22,.16),frame,.016)
                box('Stone sill',(x,y+a*(height/2+.055),z+direction*.09),(width+.22,.14,.2),frame,.016)
            if LOD==0:box('Window mullion',(x,y,z+direction*.105),(.065,height,.14),ivory,.01)

def balcony(x,y,z,width=4.9,direction=1):
    depth=1.32
    box('Balcony slab',(x,y,z+direction*depth/2),(width,.18,depth),ivory,.025)
    end=z+direction*(depth-.09)
    box('Balcony top rail',(x,y+1.04,end),(width-.15,.075,.075),teal,.01)
    if LOD<2:
        box('Balcony lower rail',(x,y+.17,end),(width-.15,.06,.07),ivory,.01)
        for side in (-1,1):box('Balcony return rail',(x+side*(width/2-.1),y+1.04,z+direction*depth/2),(.075,.075,depth-.12),teal,.01)
        count=12 if LOD==0 else 5
        for n in range(count):
            xx=x-width/2+.13+(width-.26)*n/(count-1)
            box('Straight baluster',(xx,y+.61,end),(.055,.86,.055),ivory,.008)
    else:
        # Distant silhouette retains the white balustrade without tiny posts.
        box('Distant balcony balustrade',(x,y+.58,end),(width-.2,.76,.055),ivory)

def rooftop_rail(xmin,xmax,zmin,zmax,y):
    for z in (zmin,zmax):
        box('Roof handrail',((xmin+xmax)/2,y+.85,z),(xmax-xmin,.09,.09),teal,.014)
        if LOD<2:
            count=math.ceil((xmax-xmin)/(1.1 if LOD==0 else 2.5))+1
            for i in range(count):box('Roof post',(xmin+(xmax-xmin)*i/(count-1),y+.43,z),(.07,.84,.07),ivory,.01)
    for x in (xmin,xmax):
        box('Roof side rail',(x,y+.85,(zmin+zmax)/2),(.09,.09,zmax-zmin),teal,.014)
        if LOD<2:
            count=math.ceil((zmax-zmin)/(1.1 if LOD==0 else 2.5))+1
            for i in range(count):box('Roof side post',(x,y+.43,zmin+(zmax-zmin)*i/(count-1)),(.07,.84,.07),ivory,.01)

def build_lod(level):
    global LOD,ALL,BOXES,BUFFERS
    LOD=level;ALL=[];BOXES=[];BUFFERS={}
    collection=bpy.data.collections.new('Hotel LOD '+str(level));bpy.context.scene.collection.children.link(collection)
    # Ground shells keep a real entrance cut: no geometry in either walkable void.
    for side in (-1,1):
        box('Ground wing',(side*18.62,1.9,0),(12.72,3.8,30.4),ivory)
        for xx in (15,18.6,22.2):window(side*xx,1.85,15.2,2.8,2.85,accent=True)
    box('Rear ground shell',(0,1.9,-9.95),(24.52,3.8,12.5),ivory)
    # Front colonnade outside the 4.2 m clear entrance path, under the upper hotel.
    for x in (-11.5,-7.2,7.2,11.5):
        box('Ground arcade column',(x,1.9,13.2),(.55,3.8,.55),ivory,.045)
        box('Column stone base',(x,.15,13.2),(.72,.3,.72),stone,.025)
        box('Column capital',(x,3.6,13.2),(.8,.25,.8),ivory,.025)
    tiers=[(-8.95,8.95,39.25,14.4),(-19.9,-8.95,32.535,14.7),(8.95,19.9,32.535,14.7),(-24.98,-19.9,25.535,14.7),(19.9,24.98,25.535,14.7)]
    for xmin,xmax,top,front in tiers:
        height=top-3.8;centre=(xmin+xmax)/2
        box('Solid upper tower',(centre,3.8+height/2,(front-15.2)/2),(xmax-xmin,height,front+15.2),ivory)
        # Broad, quiet floor ribbons align every window and balcony.
        floors=[5.45+i*3.5 for i in range(10) if 5.45+i*3.5+1.25<top]
        for yy in floors:
            if LOD<2:box('Front floor ribbon',(centre,yy-1.6,front+.045),(xmax-xmin,.16,.09),stone)
            columns=max(1,round((xmax-xmin)/2.65))
            for i in range(columns):
                xx=xmin+(i+.5)*(xmax-xmin)/columns
                window(xx,yy,front,1.75,2.3,accent=(abs(xx)>9 and abs(xx)<18 and i==columns//2))
                window(xx,yy,-15.2,1.75,2.3)
            if xmax-xmin>7:
                balcony(centre,yy-1.3,front+.02,min(5.1,xmax-xmin-1.2))
        roof=top+.27
        extra=.54 if abs(centre)>20 else .34
        box('Roof cornice',(centre,roof,0),(xmax-xmin+extra,.44,31.98),ivory,.04)
        box('Roof brass edge',(centre,roof+.25,0),(xmax-xmin+extra,.065,32.02),brass,.012)
        # The central crown is taller; all other tiers have terrace balustrades.
        if abs(centre)>9:rooftop_rail(xmin+.16,xmax-.16,-15.72,15.72,roof+.3)
    # The tower crown finishes exactly at 40 m with a lightly stepped cornice.
    box('Central crown',(0,39.88,0),(18.52,.24,32.6),ivory,.04)
    box('Crown turquoise reveal',(0,39.62,0),(18.28,.10,32.14),teal,.01)
    # Exterior side rooms continue the rhythm, including exposed stepped shoulders.
    for side in (-1,1):
        for outer,height,front in ((24.98,25.535,14.7),(19.9,32.535,14.7),(8.95,39.25,14.4)):
            lower=3.8 if outer==24.98 else (26.7 if outer==19.9 else 33.9)
            for yy in [5.45+i*3.5 for i in range(10)]:
                if yy-1.2<lower or yy+1.2>height:continue
                for zz in (-12.5,-8.4,-4.3,-.2,3.9,8,12.1):window(side*outer,yy,zz,1.9,2.3,side=True)
    # Strong corner piers remain perfectly vertical and stop short of roof cornices.
    if LOD<2:
        for side in (-1,1):
            for xx,top in ((24.88,25.535),(19.74,32.535),(8.76,39.25)):
                box('Facade corner pier',(side*xx,(top+3.8)/2,14.8),(.19,top-3.8,.22),ivory,.016)
    # One editable mesh per material gives five stable game draw-call groups.
    for material_slot in materials:
        data=BUFFERS.get(material_slot.name)
        if not data:continue
        mesh=bpy.data.meshes.new(f'Hotel LOD{level} {material_slot.name} geometry');mesh.from_pydata(data['verts'],[],data['faces']);mesh.update()
        mesh.materials.append(material_slot);uv=mesh.uv_layers.new(name='Architectural UV')
        for poly in mesh.polygons:
            normal=poly.normal
            axes=(1,2) if abs(normal.x)>.5 else ((0,2) if abs(normal.y)>.5 else (0,1))
            for loop in poly.loop_indices:
                v=mesh.vertices[mesh.loops[loop].vertex_index].co
                uv.data[loop].uv=(v[axes[0]]*.18,v[axes[1]]*.18)
        o=bpy.data.objects.new(f'Hotel_LOD{level}_{material_slot.name}',mesh);collection.objects.link(o);ALL.append(o)
        if level==0 and material_slot!=glass:
            bpy.context.view_layer.objects.active=o;o.select_set(True)
            mod=o.modifiers.new('Subtle straight edge bevel','BEVEL');mod.width=.012;mod.segments=1
            bpy.ops.object.modifier_apply(modifier=mod.name)
            mod=o.modifiers.new('Weighted architectural normals','WEIGHTED_NORMAL');mod.keep_sharp=True
            bpy.ops.object.modifier_apply(modifier=mod.name);o.select_set(False)
    return collection,list(ALL),list(BOXES)

def bounds(objs):
    points=[o.matrix_world@Vector(v) for o in objs for v in o.bound_box]
    # Unity axes, preserving numerical metre reporting.
    pts=[(p.x,p.z,-p.y) for p in points]
    return {'min':[min(p[i] for p in pts) for i in range(3)],'max':[max(p[i] for p in pts) for i in range(3)]}

def intersects(a,b):return all(a['min'][i]<b['max'][i]-1e-5 and a['max'][i]>b['min'][i]+1e-5 for i in range(3))
voids={'lobby':{'min':[-12.25,0,-2.25],'max':[12.25,3.7,10.25]},'vestibule':{'min':[-2.1,0,9.8],'max':[2.1,3.7,21]}}
report={'normalization':SCALE,'model_space_units':'metres before GLB normalization','model_front_axis':'+Z Unity / -Y Blender','material_names':[m.name for m in materials],'empty_required_volumes_metres':voids,'lods':[]}
collections=[]
for level in range(3):
    collection,objects,boxes=build_lod(level);collections.append(collection)
    collisions={name:[b['name'] for b in boxes if intersects(b,v)] for name,v in voids.items()}
    if any(collisions.values()):raise RuntimeError('Entrance obstruction '+str(collisions))
    real_bounds=bounds(objects)
    # Join by material for five draw-call groups while keeping metre master geometry.
    joined=[]
    for material_slot in materials:
        group=[o for o in objects if o.data.materials[0]==material_slot]
        if not group:continue
        bpy.ops.object.select_all(action='DESELECT')
        for o in group:o.select_set(True)
        bpy.context.view_layer.objects.active=group[0]
        bpy.ops.object.join();o=bpy.context.object;o.name=f'Hotel_LOD{level}_{material_slot.name}'
        joined.append(o)
    copies=[]
    bpy.ops.object.select_all(action='DESELECT')
    for original in joined:
        copy=original.copy();copy.data=original.data.copy();bpy.context.collection.objects.link(copy)
        # Bake full world coordinates into normalized vertices, retaining ground pivot.
        for vertex in copy.data.vertices:vertex.co=(original.matrix_world@vertex.co)/SCALE
        copy.matrix_world.identity();copy.select_set(True);copies.append(copy)
    bpy.context.view_layer.objects.active=copies[0]
    target=OUT/f'resort_hotel_clean_reception_lod{level}.glb'
    bpy.ops.export_scene.gltf(filepath=str(target),export_format='GLB',use_selection=True,export_materials='EXPORT',export_texcoords=True,export_normals=True,export_animations=False,export_cameras=False,export_lights=False)
    count=0
    for o in copies:o.data.calc_loop_triangles();count+=len(o.data.loop_triangles)
    report['lods'].append({'level':level,'path':str(target),'triangles':count,'render_meshes':len(copies),'bounds_metres':real_bounds,'bounds_normalized':bounds(copies),'entrance_obstructions':collisions,'glb_bytes':target.stat().st_size})
    for o in copies:bpy.data.objects.remove(o,do_unlink=True)
    collection.hide_render=level!=0;collection.hide_viewport=level!=0

# Editable master retains real architectural metre dimensions and every LOD.
scene=bpy.context.scene
scene['UnityNormalization']=SCALE
scene['EntranceVoidVerified']='Lobby x +/-12.25 y0..3.7 z-2.25..10.25; vestibule x +/-2.1 y0..3.7 z9.8..21'
scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=1600;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
scene.world=bpy.data.worlds.new('Soft resort sky');scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.35,.43,.52,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value=.7
bpy.ops.object.light_add(type='SUN',location=(-40,-40,60));bpy.context.object.rotation_euler=(.5,-.35,-.35);bpy.context.object.data.energy=2.8;bpy.context.object.data.angle=math.radians(12)
bpy.ops.object.light_add(type='AREA',location=(20,-45,30));bpy.context.object.data.energy=2200;bpy.context.object.data.shape='DISK';bpy.context.object.data.size=25
bpy.ops.object.camera_add(location=(-55,-72,46));cam=bpy.context.object;scene.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=76
cam.rotation_euler=(Vector((0,0,19))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.clip_end=500
scene.view_settings.view_transform='AgX'
bpy.ops.wm.save_as_mainfile(filepath=str(MASTER/'hotel_clean.blend'))
(ROOT/'Logs/hotel-clean-build.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
scene.render.filepath=str(REVIEW/'exterior.png');bpy.ops.render.render(write_still=True)
cam.location=(0,-27,2.0);cam.data.type='PERSP';cam.data.lens=24
cam.rotation_euler=(Vector((0,-3,2))-cam.location).to_track_quat('-Z','Y').to_euler()
scene.render.filepath=str(REVIEW/'entrance_void.png');bpy.ops.render.render(write_still=True)
print('HOTEL_CLEAN_COMPLETE '+json.dumps(report))
