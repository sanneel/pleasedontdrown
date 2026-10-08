"""Identify the latest transfer without changing either imported asset."""
import bpy, hashlib, json
from pathlib import Path
from mathutils import Vector
root = Path(__file__).resolve().parents[2]
report = []
for folder in ('apartment_building_3d_model', 'apartment_building_3d_model_1'):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(root / 'Assets/TripoModels' / folder / (folder + '.fbx')))
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    digest = hashlib.sha256()
    for o in meshes:
        digest.update(str(len(o.data.vertices)).encode())
        coords = [0.0] * (len(o.data.vertices) * 3)
        o.data.vertices.foreach_get('co', coords)
        import array
        digest.update(array.array('f', coords).tobytes())
    points = [o.matrix_world @ Vector(p) for o in meshes for p in o.bound_box]
    report.append(dict(asset=folder, vertices=sum(len(o.data.vertices) for o in meshes),
                       faces=sum(len(o.data.polygons) for o in meshes), geometry_hash=digest.hexdigest(),
                       min=[min(p[i] for p in points) for i in range(3)],
                       max=[max(p[i] for p in points) for i in range(3)]))
(root / 'Logs/latest-tripo-identity.json').write_text(json.dumps(report, indent=2))
