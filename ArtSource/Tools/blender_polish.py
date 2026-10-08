"""Blender asset inspection and conservative surface polish. See --help."""
import argparse
import json
import math
import shutil
from pathlib import Path
import sys
import bpy
import bmesh
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Screenshots/Review/BlenderPolish'
OUT.mkdir(parents=True, exist_ok=True)
SOURCE = ROOT / 'ArtSource/Polish'

def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(path))
    bpy.context.view_layer.update()
    return [o for o in bpy.context.scene.objects if o.type == 'MESH']

def render(meshes, path, face=False, quarter=False):
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 24
    scene.cycles.use_denoising = True
    scene.render.resolution_x = 700
    scene.render.resolution_y = 700 if face else 420
    scene.render.resolution_percentage = 100
    scene.world = bpy.data.worlds.new('Studio')
    scene.world.use_nodes = True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value = (.18,.21,.26,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value = .7
    vs = [o.matrix_world @ v.co for o in meshes for v in o.data.vertices]
    lo = Vector(tuple(min(v[i] for v in vs) for i in range(3)))
    hi = Vector(tuple(max(v[i] for v in vs) for i in range(3)))
    if face:
        rig = next(o for o in scene.objects if o.type == 'ARMATURE')
        head = rig.matrix_world @ rig.data.bones['Head'].head_local
        lo.z = head.z
        center = Vector((0, (lo.y+hi.y)*.5, (lo.z+hi.z)*.5))
        scale = (hi.z-lo.z)*1.25
        d = Vector((.38,-1,.08) if quarter else (0,-1,0)).normalized()
    else:
        center = (lo+hi)*.5
        scale = max(hi-lo)*1.15
        d = Vector((-.35,-1,.35)).normalized()
    cam = bpy.data.objects.new('ReviewCamera', bpy.data.cameras.new('ReviewCamera'))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = scale
    cam.location = center + d*4
    cam.rotation_euler = (-d).to_track_quat('-Z','Y').to_euler()
    for name, offset, power, size in [('Key',(-2,-3,4),350,4),('Fill',(3,-1,1),100,3),('Rim',(0,3,3),250,3)]:
        light = bpy.data.objects.new(name,bpy.data.lights.new(name,'AREA'))
        scene.collection.objects.link(light)
        light.location = center + Vector(offset)
        light.rotation_euler = (center-light.location).to_track_quat('-Z','Y').to_euler()
        light.data.energy = power
        light.data.shape = 'DISK'
        light.data.size = size
    scene.view_settings.view_transform = 'AgX'
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)

def face_polish(meshes):
    """Fair the front of the head without changing topology, UVs or skin weights."""
    ob = next(o for o in meshes if o.name.startswith('Body'))
    me = ob.data
    rig = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
    head = rig.matrix_world @ rig.data.bones['Head'].head_local
    co = np.array([tuple(v.co) for v in me.vertices])
    world = np.array([tuple(ob.matrix_world @ v.co) for v in me.vertices])
    # The GLB splits UV borders. Treat coincident points as one only for calculations.
    _, inverse = np.unique(np.round(co, 6), axis=0, return_inverse=True)
    count = np.bincount(inverse)
    unique = np.zeros((len(count),3))
    np.add.at(unique,inverse,co)
    unique /= count[:,None]
    origin = unique.copy()
    edges = np.array([tuple(e.vertices) for e in me.edges])
    edges = np.unique(np.sort(inverse[edges],axis=1),axis=0)
    edges = edges[edges[:,0] != edges[:,1]]
    a,b = edges.T
    degrees = np.bincount(np.r_[a,b],minlength=len(count))
    h = world[:,2].max()-head.z
    z = (world[:,2]-head.z)/h
    front = np.clip((head.y-world[:,1])/.035,0,1)
    weight = np.clip(z/.12,0,1)*np.clip((.82-z)/.18,0,1)*front
    weights = np.zeros(len(count))
    np.add.at(weights,inverse,weight)
    weights /= count
    for _ in range(5):
        neighbors = np.zeros_like(unique)
        np.add.at(neighbors,a,unique[b])
        np.add.at(neighbors,b,unique[a])
        delta = neighbors/np.maximum(degrees,1)[:,None]-unique
        unique += delta * (.34*weights)[:,None]
        move = unique-origin
        length = np.linalg.norm(move,axis=1)
        unique = origin + move*np.minimum(1,.0018/np.maximum(length,1e-12))[:,None]
    me.vertices.foreach_set('co',unique[inverse].astype(np.float32).ravel())
    me.update()
    # Angle-weighted normals joined across UV seams, localized to the head.
    me.calc_loop_triangles()
    tri = np.array([tuple(t.vertices) for t in me.loop_triangles])
    pts = unique[inverse]
    normals = np.zeros_like(unique)
    for k in range(3):
        v0,v1,v2 = tri[:,k],tri[:,(k+1)%3],tri[:,(k+2)%3]
        e1,e2 = pts[v1]-pts[v0],pts[v2]-pts[v0]
        e1 /= np.maximum(np.linalg.norm(e1,axis=1),1e-12)[:,None]
        e2 /= np.maximum(np.linalg.norm(e2,axis=1),1e-12)[:,None]
        angle = np.arccos(np.clip((e1*e2).sum(1),-1,1))
        n = np.cross(e1,e2)
        n /= np.maximum(np.linalg.norm(n,axis=1),1e-12)[:,None]
        np.add.at(normals,inverse[v0],n*angle[:,None])
    normals /= np.maximum(np.linalg.norm(normals,axis=1),1e-12)[:,None]
    original = np.array([tuple(n.vector) for n in me.corner_normals])
    loops = np.array([l.vertex_index for l in me.loops])
    blend = np.clip(z/.12,0,1)[loops,None]
    result = original*(1-blend)+normals[inverse[loops]]*blend
    result /= np.maximum(np.linalg.norm(result,axis=1),1e-12)[:,None]
    me.normals_split_custom_set(result.tolist())
    return {'max_vertex_move_m':float(np.linalg.norm(unique-origin,axis=1).max()),'vertices':len(co),'triangles':len(tri)}

def gun_polish(meshes):
    stats=[]
    for ob in meshes:
        me = ob.data
        extent = max(ob.dimensions)
        bm = bmesh.new(); bm.from_mesh(me)
        bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=extent*1e-6)
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
        bm.to_mesh(me); bm.free()
        if me.has_custom_normals:
            me.normals_split_custom_set([(0,0,0)]*len(me.loops))
        bpy.context.view_layer.objects.active = ob
        for p in me.polygons: p.use_smooth=True
        me.set_sharp_from_angle(angle=math.radians(48))
        bevel = ob.modifiers.new('Fine edge highlights','BEVEL')
        bevel.width = extent*.0012
        bevel.segments=2
        bevel.limit_method='ANGLE'
        bevel.angle_limit=math.radians(38)
        bevel.use_clamp_overlap=True
        bpy.ops.object.modifier_apply(modifier=bevel.name)
        normal = ob.modifiers.new('Stable broad surfaces','WEIGHTED_NORMAL')
        normal.keep_sharp=True
        normal.weight=35
        bpy.ops.object.modifier_apply(modifier=normal.name)
        for mat in ob.data.materials:
            bsdf=mat.node_tree.nodes.get('Principled BSDF')
            if not bsdf: continue
            if mat.name == 'Paint':
                for link in list(bsdf.inputs['Base Color'].links): mat.node_tree.links.remove(link)
                bsdf.inputs['Base Color'].default_value=(.105,.122,.15,1)
            name=mat.name.lower()
            bsdf.inputs['Metallic'].default_value=.5 if name in ('paint','blued','steel') else 0
            bsdf.inputs['Roughness'].default_value={'paint':.48,'blued':.46,'steel':.34,'wood':.64,'wooddark':.7,'polymer':.7,'rubber':.85}.get(name,.65)
        ob.data.calc_loop_triangles()
        stats.append({'vertices':len(ob.data.vertices),'triangles':len(ob.data.loop_triangles)})
    return stats

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--inspect', action='store_true')
    ap.add_argument('--apply', action='store_true')
    ap.add_argument('--variants', action='store_true')
    args = ap.parse_args(sys.argv[sys.argv.index('--')+1:])
    paths = list((ROOT/'Assets/_Game/Art/Characters').glob('tourist*.glb'))
    paths = [p for p in paths if ('_v' in p.stem) == args.variants]
    if not args.variants:
        paths += list((ROOT/'Assets/_Game/Art/Weapons').glob('*.glb'))
    report = []
    for path in paths:
        original = SOURCE/'Originals'/path.name
        if args.apply:
            original.parent.mkdir(parents=True,exist_ok=True)
            if not original.exists(): shutil.copy2(path,original)
        meshes = load(original if original.exists() else path)
        if args.apply:
            result=face_polish(meshes) if path.stem.startswith('tourist') else gun_polish(meshes)
            asset_objects=[o for o in bpy.context.scene.objects if o.type not in ('LIGHT','CAMERA')]
            bpy.ops.object.select_all(action='DESELECT')
            for o in asset_objects: o.select_set(True)
            bpy.ops.export_scene.gltf(filepath=str(path),export_format='GLB',use_selection=True,export_animations=True)
            report.append({'file':path.name,'result':result})
            if '_v' not in path.stem:
                render(meshes,OUT/(path.stem+'_after.png'),path.stem.startswith('tourist'))
                for img in bpy.data.images:
                    if img.source=='FILE' and not img.packed_file: img.pack()
                bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/(path.stem+'_polished.blend')))
            continue
        report.append({'file':path.name, 'objects':[{'name':o.name,'vertices':len(o.data.vertices),'polygons':len(o.data.polygons),'smooth':sum(p.use_smooth for p in o.data.polygons),'materials':[m.name for m in o.data.materials]} for o in meshes]})
        render(meshes,OUT/(path.stem+'_before.png'),path.stem.startswith('tourist'))
    report_name = 'variants_report.json' if args.variants else ('polish_report.json' if args.apply else 'inspection.json')
    (OUT/report_name).write_text(json.dumps(report,indent=2))

if __name__ == '__main__':
    main()
