from pathlib import Path
import bpy, bmesh, json
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(ROOT/'ArtSource/Resort/TripoBar/source.glb'))
o=next(o for o in bpy.context.scene.objects if o.type=='MESH');bpy.context.view_layer.objects.active=o
d=o.modifiers.new('Analysis', 'DECIMATE');d.ratio=.10;bpy.ops.object.modifier_apply(modifier=d.name)
bm=bmesh.new();bm.from_mesh(o.data);bm.verts.ensure_lookup_table()
visited=set();parts=[]
for v in bm.verts:
    if v.index in visited:continue
    stack=[v];visited.add(v.index);points=[]
    while stack:
        p=stack.pop();points.append(p.co.copy())
        for e in p.link_edges:
            q=e.other_vert(p)
            if q.index not in visited:visited.add(q.index);stack.append(q)
    if len(points)>30:
        parts.append({'vertices':len(points),'min':[min(p[i] for p in points) for i in range(3)],'max':[max(p[i] for p in points) for i in range(3)]})
parts.sort(key=lambda p:p['vertices'],reverse=True)
(ROOT/'Logs/tripo-bar-parts.json').write_text(json.dumps(parts,indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'ArtSource/Resort/TripoBar/analysis.blend'))
