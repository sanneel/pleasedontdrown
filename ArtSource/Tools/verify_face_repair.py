"""Check Blender eyebrow exports retain topology, rig transforms and normalised weights."""
import json
from pathlib import Path
import bpy
import numpy as np
from mathutils.kdtree import KDTree

ROOT = Path(__file__).resolve().parents[2]


def inspect(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(path))
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    triangles = 0
    for ob in meshes:
        ob.data.calc_loop_triangles()
        triangles += len(ob.data.loop_triangles)
        assert all(np.isfinite(tuple(v.co)).all() for v in ob.data.vertices)
        if ob.vertex_groups:
            assert max(abs(sum(g.weight for g in v.groups)-1) for v in ob.data.vertices) < .002
    bones = {b.name: np.array(o.matrix_world @ b.matrix_local)
             for o in bpy.context.scene.objects if o.type == 'ARMATURE' for b in o.data.bones}
    body = next(o for o in meshes if o.name.startswith('Body'))
    image = body.data.materials[0].node_tree.nodes.get('Principled BSDF').inputs['Base Color'].links[0].from_node.image
    pixels = np.asarray(image.pixels[:], dtype=np.float32).reshape(-1, 4)
    positions = np.array([tuple(body.matrix_world @ v.co) for v in body.data.vertices])
    positions = positions[np.lexsort(positions.T)]
    return triangles, bones, pixels, positions


results = []
for original in sorted((ROOT / 'ArtSource/FaceRepair/Originals').glob('*.glb')):
    before = inspect(original)
    after = inspect(ROOT / 'Assets/_Game/Art/Characters' / original.name)
    assert before[0] == after[0], original
    assert before[1].keys() == after[1].keys(), original
    error = max(np.abs(before[1][key]-after[1][key]).max() for key in before[1])
    assert error < .0001, (original, error)
    assert before[2].shape == after[2].shape, original
    # glTF may split/reorder coincident UV-seam vertices on export. Compare the actual surface positions.
    surface_error = 0.0
    for source, target in ((before[3], after[3]), (after[3], before[3])):
        tree = KDTree(len(target))
        for i, point in enumerate(target):
            tree.insert(point, i)
        tree.balance()
        surface_error = max(surface_error, max(tree.find(point)[2] for point in source))
    assert surface_error < .00001, (original, surface_error)
    changed = np.max(np.abs(before[2]-after[2]), axis=1) > .008
    assert 100 < changed.sum() < 10000, (original, changed.sum())
    results.append({'file': original.name, 'triangles': after[0], 'changed_texels': int(changed.sum()),
                    'max_bone_matrix_error': float(error), 'max_surface_position_error': surface_error, 'passed': True})
(ROOT / 'Screenshots/Review/FaceRepair/export_validation.json').write_text(json.dumps(results, indent=2))
print('VERIFIED_BROW_EXPORTS', len(results))
