"""Check the exported lobby cut against actual mesh triangles at every LOD."""
from pathlib import Path
import bpy,json
from mathutils import Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[2]
scale=40/.780731201
report=[]
for level in range(3):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(ROOT/('Assets/_Game/Art/Props/resort_hotel_reception_lod'+str(level)+'.glb')))
    trees=[]
    for o in bpy.context.scene.objects:
        if o.type=='MESH':
            trees.append(BVHTree.FromPolygons([o.matrix_world@v.co for v in o.data.vertices],[list(f.vertices) for f in o.data.polygons],all_triangles=True))
    hits=[];rays=0
    for x in (-.65,0,.65):
        for height in (1,1.7,2.4):
            for dx,dh in ((0,0),(-.3,0),(.3,0),(0,-.3),(0,.3)):
                start=Vector(((x+dx)/scale,-(17.5+4)/scale,(height+dh)/scale));rays+=1
                for tree in trees:
                    hit=tree.ray_cast(start,Vector((0,1,0)),16/scale)
                    if hit[0] is not None:hits.append({'x':x+dx,'height':height+dh,'distance':hit[3]*scale})
    report.append({'lod':level,'rays':rays,'hits':hits,'pass':not hits})
(ROOT/'Logs/hotel-entrance-blender-check.json').write_text(json.dumps(report,indent=2))
if any(not r['pass'] for r in report):raise RuntimeError('An exported hotel LOD blocks the entrance')
print('PASS: reception corridor open in actual triangles at all three LODs')
