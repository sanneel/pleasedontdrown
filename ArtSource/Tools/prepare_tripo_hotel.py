"""Create geometry-only game LODs from the original Tripo FBX; retain its Unity textures."""
import json
import math
from pathlib import Path
import bpy
import bmesh
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'Assets/TripoModels/apartment_building_3d_model/apartment_building_3d_model.fbx'
OUT = ROOT / 'Assets/_Game/Art/Props'
REPORT = ROOT / 'Logs/tripo-hotel-lods.json'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(SOURCE))
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
points = [o.matrix_world @ Vector(p) for o in meshes for p in o.bound_box]
minimum = Vector(tuple(min(p[i] for p in points) for i in range(3)))
maximum = Vector(tuple(max(p[i] for p in points) for i in range(3)))
centre = (minimum + maximum) / 2
report = {'source_bounds_blender': {'min': list(minimum), 'max': list(maximum)}, 'lods': []}
# Bake the source's axes and ground pivot once. glTF handles Blender -> Unity axis conversion.
for o in meshes:
    matrix = o.matrix_world.copy()
    for v in o.data.vertices:
        v.co = matrix @ v.co - Vector((centre.x, centre.y, minimum.z))
    o.matrix_world.identity()
    o.data.materials.clear()
for index, ratio in enumerate((0.12, 0.035, 0.01)):
    copies = []
    for o in meshes:
        copy = o.copy(); copy.data = o.data.copy(); bpy.context.collection.objects.link(copy)
        bpy.context.view_layer.objects.active = copy
        modifier = copy.modifiers.new('Preserve hotel silhouette', 'DECIMATE')
        modifier.ratio = ratio; modifier.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        # Relax tiny ripples on broad surfaces, protecting open boundaries, UV seams,
        # hard architectural corners and every silhouette extreme.
        bm = bmesh.new(); bm.from_mesh(copy.data); bm.normal_update()
        protected = set()
        for edge in bm.edges:
            if not edge.is_manifold or edge.seam or edge.calc_face_angle(0) > math.radians(30):
                protected.update(edge.verts)
        extremes = [(min(v.co[i] for v in bm.verts), max(v.co[i] for v in bm.verts)) for i in range(3)]
        for vert in bm.verts:
            if any(abs(vert.co[i] - limit) < 1e-5 for i in range(3)
                   for limit in extremes[i]):
                protected.add(vert)
        smooth = [v for v in bm.verts if v not in protected]
        original = {v: v.co.copy() for v in smooth}
        bmesh.ops.smooth_vert(bm, verts=smooth, factor=.18, use_axis_x=True, use_axis_y=True, use_axis_z=True)
        for vert, point in original.items():
            delta = vert.co - point
            if delta.length > .0008: vert.co = point + delta.normalized() * .0008
        max_move = max(((v.co - p).length for v, p in original.items()), default=0)
        bm.normal_update()
        for face in bm.faces: face.smooth = True
        for edge in bm.edges: edge.smooth = edge.is_manifold and edge.calc_face_angle(0) < math.radians(35)
        bm.to_mesh(copy.data); bm.free()
        normal = copy.modifiers.new('Clean architectural normals', 'WEIGHTED_NORMAL')
        normal.keep_sharp = True; normal.weight = 50
        bpy.ops.object.modifier_apply(modifier=normal.name)
        copy.data.validate(clean_customdata=False)
        copy.data.update()
        copy.data.calc_loop_triangles()
        copies.append(copy)
    bpy.ops.object.select_all(action='DESELECT')
    for o in copies: o.select_set(True)
    target = OUT / f'resort_hotel_lod{index}.glb'
    bpy.ops.export_scene.gltf(filepath=str(target), export_format='GLB', use_selection=True,
                             export_materials='NONE', export_texcoords=True, export_normals=True,
                             export_animations=False, export_cameras=False, export_lights=False)
    if index == 0:
        # Keep an editable, textured Blender master as well as the game exports.
        texture_folder = SOURCE.parent / 'apartment_building_3d_model.fbm'
        facade = bpy.data.materials.new('Polished resort facade'); facade.use_nodes = True
        nodes = facade.node_tree.nodes; links = facade.node_tree.links
        shader = nodes.get('Principled BSDF'); shader.inputs['Roughness'].default_value = .68
        base = nodes.new('ShaderNodeTexImage')
        base.image = bpy.data.images.load(str(texture_folder / 'apartment_building_3d_model_basecolor.JPEG'))
        links.new(base.outputs['Color'], shader.inputs['Base Color'])
        bump_image = nodes.new('ShaderNodeTexImage')
        bump_image.image = bpy.data.images.load(str(texture_folder / 'apartment_building_3d_model_normal.PNG'))
        bump_image.image.colorspace_settings.name = 'Non-Color'
        normal_map = nodes.new('ShaderNodeNormalMap'); normal_map.inputs['Strength'].default_value = .18
        links.new(bump_image.outputs['Color'], normal_map.inputs['Color']); links.new(normal_map.outputs['Normal'], shader.inputs['Normal'])
        for copy in copies: copy.data.materials.append(facade)
        for original_model in meshes: original_model.hide_set(True); original_model.hide_render = True
        master_dir = ROOT / 'ArtSource/Resort'; master_dir.mkdir(parents=True, exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=str(master_dir / 'hotel_polished.blend'))
        for original_model in meshes: original_model.hide_set(False); original_model.hide_render = False
    report['lods'].append({'path': str(target.relative_to(ROOT)),
                           'triangles': sum(len(o.data.loop_triangles) for o in copies),
                           'polish_max_movement_source_units': max_move})
    # The central hotel's existing 24 x 12m playable lobby must remain visibly empty.
    # Uniform scale fits this building to 40m tall, with its pivot at hotel-local z=-4m.
    scale = 40 / (maximum.z - minimum.z)
    cut_count = 0
    for o in copies:
        bm = bmesh.new(); bm.from_mesh(o.data)
        remove = []
        for face in bm.faces:
            p = [(v.co.x * scale, v.co.z * scale, -v.co.y * scale - 4) for v in face.verts]
            lobby = (min(v[0] for v in p) < 12.25 and max(v[0] for v in p) > -12.25 and
                min(v[1] for v in p) < 3.7 and max(v[1] for v in p) > -.1 and
                min(v[2] for v in p) < 6.25 and max(v[2] for v in p) > -6.25)
            # The imported facade extends beyond the old lobby. Clear a vestibule all
            # the way to the outside, rather than leaving an opaque wall in front of it.
            entrance = (min(v[0] for v in p) < 2.1 and max(v[0] for v in p) > -2.1 and
                min(v[1] for v in p) < 3.7 and max(v[1] for v in p) > -.1 and
                min(v[2] for v in p) < 17 and max(v[2] for v in p) > 5.8)
            if lobby or entrance:
                remove.append(face)
        cut_count += len(remove)
        bmesh.ops.delete(bm, geom=remove, context='FACES')
        bm.to_mesh(o.data); bm.free(); o.data.validate(clean_customdata=False); o.data.update()
    target = OUT / f'resort_hotel_reception_lod{index}.glb'
    bpy.ops.export_scene.gltf(filepath=str(target), export_format='GLB', use_selection=True,
                             export_materials='NONE', export_texcoords=True, export_normals=True,
                             export_animations=False, export_cameras=False, export_lights=False)
    report['lods'][-1]['reception_faces_removed'] = cut_count
    for o in copies: bpy.data.objects.remove(o, do_unlink=True)
REPORT.write_text(json.dumps(report, indent=2), encoding='utf-8')
print('[TripoHotel] ' + json.dumps(report))
