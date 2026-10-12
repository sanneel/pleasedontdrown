# Takes the twelve built-in stools and the lounge's four low tables out of the coloured Tripo bar
# (resort_tripo_beach_bar_lod0-2.glb, made by colour_tripo_bar.py). Squashed with the rest of the model to 26 x 16 m
# the stools came out as 1.2 x 0.7 m ovals right against the counter, and the tables stood 5-14 cm from the sofas:
# nobody could sit on either like a person. The game puts its own there instead (model_bar_stool.py,
# model_bar_table.py, GameSceneBuilder.BarDining). Run once on the coloured LODs:
# blender -b -P remove_bar_furniture.py
from pathlib import Path
import bpy, bmesh

ROOT = Path(__file__).resolve().parents[2]
PROPS = ROOT / 'Assets/_Game/Art/Props'

# In the bar's Unity space (x, y up, z): Unity = (-blender x, blender z, -blender y). Measured cushion centres.
FRONT = [-3.39, -1.04, 1.11, 3.43]               # the front row (z 5.25), along the counter
SIDE_Z = [0.47, 1.44, 2.65, 4.00]                # each side (x +/-5.8), along the side counters
LOW, HIGH = 0.275, 1.25                          # above the deck (0.266), up to the cushions (1.155); the counter top is 1.44+


DECK = 0.266
# The low tables (top 0.75) between the sofas at x +/-10.2: z 3.01..3.95 and -3.78..-2.83 (the sofas' cushions start
# at 2.88 / 4.00 and -2.68 / -3.83, measured: keep clear of them).
TABLES = [(2.99, 3.965), (-3.79, -2.82)]
TABLE_HIGH = 0.77


def in_table(x, z):
    return 9.42 < abs(x) < 11.0 and any(a < z < b for a, b in TABLES)


def in_footprint(x, z):
    if 4.87 < z < 6.0 and any(abs(x - c) < 0.7 for c in FRONT):
        return True  # (the counter's front is at z 4.68-4.84)
    if 5.05 < abs(x) < 6.9 and any(abs(z - c) < 0.48 for c in SIDE_Z):
        return True  # (the side counters end at |x| 4.84)
    return in_table(x, z)


def is_stool(x, y, z):
    return LOW < y < (TABLE_HIGH if in_table(x, z) else HIGH) and in_footprint(x, z)


for lod in range(3):
    path = PROPS / f'resort_tripo_beach_bar_lod{lod}.glb'
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(path))
    o = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bm = bmesh.new()
    bm.from_mesh(o.data)
    doomed = []
    for f in bm.faces:
        c = f.calc_center_median()
        # The stool, and the bits of deck its feet were welded to (anything in its footprint rising off the deck).
        if is_stool(-c.x, c.z, -c.y) or (c.z < (TABLE_HIGH if in_table(-c.x, -c.y) else HIGH) and in_footprint(-c.x, -c.y) and max(v.co.z for v in f.verts) > DECK + 0.006):
            doomed.append(f)
    before = len(bm.faces)
    bmesh.ops.delete(bm, geom=doomed, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context='VERTS')
    # Close the holes that leaves in the deck (flat: the new faces take their normal from their shape).
    def near(v):
        return any(in_footprint(-v.co.x + dx, -v.co.y + dz) for dx in (-0.3, 0, 0.3) for dz in (-0.3, 0, 0.3))
    holes = [e for e in bm.edges if e.is_boundary and all(abs(v.co.z - DECK) < 0.03 and near(v) for v in e.verts)]
    filled = bmesh.ops.holes_fill(bm, edges=holes, sides=0)['faces'] if holes else []
    for f in filled:
        f.normal_update()
        if f.normal.z < 0:
            f.normal_flip()  # facing up, like the deck
    print(f'LOD{lod}: {len(filled)} holes in the deck closed')
    bm.to_mesh(o.data)
    bm.free()
    o.data.update()
    # Same material names as before (the import adds ".001"): the prefab's materials go by slot, the names by look.
    for m in o.data.materials:
        if m is not None and '.' in m.name:
            m.name = m.name.split('.')[0]
    bpy.ops.export_scene.gltf(filepath=str(path), export_format='GLB', export_materials='EXPORT', export_yup=True)
    print(f'LOD{lod}: {len(doomed)} of {before} faces were the stools and the low tables')
