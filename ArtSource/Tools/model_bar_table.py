# The hotel bar lounge's low table (Assets/_Game/Art/Props/bar_table.glb), for between two of its sofas: the bar model's
# own stood 5-14 cm from the sofas (remove_bar_furniture.py takes them out), so a seated player's legs had nowhere to
# go. This one is narrower, set in the middle, 28 cm clear of each sofa. Same height and colours as the old ones: a
# teal top with a wooden rim on four wooden legs. Origin at the floor under its middle, +Y up, the long side along X.
# The numbers are the game's too (GameSceneBuilder.BarDining: BarTableTop, BarTableDepth).
# blender -b -P model_bar_table.py -- <out.glb>
import bpy, bmesh, sys

out = sys.argv[sys.argv.index("--") + 1]
TOP = 0.484            # top surface over the floor (the old tables': 0.75 on the bar's 0.266 deck)
WIDTH, DEPTH = 0.95, 0.56
SLAB = 0.045

bpy.ops.wm.read_factory_settings(use_empty=True)


def mat(name, rgb, rough=0.6):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    lin = tuple(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in rgb)
    b.inputs["Base Color"].default_value = (*lin, 1)
    b.inputs["Roughness"].default_value = rough
    return m


# The bar's own paints (colour_tripo_bar.py): the game swaps them for its Prop_resort_bar_* materials.
teal = mat("resort_bar_teal", (.08, .47, .48), 0.5)
wood = mat("resort_bar_wood", (.57, .34, .17), 0.55)
parts = []


def box(size, at, m, bevel=0.0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=at)
    o = bpy.context.object
    o.scale = size
    bpy.ops.object.transform_apply(scale=True)
    if bevel > 0:
        bm = bmesh.new(); bm.from_mesh(o.data)
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=3, affect='EDGES')
        bm.to_mesh(o.data); bm.free()
    o.data.materials.append(m)
    parts.append(o)
    return o


# Blender: X along the table, -Y = the game's +Z (depth), Z up.
box((WIDTH, DEPTH, SLAB * 0.7), (0, 0, TOP - SLAB * 0.35), teal, 0.012)              # the teal top
box((WIDTH + 0.02, DEPTH + 0.02, SLAB * 0.45), (0, 0, TOP - SLAB * 0.8), wood, 0.008)  # its wooden rim under it
box((WIDTH - 0.12, DEPTH - 0.12, 0.06), (0, 0, TOP - SLAB - 0.03), wood, 0.005)        # the apron
for sx in (-1, 1):
    for sy in (-1, 1):
        box((0.05, 0.05, TOP - SLAB), (sx * (WIDTH / 2 - 0.08), sy * (DEPTH / 2 - 0.08), (TOP - SLAB) / 2), wood, 0.006)

bpy.ops.object.select_all(action='DESELECT')
for o in parts:
    o.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
o = bpy.context.object
o.name = o.data.name = "bar_table"
for f in o.data.polygons:
    f.use_smooth = False
bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', use_selection=True)
print("BAR_TABLE", [round(d, 3) for d in o.dimensions])
