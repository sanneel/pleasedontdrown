"""Build the editable Blender blink comparison after MeshyCharacters.BakeFaceRepairAll.

Blender --background --python ArtSource/Tools/blender_face_review.py
Frames: 1=open, 12=previous closed texture, 24=repaired closed texture.
"""
import json
import re
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Screenshots/Review/FaceRepair'
SOURCE = ROOT / 'ArtSource/FaceRepair'
SOURCE.mkdir(parents=True, exist_ok=True)
sys.path.insert(0, str(Path(__file__).parent))
from blender_polish import render


def main():
    log = (OUT / 'final2.log').read_text(encoding='utf-8', errors='replace')
    matches = re.findall(r'\[FaceRepairReview\] seed=(\d+) body=(\d+) asset=([^\r\n]+)', log)
    assert len(matches) == 8, 'Run the tourist bake and review first.'
    bpy.ops.wm.read_factory_settings(use_empty=True)
    empty = bpy.context.scene
    report = []
    for index, (seed, body, asset) in enumerate(matches):
        name = Path(asset).stem
        scene = bpy.data.scenes.new(f'{index+1:02d} - {name}')
        bpy.context.window.scene = scene
        bpy.ops.import_scene.gltf(filepath=str(ROOT / f'Assets/_Game/Art/Characters/{name}.glb'))
        meshes = [o for o in scene.objects if o.type == 'MESH']
        after = bpy.data.images.load(str(ROOT / f'Assets/_Game/Avatar/Bodies/{name}_eyes_closed.png'))
        before = bpy.data.images.load(str(OUT / f'Originals/{name}_eyes_closed.png'))
        assert tuple(after.size) == tuple(before.size)
        assert np.isfinite(np.asarray(after.pixels[:])).all()
        for ob in meshes:
            for mat in ob.data.materials:
                nodes, links = mat.node_tree.nodes, mat.node_tree.links
                bsdf = nodes.get('Principled BSDF')
                if not bsdf or not bsdf.inputs['Base Color'].links:
                    continue
                original = bsdf.inputs['Base Color'].links[0].from_socket
                for socket in ('Normal', 'Metallic', 'Roughness'):
                    for link in list(bsdf.inputs[socket].links):
                        links.remove(link)
                bsdf.inputs['Metallic'].default_value = 0
                bsdf.inputs['Roughness'].default_value = .72
                old = nodes.new('ShaderNodeTexImage'); old.image = before; old.label = 'Previous blink'
                new = nodes.new('ShaderNodeTexImage'); new.image = after; new.label = 'Repaired blink'
                blink = nodes.new('ShaderNodeMixRGB'); blink.label = 'Open / closed'
                fix = nodes.new('ShaderNodeMixRGB'); fix.label = 'Previous / repaired'
                links.new(original, blink.inputs[1]); links.new(old.outputs['Color'], blink.inputs[2])
                links.new(blink.outputs[0], fix.inputs[1]); links.new(new.outputs['Color'], fix.inputs[2])
                links.new(fix.outputs[0], bsdf.inputs['Base Color'])
                for frame, blink_value, fix_value in ((1, 0, 0), (12, 1, 0), (24, 1, 1)):
                    blink.inputs[0].default_value = blink_value
                    blink.inputs[0].keyframe_insert('default_value', frame=frame)
                    fix.inputs[0].default_value = fix_value
                    fix.inputs[0].keyframe_insert('default_value', frame=frame)
                old.location = (-700, -200); new.location = (-440, -420)
                blink.location = (-200, 180); fix.location = (20, 180); bsdf.location = (240, 180)
        scene.frame_end = 24
        scene.timeline_markers.new('OPEN', frame=1)
        scene.timeline_markers.new('BEFORE', frame=12)
        scene.timeline_markers.new('REPAIRED', frame=24)
        scene['Instructions'] = 'Frame 1: open eyes. Frame 12: previous blink. Frame 24: repaired blink.'
        scene.frame_set(24)
        render(meshes, OUT / f'blender_{seed}_after.png', face=True)
        # Closer facial framing than the full ponytail-based inspection camera.
        body_text = (ROOT / asset).read_text(encoding='utf-8-sig')
        eye_height = float(re.search(r'  EyeHeight: ([\d.e+-]+)', body_text)[1])
        center = Vector((0, 0, eye_height - .015))
        scene.camera.location = center + Vector((0, -4, 0))
        scene.camera.data.ortho_scale = .43
        scene.render.resolution_x = scene.render.resolution_y = 600
        scene.render.filepath = str(OUT / f'blender_{seed}_after.png')
        bpy.ops.render.render(write_still=True)
        scene.frame_set(12)
        scene.render.filepath = str(OUT / f'blender_{seed}_before.png')
        bpy.ops.render.render(write_still=True)
        scene.frame_set(24)
        for ob in scene.objects:
            if ob.type == 'ARMATURE':
                ob.hide_set(True, view_layer=scene.view_layers[0])
        report.append({'seed': int(seed), 'body': int(body), 'source': name, 'texture': list(after.size)})
    bpy.data.scenes.remove(empty)
    bpy.context.window.scene = next(s for s in bpy.data.scenes if 'sporty_v02' in s.name)
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type == 'VIEW_3D':
                area.spaces.active.region_3d.view_perspective = 'CAMERA'
                area.spaces.active.shading.type = 'MATERIAL'
                area.spaces.active.overlay.show_overlays = False
    text = bpy.data.texts.new('READ ME - face repair')
    text.write('Choose a character in the Scene selector.\nFrame 1: open eyes\nFrame 12: previous blink\nFrame 24: repaired blink\n\nTextures are packed. Source rigs and face geometry are preserved.\nThe game texture generator is MeshyCharacters.ClosedEyes.cs.\nRebuild with PLEASE DON\'T DROWN > Repair tourist eyelid textures.\n')
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / 'Face_Repair.blend'))
    (OUT / 'blender_validation.json').write_text(json.dumps(report, indent=2))
    # Contact sheet: each row is a reference character, before then after.
    sheet = np.ones((8*240, 480, 4), dtype=np.float32)
    for row, item in enumerate(report):
        for col, state in enumerate(('before', 'after')):
            img = bpy.data.images.load(str(OUT / f'blender_{item["seed"]}_{state}.png'))
            img.scale(240, 240)
            pixels = np.asarray(img.pixels[:], dtype=np.float32).reshape(240, 240, 4)
            y = (7-row)*240
            sheet[y:y+240, col*240:(col+1)*240] = pixels
    result = bpy.data.images.new('Before and after', width=480, height=1920)
    result.pixels.foreach_set(sheet.ravel())
    result.filepath_raw = str(OUT / 'before_after.png')
    result.file_format = 'PNG'; result.save()
    print('FACE_REVIEW_VALIDATED', len(report))


if __name__ == '__main__':
    main()
