"""The coloured Tripo beach bar (island 2's Coral Bar) given wood, plank and straw textures and a dressed inside.

blender -b --factory-startup -P ArtSource/Tools/retexture_tripo_bar.py [-- --review <prefix>]

colour_tripo_bar.py painted the 1.8-million-triangle Tripo bar in flat colours by region, with no UVs: inside, the
floor, the shelves, the jars on them, the door and the work counters were all one brown. This takes those flat LODs
(ArtSource/Resort/TripoBar/flat/, copied from Assets/_Game/Art/Props on the first run) and:
- gives every face UVs in metres, projected along its normal's main axis (boards and grain run along x or y, straw
  down the roof's slope), so the textures from make_bar_textures.py tile at a real size;
- inside the U of the counter: the jars on the back shelves coloured glass (green, amber, clear, ruby; one colour per
  jar), the back wall behind them dark wood, the staff floor its own boards, and every counter top (the front, the sides, the low work counters) the dark varnished top the front had;
- exports the LODs back to Assets/_Game/Art/Props with the materials renamed resort_barhut_* (the stools and tables
  keep resort_bar_*), then points the wood, deck, floor, top and thatch materials at the textures in
  Art/Props/Textures (GameSceneBuilder.PropPaint takes a resort_barhut_ material's texture as it imports the prefab).
Then: Unity -executeMethod PleaseDontDrown.Editor.GameSceneBuilder.ImportColouredTripoBar.
"""
import json
import math
import shutil
import sys
from pathlib import Path

import bpy
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
PROPS = ROOT / 'Assets/_Game/Art/Props'
FLAT = ROOT / 'ArtSource/Resort/TripoBar/flat'
TEXTURES = ROOT / 'ArtSource/Resort/TripoBar/textures'
sys.path.insert(0, str(ROOT / 'ArtSource/Tools'))
from glb_io import Glb  # noqa: E402

# name: (sRGB colour, texture file or None, metres per tile)
MATERIALS = {
    'resort_barhut_thatch': ((.78, .60, .32), 'bar_thatch.jpg', 1.5),
    'resort_barhut_wood': ((.57, .34, .17), 'bar_wood_grain.jpg', 2.0),
    'resort_barhut_wood_dark': ((.34, .20, .11), 'bar_wood_grain.jpg', 2.0),
    'resort_barhut_deck': ((.69, .47, .27), 'bar_wood_planks.jpg', 2.4),
    'resort_barhut_floor': ((.60, .40, .24), 'bar_wood_planks.jpg', 1.6),
    'resort_barhut_countertop': ((.36, .22, .13), 'bar_wood_grain.jpg', 1.2),
    'resort_barhut_green_glass': ((.17, .46, .25), None, 1.0),
    'resort_barhut_amber_glass': ((.72, .42, .13), None, 1.0),
    'resort_barhut_clear_glass': ((.70, .85, .84), None, 1.0),
    'resort_barhut_ruby_glass': ((.56, .11, .15), None, 1.0),
}
RENAME = {'resort_bar_thatch': 'resort_barhut_thatch', 'resort_bar_wood': 'resort_barhut_wood',
          'resort_bar_deck': 'resort_barhut_deck', 'resort_bar_countertop': 'resort_barhut_countertop'}
GLASS = ['resort_barhut_green_glass', 'resort_barhut_amber_glass', 'resort_barhut_clear_glass', 'resort_barhut_ruby_glass']
TEXTURE_MEAN = 0.8  # make_bar_textures.py's pictures average this: the factor is the colour over it

# Where things are, in Blender's frame of the bar (x = -Unity x, y = -Unity z, z up; the front is -y), measured on the
# model (SceneProbeMaps.HeightMapBatch in Unity, and the faces here): the back wall at y 3.18 runs the full width; a
# shelf unit either side of the door, x 1.3..4.5 (and mirrored), jars in three rows standing on boards; the staff floor
# 0.45 up inside the U (|x| < 2.3, y -3.6..3.2); the counter tops 1.55 up, the work counters' 1.07.
SHELF_X = (1.45, 4.35)
JAR_ROWS = ((1.78, 2.12), (2.24, 2.57), (2.70, 2.97))
JAR_DEPTH = (2.80, 3.145)


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def load_flat(level):
    flat = FLAT / f'resort_tripo_beach_bar_lod{level}.glb'
    if not flat.exists():
        FLAT.mkdir(parents=True, exist_ok=True)
        shutil.copy(PROPS / flat.name, flat)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(flat))
    return next(o for o in bpy.context.scene.objects if o.type == 'MESH')


def face_arrays(me):
    n = len(me.polygons)
    c = np.zeros(n * 3); me.polygons.foreach_get('center', c)
    nr = np.zeros(n * 3); me.polygons.foreach_get('normal', nr)
    mi = np.zeros(n, np.int64); me.polygons.foreach_get('material_index', mi)
    return c.reshape(-1, 3), nr.reshape(-1, 3), mi


def face_bounds(me):
    """Each face's lowest and highest corner (x, y, z)."""
    co = np.zeros(len(me.vertices) * 3); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
    totals = np.zeros(len(me.polygons), np.int64); me.polygons.foreach_get('loop_total', totals)
    lv = np.zeros(len(me.loops), np.int64); me.loops.foreach_get('vertex_index', lv)
    starts = np.concatenate([[0], np.cumsum(totals)[:-1]])
    p = co[lv]
    return np.minimum.reduceat(p, starts, axis=0), np.maximum.reduceat(p, starts, axis=0)


def jars(me, c, nr, wood):
    """The jar faces on the back shelves, and which jar each belongs to (connected pieces)."""
    ax = np.abs(c[:, 0])
    on_shelf = (ax > SHELF_X[0]) & (ax < SHELF_X[1]) & (c[:, 1] > JAR_DEPTH[0]) & (c[:, 1] < JAR_DEPTH[1])
    in_row = np.zeros(len(c), bool)
    for lo, hi in JAR_ROWS:
        in_row |= (c[:, 2] > lo) & (c[:, 2] < hi)
    # Not the boards' undersides or the unit's own end panels.
    sel = wood & on_shelf & in_row & (np.abs(nr[:, 2]) < 0.97)
    sel &= ~((np.abs(nr[:, 0]) > 0.97) & ((ax < SHELF_X[0] + 0.06) | (ax > SHELF_X[1] - 0.06)))
    faces = np.nonzero(sel)[0]
    # Union-find over the faces' vertices: a jar is one connected piece.
    nv = len(me.vertices)
    parent = np.arange(nv)
    def find(a):
        root = a
        while parent[root] != root:
            root = parent[root]
        while parent[a] != root:
            parent[a], a = root, parent[a]
        return root
    for f in faces:
        vs = me.polygons[f].vertices
        r0 = find(vs[0])
        for v in vs[1:]:
            r = find(v)
            if r != r0:
                parent[r] = r0
    label = np.array([find(me.polygons[f].vertices[0]) for f in faces])
    return faces, label


def box_uvs(me, tiles):
    """UVs in metres / the material's tile, projected along each face's main axis."""
    nl = len(me.loops)
    lv = np.zeros(nl, np.int64); me.loops.foreach_get('vertex_index', lv)
    co = np.zeros(len(me.vertices) * 3); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
    lf = np.zeros(nl, np.int64)
    starts = np.zeros(len(me.polygons), np.int64); me.polygons.foreach_get('loop_start', starts)
    totals = np.zeros(len(me.polygons), np.int64); me.polygons.foreach_get('loop_total', totals)
    lf = np.repeat(np.arange(len(me.polygons)), totals)
    _, nr, mi = face_arrays(me)
    axis = np.argmax(np.abs(nr), axis=1)[lf]
    p = co[lv]
    u = np.where(axis == 0, p[:, 1], p[:, 0])
    v = np.where(axis == 2, p[:, 1], p[:, 2])
    tile = tiles[mi[lf]]
    uv = np.stack([u / tile, v / tile], axis=1)
    layer = me.uv_layers.new(name='UVMap')
    layer.data.foreach_set('uv', uv.ravel())


def retexture(level, review):
    o = load_flat(level)
    me = o.data
    old = [m.name for m in me.materials]
    keep = [n for n in old if n not in RENAME]
    names = keep + list(MATERIALS)
    remap = np.array([names.index(RENAME.get(n, n)) for n in old])
    c, nr, mi_old = face_arrays(me)
    mi = remap[mi_old]
    idx = {n: i for i, n in enumerate(names)}
    wood = mi == idx['resort_barhut_wood']
    counts = {}

    def paint(mask, name):
        mi[mask] = idx[name]
        counts[name] = counts.get(name, 0) + int(mask.sum())

    ax = np.abs(c[:, 0])
    inside = (ax < 5.4) & (c[:, 1] > -4.95) & (c[:, 1] < 3.25)
    # The jars first (they are "wood" until now), one glass colour each. A jar can be several connected pieces (the
    # import splits it at hard edges): pieces of a shelf row closer than 12 cm along it are one jar.
    faces, label = jars(me, c, nr, wood)
    pieces = []
    for lab in np.unique(label):
        members = faces[label == lab]
        centre = c[members].mean(0)
        row = next((r for r, (lo, hi) in enumerate(JAR_ROWS) if lo - 0.03 < centre[2] < hi + 0.03), 0)
        pieces.append((int(np.sign(centre[0])), row, centre[0], members))
    jar_count = 0
    for side in (-1, 1):
        for row in range(len(JAR_ROWS)):
            line = sorted((p for p in pieces if p[0] == side and p[1] == row), key=lambda p: p[2])
            jar, last = -1, None
            for _, _, x, members in line:
                if last is None or x - last > 0.12:
                    jar += 1
                    jar_count += 1
                last = x
                mask = np.zeros(len(c), bool); mask[members] = True
                paint(mask, GLASS[(jar * 3 + row * 2 + (side > 0)) % len(GLASS)])
    # The back wall behind the jars dark, so they stand out: only triangles wholly inside a shelf unit (the wall is a
    # few big triangles; one reaching past the unit made a zigzag over the door). The door between the units is the
    # same big triangles, so it stays wood.
    wood = mi == idx['resort_barhut_wood']
    lo, hi = face_bounds(me)
    within = (np.minimum(np.abs(lo[:, 0]), np.abs(hi[:, 0])) > SHELF_X[0] - 0.08) & (np.maximum(np.abs(lo[:, 0]), np.abs(hi[:, 0])) < SHELF_X[1] + 0.08)
    within &= (np.sign(lo[:, 0]) == np.sign(hi[:, 0])) & (lo[:, 2] > JAR_ROWS[0][0] - 0.1) & (hi[:, 2] < JAR_ROWS[-1][1] + 0.12)
    back = wood & within & (np.abs(c[:, 1] - 3.18) < 0.04) & (nr[:, 1] < -0.9)
    paint(back, 'resort_barhut_wood_dark')
    up = nr[:, 2] > 0.9
    floor = up & (np.abs(c[:, 2] - 0.45) < 0.05) & (ax < 2.35) & (c[:, 1] > -3.6) & (c[:, 1] < 3.25)
    paint(floor, 'resort_barhut_floor')
    wood = mi == idx['resort_barhut_wood']
    tops = wood & up & inside & ((np.abs(c[:, 2] - 1.55) < 0.03) | ((np.abs(c[:, 2] - 1.07) < 0.05) & (ax < 3.4)))
    paint(tops, 'resort_barhut_countertop')

    # Materials: the kept ones as imported, the new ones flat colour (the textures are added to the GLB after).
    old_mats = {m.name: m for m in me.materials}
    me.materials.clear()
    for n in names:
        if n in old_mats and n not in MATERIALS:
            me.materials.append(old_mats[n])
            continue
        colour = MATERIALS[n][0]
        m = bpy.data.materials.new(n)
        m.use_nodes = True
        p = m.node_tree.nodes.get('Principled BSDF')
        rgba = tuple(srgb_to_linear(x) for x in colour) + (1.0,)
        p.inputs['Base Color'].default_value = rgba
        p.inputs['Roughness'].default_value = 0.3 if n.endswith('_glass') else 0.6
        m.diffuse_color = rgba
        me.materials.append(m)
    me.polygons.foreach_set('material_index', mi)  # (after the slots: clearing them resets every face to the first)
    tiles = np.array([MATERIALS[n][2] if n in MATERIALS else 1.0 for n in names])
    box_uvs(me, tiles)
    me.update()

    out = PROPS / f'resort_tripo_beach_bar_lod{level}.glb'
    bpy.ops.export_scene.gltf(filepath=str(out), export_format='GLB', export_materials='EXPORT', export_yup=True,
                              export_image_format='NONE', export_texcoords=True, export_normals=True)
    link_textures(out)
    print(f'[bar] lod{level}: {len(me.polygons)} faces, {jar_count} jars, ' +
          ', '.join(f'{k} {v}' for k, v in sorted(counts.items())))
    if review and level == 0:
        render_review(o, review)


def link_textures(path):
    """Point each textured resort_barhut_ material at its picture in Art/Props/Textures (kept outside the GLB, so Unity
    imports it once, compressed, with mipmaps), its colour the factor over the picture's mean."""
    g = Glb(str(path))
    j = g.json
    files = sorted({t for _, t, _ in MATERIALS.values() if t})
    j['images'] = [{'name': f[:-4], 'uri': 'Textures/' + f} for f in files]
    j['samplers'] = [{'magFilter': 9729, 'minFilter': 9987, 'wrapS': 10497, 'wrapT': 10497}]
    j['textures'] = [{'sampler': 0, 'source': i} for i in range(len(files))]
    for m in j['materials']:
        spec = MATERIALS.get(m.get('name'))
        if not spec:
            continue
        colour, texture, _ = spec
        pbr = m.setdefault('pbrMetallicRoughness', {})
        if texture:
            pbr['baseColorTexture'] = {'index': files.index(texture)}
            pbr['baseColorFactor'] = [min(1.0, srgb_to_linear(x) / srgb_to_linear(TEXTURE_MEAN)) for x in colour] + [1.0]
        pbr['metallicFactor'] = 0.0
    g.save(str(path))


def render_review(o, prefix):
    """A Blender look from behind the counter at the shelves (textures linked into the nodes for the render only)."""
    for m in o.data.materials:
        spec = MATERIALS.get(m.name)
        if not spec or not spec[1]:
            continue
        nodes, links = m.node_tree.nodes, m.node_tree.links
        p = nodes.get('Principled BSDF')
        tex = nodes.new('ShaderNodeTexImage')
        tex.image = bpy.data.images.load(str(TEXTURES / spec[1]), check_existing=True)
        mix = nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'
        mix.inputs['Factor'].default_value = 1.0
        factor = [min(1.0, srgb_to_linear(x) / srgb_to_linear(TEXTURE_MEAN)) for x in spec[0]] + [1.0]
        mix.inputs['B'].default_value = factor
        links.new(tex.outputs['Color'], mix.inputs['A'])
        links.new(mix.outputs['Result'], p.inputs['Base Color'])
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x, scene.render.resolution_y = 1280, 800
    world = bpy.data.worlds.new('w'); scene.world = world
    world.use_nodes = True
    world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.6, 0.75, 0.95, 1)
    world.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.8
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN'))
    sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(50), 0, math.radians(30))
    scene.collection.objects.link(sun)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam'))
    scene.collection.objects.link(cam)
    scene.camera = cam
    for name, at, look, fov in (('shelves', (0, -2.4, 2.1), (0, 3.18, 2.2), 70), ('counter', (0, 2.6, 2.2), (0, -4.2, 1.4), 70),
                                ('outside', (6, -17, 4.5), (0, 0, 2.0), 50)):
        cam.location = at
        d = np.array(look) - np.array(at)
        cam.rotation_euler = (math.atan2(math.hypot(d[0], d[1]), -d[2]), 0, math.atan2(d[1], d[0]) - math.pi / 2)
        cam.data.angle = math.radians(fov)
        scene.render.filepath = f'{prefix}_{name}.png'
        bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    review = argv[argv.index('--review') + 1] if '--review' in argv else None
    for level in range(3):
        retexture(level, review)
    dest = PROPS / 'Textures'
    for f in TEXTURES.glob('bar_*.jpg'):
        shutil.copy(f, dest / f.name)


if __name__ == '__main__':
    main()
