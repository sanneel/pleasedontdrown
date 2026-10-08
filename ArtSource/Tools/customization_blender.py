"""Author, save and export the lifeguard accessory collection in Blender.
blender -b --factory-startup -P ArtSource/Tools/customization_blender.py
Coordinates below are Unity head-local metres; Blender uses (x,-z,y).
The editable .blend and lossless game mesh data share the same evaluated geometry.
"""
import bpy, bmesh, json, math
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'ArtSource/Customization'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
materials = {}
pieces = {}
current = None

def B(v): return (v[0], -v[2], v[1])
def U(v): return {'x': round(v[0],7), 'y': round(v[2],7), 'z': round(-v[1],7)}

def material(name, color, region=0):
    m=bpy.data.materials.new(name); m.diffuse_color=(*color,1); m.use_nodes=True
    node=m.node_tree.nodes.get('Principled BSDF')
    node.inputs['Base Color'].default_value=(*color,1)
    node.inputs['Roughness'].default_value=.6 if name!='Lens' else .18
    m['tint_region']=region; materials[name]=m
    return m

material('Hat fabric',(1,1,1),1); material('Hat seam',(.72,.72,.72),1)
material('Hair',(1,1,1),2); material('Hair grooves',(.64,.64,.64),2)
material('Straw',(.82,.63,.30)); material('Straw edge',(.62,.43,.19))
material('Dark',(.045,.052,.063)); material('Lens',(.035,.12,.16))
material('Gold',(.95,.64,.16)); material('Silver',(.65,.72,.77))
material('Pink',(.96,.23,.47)); material('Aqua',(.12,.72,.7))
material('Ivory',(.96,.94,.81)); material('Ruby',(.85,.07,.13))

def begin(name):
    global current
    current=bpy.data.collections.new(name); scene.collection.children.link(current); pieces[name]=[]

def keep(o,mat):
    for c in list(o.users_collection): c.objects.unlink(o)
    current.objects.link(o); o.data.materials.append(materials[mat]); pieces[current.name].append(o)
    return o

def surface(name,vs,fs,mat,smooth=True):
    mesh=bpy.data.meshes.new(name);mesh.from_pydata([B(v) for v in vs],[],fs);mesh.update()
    bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
    for p in mesh.polygons:p.use_smooth=smooth
    o=bpy.data.objects.new(name,mesh);scene.collection.objects.link(o)
    return keep(o,mat)

def lathe(name,profile,mat='Hat fabric',cz=.015,segments=48,warp=0):
    vs=[];fs=[]
    for r,y in profile:
        for k in range(segments):
            a=2*math.pi*k/segments
            vs.append((r*math.cos(a),y+warp*(r/.46)**3*math.cos(2*a),cz+r*math.sin(a)))
    for j in range(len(profile)-1):
        for k in range(segments):
            a=j*segments+k;b=j*segments+(k+1)%segments
            fs.append((a,b,b+segments,a+segments))
    return surface(name,vs,fs,mat)

def tube(name,points,r,mat,cyclic=False):
    c=bpy.data.curves.new(name,'CURVE');c.dimensions='3D';c.resolution_u=1;c.bevel_depth=r;c.bevel_resolution=2
    sp=c.splines.new('POLY');sp.points.add(len(points)-1)
    for p,co in zip(sp.points,points):p.co=(*B(co),1)
    sp.use_cyclic_u=cyclic;c.use_fill_caps=True
    o=bpy.data.objects.new(name,c);scene.collection.objects.link(o);return keep(o,mat)

def ring(y,r,mat='Hat seam',thick=.005,cz=.015):
    return tube('Piped edge',[(r*math.cos(a),y,cz+r*math.sin(a)) for a in [2*math.pi*k/64 for k in range(64)]],thick,mat,True)

def ball(name,at,radii,mat):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=20,ring_count=12,location=B(at))
    o=bpy.context.object;o.name=name;o.scale=(radii[0],radii[2],radii[1])
    for p in o.data.polygons:p.use_smooth=True
    return keep(o,mat)

def box(name,at,size,mat,bevel=.005):
    bpy.ops.mesh.primitive_cube_add(size=1,location=B(at));o=bpy.context.object;o.name=name;o.scale=(size[0],size[2],size[1])
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if bevel:
        mod=o.modifiers.new('Soft tailored edges','BEVEL');mod.width=bevel;mod.segments=3
        o.modifiers.new('Weighted corner normals','WEIGHTED_NORMAL')
    return keep(o,mat)

def crown(mat='Hat fabric'):
    lathe('Six-panel crown',[(0,.56),(.105,.552),(.205,.51),(.282,.442),(.307,.389),(.302,.378),(.289,.391),(.27,.436),(.196,.495),(.10,.538),(0,.543)],mat)

def brim(radius=.43,warp=0,mat='Hat fabric'):
    lathe('Shaped brim',[(.292,.391),(.34,.383),(radius,.37),(radius,.362),(.34,.373),(.292,.38)],mat,warp=warp)

def peak(back=False):
    sign=-1 if back else 1
    vs=[];n=32
    for zbase,width,y in [(.19,.275,.392),(.49,.21,.372)]:
        for i in range(n+1):
            t=-1+2*i/n
            vs.append((t*width,y+.028*t*t,.015+sign*(zbase+.05*(1-t*t))))
    fs=[(i,i+1,n+i+2,n+i+1) for i in range(n)]
    o=surface('Curved bill',vs,fs,'Hat fabric');m=o.modifiers.new('Bill thickness','SOLIDIFY');m.thickness=.009
    tube('Bill stitching',[(v[0]*.93,v[1]+.006,v[2]-.009*sign) for v in vs[n+1:]],.002,'Hat seam')

for style in ['Cap','CapBackwards','BucketHat','Visor','StrawHat','Headband','Bandana','Beanie','Cowboy','Pirate','Crown','PartyHat','Headphones']:
    begin('hat_'+style)
    if style in ('Cap','CapBackwards'):
        crown();peak(style=='CapBackwards');ring(.393,.303)
        ball('Top button',(0,.566,.015),(.018,.009,.018),'Hat seam')
        for k in range(6):
            a=k*math.pi/3
            tube('Panel stitch',[(r*math.cos(a),y,.015+r*math.sin(a)) for r,y in [(.02,.562),(.10,.555),(.20,.515),(.28,.448),(.306,.395)]],.002,'Hat seam')
    elif style=='BucketHat':
        lathe('Bucket crown',[(0,.571),(.21,.57),(.257,.539),(.302,.404),(.302,.391),(.291,.391),(.246,.531),(.20,.558),(0,.558)])
        lathe('Drooping brim',[(.299,.404),(.34,.378),(.394,.331),(.398,.321),(.385,.326),(.331,.367),(.299,.392)])
        ring(.407,.302);ring(.336,.389,thick=.002)
    elif style=='Visor':
        lathe('Visor band',[(.30,.426),(.305,.423),(.307,.389),(.299,.386),(.296,.419),(.30,.426)]);peak()
    elif style=='StrawHat':
        lathe('Woven crown',[(0,.582),(.18,.578),(.245,.537),(.286,.403),(.286,.39),(.274,.392),(.23,.528),(.173,.565),(0,.567)],'Straw')
        brim(.475,.014,'Straw');ring(.409,.29,'Hat fabric',.016)
        for r in [.33,.37,.41,.451,.474]:ring(.37,r,'Straw edge',.0017)
    elif style=='Headband':
        lathe('Sports headband',[(.302,.426),(.307,.421),(.31,.383),(.303,.378),(.294,.385),(.296,.419),(.302,.426)])
        box('Rescue patch',(0,.4,.326),(.057,.026,.008),'Ivory',.005)
    elif style=='Bandana':
        crown();ring(.397,.306,thick=.01)
        ball('Tied knot',(0,.406,-.302),(.039,.025,.026),'Hat seam')
        for side in [-1,1]:
            surface('Cloth tail',[(0,.40,-.315),(side*.085,.36,-.36),(side*.065,.245,-.335),(side*.015,.29,-.325)],[(0,1,2,3)],'Hat fabric').modifiers.new('Cloth thickness','SOLIDIFY').thickness=.005
        for x in [-.12,-.06,0,.06,.12]:ball('Printed dot',(x,.45,.287),(.008,.008,.002),'Ivory')
    elif style=='Beanie':
        lathe('Knitted crown',[(0,.644),(.10,.63),(.21,.562),(.294,.442),(.304,.397),(.294,.39),(.277,.437),(.197,.551),(.09,.614),(0,.625)])
        lathe('Folded cuff',[(.302,.449),(.315,.44),(.319,.394),(.31,.382),(.297,.389),(.299,.441),(.302,.449)],'Hat seam')
        for k in range(36):
            a=k*math.tau/36;tube('Knit rib',[(r*math.cos(a),y,.015+r*math.sin(a)) for r,y in [(.312,.397),(.314,.43),(.303,.447)]],.002,'Hat fabric')
        ball('Pom pom',(0,.655,.015),(.052,.055,.052),'Hat seam')
    elif style=='Cowboy':
        lathe('Pinched crown',[(0,.63),(.16,.634),(.234,.573),(.28,.407),(.278,.395),(.267,.402),(.221,.567),(.15,.621),(0,.617)])
        brim(.465,.072);ring(.415,.282,'Dark',.013)
        ball('Concho',(0,.423,.308),(.021,.025,.005),'Gold')
    elif style=='Pirate':
        crown('Dark')
        n=72;vs=[]
        for radius in [.28,.47]:
            for k in range(n):
                a=math.tau*k/n;lift=.115*(.5+.5*math.cos(3*a)) if radius>.3 else 0
                vs.append((radius*math.cos(a),.4+lift,.015+radius*math.sin(a)))
        o=surface('Tricorn folded brim',vs,[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],'Hat fabric');o.modifiers.new('Leather thickness','SOLIDIFY').thickness=.012
        tube('Gold braid',vs[n:],.006,'Gold',True)
        ball('Skull badge',(0,.464,.32),(.036,.039,.014),'Ivory')
        for x in [-.013,.013]:ball('Badge socket',(x,.47,.334),(.008,.009,.004),'Dark')
    elif style=='Crown':
        lathe('Crown band',[(.294,.404),(.306,.404),(.306,.45),(.294,.45),(.294,.404)],'Hat fabric')
        n=48;vs=[]
        for row in range(2):
            for k in range(n):
                a=math.tau*k/n;y=.437 if row==0 else .47+.12*(.5+.5*math.cos(6*a))
                vs.append((.301*math.cos(a),y,.015+.301*math.sin(a)))
        o=surface('Six crown points',vs,[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],'Hat fabric');o.modifiers.new('Crown thickness','SOLIDIFY').thickness=.012
        ring(.411,.309,'Gold',.009)
        for k in range(6):
            a=math.tau*k/6;ball('Jewel',(.315*math.cos(a),.45,.015+.315*math.sin(a)),(.018,.024,.018),'Ruby' if k%2 else 'Aqua')
    elif style=='PartyHat':
        crown();lathe('Party cone',[(.19,.51),(.018,.855),(0,.875),(.012,.85),(.182,.51)],'Hat fabric')
        for y,r in [(.57,.16),(.65,.12),(.73,.08)]:ring(y,r,'Gold',.009)
        ball('Party pom',(0,.88,.015),(.034,.035,.034),'Pink')
    else:
        tube('Padded headphone arch',[(.325*math.cos(a),.255+.34*math.sin(a),.015) for a in [math.pi*k/48 for k in range(49)]],.029,'Hat fabric')
        for side in [-1,1]:
            ball('Ear cushion',(side*.312,.255,.015),(.038,.095,.072),'Dark')
            ball('Ear shell',(side*.348,.255,.015),(.03,.083,.067),'Hat fabric')
            ball('Ear logo',(side*.376,.255,.015),(.005,.027,.027),'Gold')

def outline(style):
    if style=='Stars':return [(math.cos(math.pi/2+i*math.pi/5)*(.108 if i%2==0 else .064),math.sin(math.pi/2+i*math.pi/5)*(.108 if i%2==0 else .064)) for i in range(10)]
    if style=='Hearts':
        return [(16*math.sin(t)**3*.0067,(13*math.cos(t)-5*math.cos(2*t)-2*math.cos(3*t)-math.cos(4*t))*.007) for t in [math.tau*i/64 for i in range(64)]]
    if style in ('Sport','Sunglasses'):
        return [(math.copysign(abs(math.cos(t))**.5,math.cos(t))*.109,math.copysign(abs(math.sin(t))**.5,math.sin(t))*.080) for t in [math.tau*i/48 for i in range(48)]]
    if style=='Aviators':return [(math.cos(t)*.109,math.sin(t)*(.108 if math.sin(t)<0 else .079)) for t in [math.tau*i/64 for i in range(64)]]
    return [(math.cos(t)*.108,math.sin(t)*.108) for t in [math.tau*i/64 for i in range(64)]]

for style in ['Sunglasses','Round','Hearts','Goggles','Aviators','Stars','Sport']:
    begin('glasses_'+style)
    frame='Pink' if style=='Hearts' else 'Gold' if style=='Aviators' else 'Aqua' if style in ('Goggles','Sport') else 'Dark'
    for side,cx in [(-1,-.1227),(1,.1176)]:
        shape=outline(style);pts=[(cx+x,.302+y,.335) for x,y in shape]
        tube('Sculpted lens rim',pts,.007 if style!='Goggles' else .014,frame,True)
        if style in ('Sunglasses','Aviators','Sport'):
            surface('Tinted lens',[(cx,.302,.337)]+[(cx+x*.96,.302+y*.96,.337) for x,y in shape],[(0,i+1,(i+1)%len(shape)+1) for i in range(len(shape))],'Lens')
            tube('Lens glint',[(cx-.04,.345,.342),(cx+.016,.36,.342)],.002,'Silver')
        tube('Curved temple',[(cx+side*.108,.316,.329),(side*.289,.31,.245),(side*.31,.275,.066),(side*.303,.241,.018)],.005,frame)
    tube('Nose bridge',[(-.018,.316,.34),(0,.327,.349),(.013,.316,.34)],.005,frame)
    if style=='Goggles':
        tube('Goggle strap',[(.31*math.cos(t),.275,.015+.3*math.sin(t)) for t in [math.pi+math.pi*i/48 for i in range(49)]],.012,'Dark')

begin('face_Mustache')
for side in [-1,1]:
    tube('Curled moustache',[(side*x,y,z) for x,y,z in [(0,.169,.291),(.032,.174,.30),(.072,.167,.30),(.103,.18,.291),(.109,.20,.285)]],.013,'Hair')
begin('face_Beard')
# Sculpted jawline tufts leave the mouth/teeth open, instead of one large chin sphere.
for side in [-1,1]:
    for i in range(5):
        x=side*(.032+.026*i);y=.01+.016*i;z=.19-.012*i
        ball('Jaw tuft',(x,y,z),(.036,.054,.028),'Hair')
    tube('Jaw seam',[(side*.035,-.025,.194),(side*.08,.012,.172),(side*.128,.048,.149)],.002,'Hair grooves')
begin('face_Stubble')
for x in [-.017,0,.017]:ball('Goatee tuft',(x,.025,.20),(.015,.035,.017),'Hair')
begin('teeth_Bucky')
for side in [-1,1]:box('Rounded front tooth',(side*.026,.091,.248),(.046,.087,.023),'Ivory',.007)

# Lift brims clear of the eyes. Headphones and fabric bands hug the original skull.
for name,objects in pieces.items():
    if name.startswith('hat_') and name not in ('hat_Headphones','hat_Headband','hat_Bandana'):
        for o in objects:o.location.z += .055
bpy.context.view_layer.update()

# Evaluate Blender bevels, curves and normals once; game meshes need no runtime modelling.
deps=bpy.context.evaluated_depsgraph_get();result=[]
for name,objects in pieces.items():
    data={'name':name,'vertices':[],'normals':[],'triangles':[],'colors':[],'regions':[]}
    for o in objects:
        evaluated=o.evaluated_get(deps);mesh=evaluated.to_mesh();mesh.calc_loop_triangles()
        normal_matrix=o.matrix_world.to_3x3().inverted().transposed()
        for tri in mesh.loop_triangles:
            mat=o.data.materials[mesh.polygons[tri.polygon_index].material_index]
            col=mat.diffuse_color
            ids=[]
            for li in tri.loops:
                ids.append(len(data['vertices']))
                v=mesh.vertices[mesh.loops[li].vertex_index]
                data['vertices'].append(U(o.matrix_world@v.co))
                data['normals'].append(U((normal_matrix@mesh.corner_normals[li].vector).normalized()))
                data['colors'].append(dict(zip(('r','g','b','a'),[round(c,5) for c in col])))
                data['regions'].append(int(mat.get('tint_region',0)))
            data['triangles']+= ids
        evaluated.to_mesh_clear()
    result.append(data)
(OUT/'wearables.json').write_text(json.dumps({'pieces':result},separators=(',',':')))
for name,objects in pieces.items():
    for o in objects:o.hide_set(name not in ('hat_Cowboy','glasses_Sunglasses'))
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.region_3d.view_distance=1.5
            area.spaces.active.region_3d.view_location=(0,0,.35)
            area.spaces.active.shading.color_type='MATERIAL'
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'lifeguard_customization.blend'))
print('[Customization] Authored',len(result),'pieces;',sum(len(p['triangles'])//3 for p in result),'triangles total')
