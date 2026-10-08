"""Render one textured studio view of each imported GLB for audit contact sheets.

blender -b --factory-startup -P ArtSource/Tools/glb_contact_sheet.py
"""
import json
import sys
from pathlib import Path

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Screenshots/Review/GLB'
OUT.mkdir(parents=True, exist_ok=True)
AUDIT = json.loads((ROOT / 'Docs/GLB-Polish-Audit.json').read_text())['assets']
paths = [ROOT / a['path'] for a in AUDIT if a['path'].startswith('Assets/')]
selected = sys.argv[sys.argv.index('--only') + 1].split(',') if '--only' in sys.argv else None
if selected:
    paths = [p for p in paths if p.stem in selected]
results = []


def render(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(path))
    # Rigged imports also create an invisible Icosphere bone-shape helper.
    # Including it in bounds makes every character appear tiny in the sheet.
    meshes = [ob for ob in bpy.context.scene.objects if ob.type == 'MESH' and ob.visible_get()]
    if not meshes:
        raise ValueError('no mesh objects')
    corners = [ob.matrix_world @ Vector(corner) for ob in meshes for corner in ob.bound_box]
    lo = Vector(tuple(min(c[i] for c in corners) for i in range(3)))
    hi = Vector(tuple(max(c[i] for c in corners) for i in range(3)))
    size = hi - lo
    center = (lo + hi) * 0.5
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'TEXTURE'
    scene.display.shading.show_cavity = True
    scene.render.resolution_x = 440
    scene.render.resolution_y = 440
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    scene.world = bpy.data.worlds.new('Background')
    scene.world.color = (0.7, 0.75, 0.78)
    cam = bpy.data.objects.new('Camera', bpy.data.cameras.new('Camera'))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = max(size.x, size.y, size.z, 0.01) * 1.45
    direction = Vector((0.65, -1.0, 0.42)).normalized()
    cam.location = center + direction * (max(size) * 4 + 2)
    cam.rotation_euler = (-direction).to_track_quat('-Z', 'Y').to_euler()
    output = OUT / (path.parent.name + '_' + path.stem + '.png')
    scene.render.filepath = str(output)
    bpy.ops.render.render(write_still=True)
    return {'path': path.relative_to(ROOT).as_posix(), 'image': output.relative_to(ROOT).as_posix(),
            'world_size': [round(v, 4) for v in size]}


for number, path in enumerate(paths, 1):
    try:
        result = render(path)
    except Exception as exc:
        result = {'path': path.relative_to(ROOT).as_posix(), 'error': str(exc)}
    results.append(result)
    print('GLB_REVIEW', number, '/', len(paths), result, flush=True)

if selected:
    old = json.loads((OUT / 'render_results.json').read_text())
    changed = {r['path'] for r in results}
    results = [r for r in old if r['path'] not in changed] + results
(OUT / 'render_results.json').write_text(json.dumps(results, indent=2) + '\n')
