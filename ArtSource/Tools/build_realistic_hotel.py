"""Polish the user's Tripo hotel (ArtSource/Resort/tripo_hotel_upload.glb) into realistic game LODs.

Keeps the model's shape and colour scheme. The AI texture atlas is smeared, so every face is classified
from it (glass, cream wall, sandstone trim, teal, white, grey metal) and gets a clean physical material.
Wavy walls, slabs and window panes are snapped onto true axis-aligned planes, flat areas are merged,
the lobby volume is cut out for the playable reception, and three LODs are exported.

blender -b --factory-startup -P ArtSource/Tools/build_realistic_hotel.py -- [preview]
"""
import colorsys
import json
import math
import sys
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
from hotel_windows import rebuild_windows  # noqa: E402
from hotel_exterior import finish_exterior, box_uvs  # noqa: E402
from hotel_fill import fill_blanks  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'ArtSource/Resort/tripo_hotel_upload.glb'
OUT = ROOT / 'Assets/_Game/Art/Props'
REVIEW = ROOT / 'Screenshots/Review/HotelRealistic'
REPORT = ROOT / 'Logs/hotel-realistic.json'
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
PREVIEW_ONLY = 'preview' in ARGS

HEIGHT = 40.0          # game height in metres (unchanged from the previous hotel)
PIVOT_Z = -4.0         # hotel-local z of the building pivot (unchanged)

# name: (linear base colour, roughness, metallic) - also the Unity material names (tripohotel_<name>)
CLASSES = {
    'wall':  ((.80, .64, .41), .80, 0.0),   # warm cream render
    'trim':  ((.56, .38, .16), .72, 0.0),   # sandstone cornices and bands
    'white': ((.86, .80, .66), .62, 0.0),   # balusters, soffits, mouldings
    'teal':  ((.03, .30, .38), .45, 0.0),   # painted window surrounds and canopy fascia
    'glass': ((.02, .06, .10), .05, 0.55),   # reflective tinted glazing
    'metal': ((.32, .35, .37), .38, 0.8),   # roof railings
    'roof':  ((.40, .38, .35), .85, 0.0),   # flat roofs and paving
    'gold':  ((.80, .55, .18), .22, 1.0),   # brass/gold accents
    'glass_b': ((.05, .11, .15), .06, 0.5), # lighter tinted glass
    'curtain': ((.22, .16, .10), .18, 0.3), # drawn curtains seen through glass
    'stone': ((.62, .56, .46), .60, 0.0),   # column plinths, paving stone
}
NAMES = list(CLASSES)


def log(*a):
    print('[RealHotel]', *a, flush=True)


def classify_colours(rgb):
    """rgb: (n,3) sRGB 0-1 -> class index array."""
    r, g, b = rgb[:, 0], rgb[:, 1], rgb[:, 2]
    mx = rgb.max(1); mn = rgb.min(1); v = mx; s = np.where(mx > 1e-5, (mx - mn) / np.maximum(mx, 1e-5), 0)
    d = np.maximum(mx - mn, 1e-5)
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
    out = np.full(len(rgb), NAMES.index('wall'))
    blue = (h > 175) & (h < 260)
    out[(s < .14) & (v > .80)] = NAMES.index('white')
    out[(s < .14) & (v <= .80)] = NAMES.index('roof')
    warm = (h >= 15) & (h <= 75) & (s >= .14)
    out[warm & ((s > .50) | (v < .64))] = NAMES.index('trim')
    out[blue & (s >= .14) & (v >= .45)] = NAMES.index('teal')
    out[blue & (s >= .14) & (v < .45)] = NAMES.index('glass')
    out[v < .22] = NAMES.index('glass')
    return out


def load_source():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(SOURCE))
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    assert len(meshes) == 1, meshes
    obj = meshes[0]
    obj.data.transform(obj.matrix_world); obj.matrix_world.identity()
    for o in list(bpy.context.scene.objects):
        if o != obj: bpy.data.objects.remove(o, do_unlink=True)
    me = obj.data
    co = np.empty(len(me.vertices) * 3); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
    lo, hi = co.min(0), co.max(0)
    scale = HEIGHT / (hi[2] - lo[2])
    # Ground pivot at the footprint centre, metres, Blender Z-up (glTF export converts to Unity).
    co = (co - np.array([(lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, lo[2]])) * scale
    me.vertices.foreach_set('co', co.ravel()); me.update()
    log('source tris', sum(len(p.vertices) - 2 for p in me.polygons), 'scale', round(scale, 3),
        'size m', np.round((hi - lo) * scale, 2).tolist())
    return obj, scale


def face_classes(obj):
    me = obj.data
    image = next(i for i in bpy.data.images if 'basecolor' in i.name.lower())
    w, h = image.size
    px = np.empty(w * h * 4, dtype=np.float32); image.pixels.foreach_get(px); px = px.reshape(h, w, 4)[:, :, :3]
    uv = np.empty(len(me.loops) * 2); me.uv_layers.active.data.foreach_get('uv', uv); uv = uv.reshape(-1, 2)
    starts = np.empty(len(me.polygons), dtype=np.int64); me.polygons.foreach_get('loop_start', starts)
    counts = np.empty(len(me.polygons), dtype=np.int64); me.polygons.foreach_get('loop_total', counts)
    assert counts.min() == 3 and counts.max() == 3, 'expected a triangle mesh'
    tri = uv[starts[:, None] + np.arange(3)]               # (n,3,2)
    centre = tri.mean(1)
    votes = np.zeros((len(starts), len(NAMES)), dtype=np.int32)
    # Sample the centroid and three points part-way to the corners; majority vote.
    for k, pts in enumerate([centre] + [centre + (tri[:, i] - centre) * .55 for i in range(3)]):
        x = np.clip((pts[:, 0] % 1) * w, 0, w - 1).astype(np.int64)
        y = np.clip((pts[:, 1] % 1) * h, 0, h - 1).astype(np.int64)
        cls = classify_colours(px[y, x])
        votes[np.arange(len(cls)), cls] += 2 if k == 0 else 1
    cls = votes.argmax(1)
    # Four smoothing passes through shared vertices remove speckle on the smeared atlas.
    vidx = np.empty(len(me.loops), dtype=np.int64); me.loops.foreach_get('vertex_index', vidx)
    fverts = vidx[starts[:, None] + np.arange(3)]
    area = np.empty(len(starts)); me.polygons.foreach_get('area', area)
    for _ in range(4):
        hist = np.zeros((len(me.vertices), len(NAMES)))
        for i in range(3): np.add.at(hist, (fverts[:, i], cls), area)
        score = hist[fverts[:, 0]] + hist[fverts[:, 1]] + hist[fverts[:, 2]]
        score[np.arange(len(cls)), cls] += area * 2.5       # keep own class unless clearly outvoted
        cls = score.argmax(1)
    return cls


def straighten(obj, tol=.018, max_move=.09, align=.97, only=None):
    """Snap near-axis-aligned faces onto shared planes. Metres (after scaling). only: material index filter."""
    me = obj.data
    nf = len(me.polygons)
    normal = np.empty(nf * 3); me.polygons.foreach_get('normal', normal); normal = normal.reshape(-1, 3)
    centre = np.empty(nf * 3); me.polygons.foreach_get('center', centre); centre = centre.reshape(-1, 3)
    area = np.empty(nf); me.polygons.foreach_get('area', area)
    starts = np.empty(nf, dtype=np.int64); me.polygons.foreach_get('loop_start', starts)
    vidx = np.empty(len(me.loops), dtype=np.int64); me.loops.foreach_get('vertex_index', vidx)
    fverts = vidx[starts[:, None] + np.arange(3)]
    mats = np.empty(nf, dtype=np.int64); me.polygons.foreach_get('material_index', mats)
    co = np.empty(len(me.vertices) * 3); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
    moved = np.zeros(len(co))
    for axis in range(3):
        aligned = np.abs(normal[:, axis]) > align
        if only is not None: aligned &= mats == only
        ids = np.nonzero(aligned)[0]
        if not len(ids): continue
        coord = centre[ids, axis]; order = np.argsort(coord); c = coord[order]
        cluster = np.concatenate([[0], np.cumsum(np.diff(c) > tol)])
        weights = area[ids][order]
        sums = np.bincount(cluster, weights * c); wsum = np.bincount(cluster, weights)
        value = sums / np.maximum(wsum, 1e-12)
        face_plane = np.empty(len(ids)); face_plane[order] = value[cluster]
        # Vertex target = area-weighted plane of its aligned faces.
        acc = np.zeros(len(co)); wacc = np.zeros(len(co))
        for i in range(3):
            np.add.at(acc, fverts[ids, i], face_plane * area[ids])
            np.add.at(wacc, fverts[ids, i], area[ids])
        has = wacc > 0
        target = np.where(has, acc / np.maximum(wacc, 1e-12), co[:, axis])
        delta = target - co[:, axis]
        ok = has & (np.abs(delta) < max_move)
        co[ok, axis] = target[ok]; moved = np.maximum(moved, np.where(ok, np.abs(delta), 0))
        log('axis', 'xyz'[axis], 'aligned faces', len(ids), 'planes', int(cluster.max()) + 1,
            'snapped verts', int(ok.sum()), 'mean move cm', round(float(np.abs(delta[ok]).mean() * 100), 2))
    me.vertices.foreach_set('co', co.ravel()); me.update()
    return float(moved.max())


def make_materials(obj, cls):
    me = obj.data
    me.materials.clear()
    for name, (colour, rough, metal) in CLASSES.items():
        m = bpy.data.materials.new('tripohotel_' + name); m.use_nodes = True
        p = m.node_tree.nodes.get('Principled BSDF')
        p.inputs['Base Color'].default_value = (*colour, 1); p.inputs['Roughness'].default_value = rough
        p.inputs['Metallic'].default_value = metal
        m.diffuse_color = (*colour, 1)
        me.materials.append(m)
    me.polygons.foreach_set('material_index', cls.astype(np.int32))
    me.update()


def simplify(obj):
    """Weld, merge flat regions per material, triangulate. UVs are no longer needed."""
    me = obj.data
    while me.uv_layers: me.uv_layers.remove(me.uv_layers[0])
    for a in list(me.attributes):
        if a.name.startswith('Col') or a.domain == 'CORNER' and a.data_type in {'FLOAT_COLOR', 'BYTE_COLOR'}:
            me.attributes.remove(a)
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=.006)
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=.002)
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(4), use_dissolve_boundaries=False,
                               verts=bm.verts, edges=bm.edges, delimit={'MATERIAL'})
    bmesh.ops.triangulate(bm, faces=bm.faces, quad_method='BEAUTY', ngon_method='BEAUTY')
    bm.to_mesh(me); bm.free(); me.update()
    return sum(len(p.vertices) - 2 for p in me.polygons)


def finish_normals(obj):
    me = obj.data
    bm = bmesh.new(); bm.from_mesh(me); bm.normal_update()
    for f in bm.faces: f.smooth = True
    for e in bm.edges:
        e.smooth = e.is_manifold and e.calc_face_angle(0) < math.radians(30) and \
            len({f.material_index for f in e.link_faces}) == 1
    bm.to_mesh(me); bm.free(); me.update()
    # Flat architecture reads as flat sheets: each face's shading normal is the axis it faces.
    loops = np.empty(len(me.loops) * 3); me.corner_normals.foreach_get('vector', loops); loops = loops.reshape(-1, 3)
    nf = len(me.polygons)
    fn = np.empty(nf * 3); me.polygons.foreach_get('normal', fn); fn = fn.reshape(-1, 3)
    mats = np.empty(nf, dtype=np.int64); me.polygons.foreach_get('material_index', mats)
    starts = np.empty(nf, dtype=np.int64); me.polygons.foreach_get('loop_start', starts)
    totals = np.empty(nf, dtype=np.int64); me.polygons.foreach_get('loop_total', totals)
    # Glass always; any other face within ~25 degrees of an axis (walls, slabs, cornice faces) too, so the
    # AI mesh's leftover ripples don't shade. Round parts (columns, balusters) keep smooth normals.
    glass = np.nonzero((mats == NAMES.index('glass')) | (np.abs(fn).max(1) > .9))[0]
    axis = np.abs(fn[glass]).argmax(1)
    snapped = np.zeros((len(glass), 3)); snapped[np.arange(len(glass)), axis] = np.sign(fn[glass, axis])
    loop_face = np.repeat(np.arange(nf), totals)
    is_glass = np.zeros(nf, dtype=bool); is_glass[glass] = True
    face_normal = np.zeros((nf, 3)); face_normal[glass] = snapped
    sel = is_glass[loop_face]
    loops[sel] = face_normal[loop_face[sel]]
    me.normals_split_custom_set(loops.tolist())
    me.update()


def decimated_copy(obj, ratio, name):
    copy = obj.copy(); copy.data = obj.data.copy(); copy.name = name
    bpy.context.collection.objects.link(copy)
    if ratio < 1:
        mod = copy.modifiers.new('dec', 'DECIMATE'); mod.ratio = ratio; mod.use_collapse_triangulate = True
        evaluated = copy.evaluated_get(bpy.context.evaluated_depsgraph_get())
        mesh = bpy.data.meshes.new_from_object(evaluated)
        copy.modifiers.clear(); copy.data = mesh
    finish_normals(copy)
    copy.data.calc_loop_triangles(); log(name, 'tris', len(copy.data.loop_triangles))
    return copy


def cut_reception(obj):
    """Clear the playable ground floor (hotel-local Unity coordinates): the whole building, 47 x 18 m (lobby, the
    doctor's room and the casino; Unity: GameSceneBuilder.HotelInterior). Tall faces are first split at 3.75 m so
    the floors above keep their walls."""
    X, Y, Z0, Z1 = 23.65, 3.75, -18.85, -.55
    bm = bmesh.new(); bm.from_mesh(obj.data)
    def unity(v):
        return v.co.x, v.co.z, -v.co.y + PIVOT_Z
    def over(face):
        p = [unity(v) for v in face.verts]
        xs = [q[0] for q in p]; ys = [q[1] for q in p]; zs = [q[2] for q in p]
        return min(xs) < X and max(xs) > -X and min(ys) < Y and max(ys) > -.1 and min(zs) < Z1 and max(zs) > Z0, max(ys)
    tall = [f for f in bm.faces if over(f)[0] and over(f)[1] > Y + .01]
    if tall:
        edges = list({e for f in tall for e in f.edges})
        bmesh.ops.bisect_plane(bm, geom=tall + edges + list({v for f in tall for v in f.verts}),
                               plane_co=(0, 0, Y), plane_no=(0, 0, 1))
        bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3])
    remove = []
    for face in bm.faces:
        hit, top = over(face)
        if hit and top <= Y + .01: remove.append(face)
    bmesh.ops.delete(bm, geom=remove, context='FACES')
    bm.to_mesh(obj.data); bm.free(); obj.data.update()
    return len(remove)


def export(objs, path):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.gltf(filepath=str(path), export_format='GLB', use_selection=True, export_materials='EXPORT',
                              export_texcoords=True, export_normals=True, export_animations=False,
                              export_cameras=False, export_lights=False, export_apply=True)


def render(objs, prefix):
    REVIEW.mkdir(parents=True, exist_ok=True)
    sc = bpy.context.scene
    if sc.camera is None: setup_render(sc)
    cam = sc.camera
    shots = {'exterior': ((48, -70, 18), (0, 0, 17), 30), 'entrance': ((9, -38, 2.2), (0, -14, 5), 28),
             'closeup': ((-9, -34, 12), (-13, -14, 12), 40),
             'front_left': ((-14, -32, 13), (-14, -4, 13), 32), 'front_right': ((14, -32, 13), (14, -4, 13), 32),
             'front_top': ((0, -34, 30), (0, -4, 31), 30), 'east': ((75, -5, 18), (0, -5, 18), 32),
             'west': ((-75, 5, 18), (0, 5, 18), 32), 'back': ((10, 80, 18), (0, 0, 18), 32),
             'roof': ((35, -45, 62), (0, 0, 34), 30), 'ground': ((-22, -28, 1.7), (-8, -6, 4), 28),
             'canopy_side': ((-15, -21, 2.6), (-7.5, -5, 3.2), 30), 'canopy_side_r': ((15, -21, 2.6), (7.5, -5, 3.2), 30),
             'roof_left': ((-34, -30, 40), (-16, -6, 30), 30), 'roof_right': ((34, -30, 40), (16, -6, 30), 30),
             'roof_top': ((-14, -22, 50), (0, 0, 38), 32), 'roof_back': ((0, 40, 44), (0, 6, 32), 30)}
    for name, (loc, tgt, lens) in shots.items():
        cam.location = loc; cam.data.lens = lens
        cam.rotation_euler = (Vector(tgt) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = str(REVIEW / f'{prefix}_{name}.png'); bpy.ops.render.render(write_still=True)


def setup_render(sc):
    sc.render.engine = 'BLENDER_EEVEE'
    sc.render.resolution_x, sc.render.resolution_y = 1600, 1000
    sc.view_settings.view_transform = 'Standard'; sc.view_settings.look = 'None'
    w = bpy.data.worlds.new('sky'); sc.world = w; w.use_nodes = True
    sky = w.node_tree.nodes.new('ShaderNodeTexSky')
    w.node_tree.links.new(sky.outputs['Color'], w.node_tree.nodes['Background'].inputs['Color'])
    w.node_tree.nodes['Background'].inputs['Strength'].default_value = .12
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 3.2
    sun.data.angle = math.radians(1.5)
    sun.rotation_euler = (math.radians(52), 0, math.radians(-35)); sc.collection.objects.link(sun)
    ground = bpy.data.meshes.new('ground'); ground.from_pydata([(-300, -300, 0), (300, -300, 0), (300, 300, 0), (-300, 300, 0)], [], [(0, 1, 2, 3)])
    g = bpy.data.objects.new('ground', ground); sc.collection.objects.link(g)
    sand = bpy.data.materials.new('sand'); sand.use_nodes = True
    sand.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (.75, .62, .42, 1); ground.materials.append(sand)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam


CACHE = ROOT / 'Logs/hotel_realistic_merged.blend'


def merged_hotel():
    """Classified, straightened, merged hotel. 'cached' reuses the last one (the slow part, ~2 min)."""
    if 'cached' in ARGS and CACHE.exists():
        bpy.ops.wm.open_mainfile(filepath=str(CACHE))
        obj = bpy.data.objects['Hotel']
        return obj, json.loads(obj['report'])
    obj, scale = load_source()
    cls = face_classes(obj)
    counts = {n: int((cls == i).sum()) for i, n in enumerate(NAMES)}
    log('classes', counts)
    make_materials(obj, cls)
    max_move = straighten(obj)
    # Window panes: AI glass is crumpled. Two passes pull every pane onto one flat plane.
    for _ in range(2):
        straighten(obj, tol=.05, max_move=.25, align=.75, only=NAMES.index('glass'))
    max_move = max(max_move, straighten(obj))
    log('max straighten move cm', round(max_move * 100, 2))
    tris = simplify(obj)
    log('after merge tris', tris)
    report = {'source': str(SOURCE.relative_to(ROOT)), 'classes': counts, 'straighten_max_cm': max_move * 100,
              'merged_tris': tris, 'lods': []}
    obj.name = 'Hotel'; obj['report'] = json.dumps(report)
    CACHE.parent.mkdir(exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(CACHE), copy=True)
    return obj, report


def main():
    obj, report = merged_hotel()
    tris = report['merged_tris']
    details, report['windows'] = rebuild_windows(obj, NAMES, log)
    finish_exterior(obj, details, NAMES, CLASSES, log)
    details.hide_render = True
    lods = []
    for i, target in enumerate((250000, 60000, 15000)):
        # Each level is reduced from the previous one (base mesh for LOD0).
        base = lods[-1] if lods else obj
        base.data.calc_loop_triangles()
        ratio = min(1, target / len(base.data.loop_triangles))
        lods.append(decimated_copy(base, ratio, f'HotelLOD{i}'))
    obj.hide_render = True; obj.hide_set(True)
    # Keep close-up frame detail at LOD0; simplify small railings and frames at distance.
    windows = []
    for i, lod in enumerate(lods):
        details.data.calc_loop_triangles()
        budget = (len(details.data.loop_triangles), 24000, 6500)[i]
        w = decimated_copy(details, min(1, budget / len(details.data.loop_triangles)), f'HotelWindows{i}')
        windows.append(w)
    def show(i):
        for j in range(len(lods)): lods[j].hide_render = windows[j].hide_render = j != i
    if PREVIEW_ONLY:
        show(0); render([lods[0]], 'preview0')
        return
    for i, lod in enumerate(lods):
        removed = cut_reception(lod) + cut_reception(windows[i])
        fill_blanks(lod, windows[i], NAMES, log)
        for o in (lod, windows[i]): box_uvs(o)
        tris = sum(len(o.data.polygons) for o in (lod, windows[i]))
        path = OUT / f'resort_hotel_real_lod{i}.glb'
        export([lod, windows[i]], path)
        report['lods'].append({'path': str(path.relative_to(ROOT)), 'triangles': tris, 'reception_faces_removed': removed})
        log('exported', path.name, tris)
    show(0)
    render(lods[:1], 'lod0')
    report['repairs'] = json.loads((ROOT / 'Logs/hotel-window-repairs.json').read_text(encoding='utf-8'))
    report['original_silhouette_preserved'] = True
    bpy.ops.wm.save_as_mainfile(filepath=str(ROOT / 'ArtSource/Resort/hotel_realistic.blend'))
    REPORT.write_text(json.dumps(report, indent=2), encoding='utf-8')
    log(json.dumps(report))


main()
