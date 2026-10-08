"""Rebuild smooth vinyl normals; retain the painted face and UV layout."""
from pathlib import Path
import bpy
import bmesh
import numpy as np
from mathutils import Vector, Matrix

root = Path(__file__).resolve().parents[2]
path = root / 'Assets/_Game/Art/Meshy/flamingo.glb'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(root / 'ArtSource/Meshy/raw/flamingo_raw.glb'))
# The generated atlas baked triangle lighting into the pink vinyl. Normalize
# just that paint colour, preserving the yellow/black beak and white eye print.
for image in list(bpy.data.images):
    if image.size[0] < 64:
        continue
    pixels = np.empty(len(image.pixels), dtype=np.float32)
    image.pixels.foreach_get(pixels)
    rgba = pixels.reshape(-1, 4)
    pink = (rgba[:, 0] > rgba[:, 1] * 1.25) & (rgba[:, 2] > rgba[:, 1] * 1.12) & (rgba[:, 0] > 0.15)
    print('Vinyl paint pixels:', image.name, np.count_nonzero(pink), '/', len(rgba))
    if np.count_nonzero(pink) > len(rgba) * 0.5:
        rgba[pink, :3] = np.median(rgba[pink, :3], axis=0)
        clean = bpy.data.images.new('FlamingoCleanVinyl', width=image.size[0], height=image.size[1], alpha=True)
        clean.colorspace_settings.name = image.colorspace_settings.name
        clean.pixels.foreach_set(pixels)
        clean.update()
        clean.pack()
        for material in bpy.data.materials:
            if material.use_nodes:
                for node in material.node_tree.nodes:
                    if node.type == 'TEX_IMAGE' and node.image == image:
                        node.image = clean
for obj in list(bpy.context.scene.objects):
    if obj.type != 'MESH':
        continue
    mesh = obj.data
    obj.name = mesh.name = 'Body'
    mesh.transform(obj.matrix_world)
    obj.matrix_world = Matrix.Identity(4)
    lo = Vector(tuple(min(v.co[i] for v in mesh.vertices) for i in range(3)))
    hi = Vector(tuple(max(v.co[i] for v in mesh.vertices) for i in range(3)))
    centre = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
    scale = 1.90185 / (hi.y - lo.y)
    for vertex in mesh.vertices:
        vertex.co = (vertex.co - centre) * scale
    # Shared positions imported as split UV vertices should share smooth normals.
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=0.0001)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    if mesh.has_custom_normals:
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    for face in mesh.polygons:
        face.use_smooth = True
    for edge in mesh.edges:
        edge.use_edge_sharp = False
    mesh.update()
    print('Flamingo topology before polish:', len(mesh.vertices), 'vertices', len(mesh.polygons), 'faces')
    # Weld the source BEFORE decimation. Decimating its disconnected UV islands
    # independently creates physical cracks which no material polish can hide.
    if len(mesh.polygons) > 18000:
        bpy.context.view_layer.objects.active = obj
        decimate = obj.modifiers.new('Connected vinyl surface', 'DECIMATE')
        decimate.ratio = 18000 / len(mesh.polygons)
        decimate.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=decimate.name)
    for material in mesh.materials:
        if not material or not material.use_nodes:
            continue
        for node in material.node_tree.nodes:
            if node.type == 'BSDF_PRINCIPLED':
                for link in list(node.inputs['Normal'].links):
                    material.node_tree.links.remove(link)
                for socket in ('Metallic', 'Roughness'):
                    for link in list(node.inputs[socket].links):
                        material.node_tree.links.remove(link)
                node.inputs['Roughness'].default_value = 0.06
                node.inputs['Metallic'].default_value = 0.0
bpy.ops.export_scene.gltf(filepath=str(path), export_format='GLB', export_yup=True,
                          export_normals=True, export_tangents=True)
print('Flamingo normals smoothed; baked faceted normal map removed.')
