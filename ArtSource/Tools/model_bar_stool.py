# The hotel bar's stool (Assets/_Game/Art/Props/bar_stool.glb), sized for the players' goofy lifeguard: a round
# teal cushion on a wooden seat, four splayed legs, a brass footrest ring where his feet land with the knees bent
# (thighs about level) and a lower wooden rail for longer legs. Origin at the floor under the centre, +Y up in Unity.
# The numbers are the game's too: AvatarAnimator (StoolRing*, StoolRail*) and GameSceneBuilder.BarDining (StoolTop).
# blender -b -P model_bar_stool.py -- <out.glb>
import bpy, bmesh, math, sys
from mathutils import Vector

out = sys.argv[sys.argv.index("--") + 1]
SEAT_TOP = 1.07        # cushion top over the floor (his waist then level with the counter top)
CUSHION_R = 0.20       # cushion radius (the bottom is wider: it sits on it and over its edge)
CUSHION_H = 0.085
RING_DROP = 0.29       # footrest ring: this far under the cushion top (his thighs level, his short shins straight down)...
RING_R = 0.24          # ...this wide (the heels on it, the shins well clear of the cushion)
RAIL_DROP = 0.56       # lower rail
LEG_TOP_R, LEG_FOOT_R = 0.125, 0.25   # legs: from under the seat out to the floor

bpy.ops.wm.read_factory_settings(use_empty=True)

def mat(name, rgb, rough=0.6, metal=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    lin = tuple(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in rgb)
    b.inputs["Base Color"].default_value = (*lin, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    return m

# The bar's own paints (colour_tripo_bar.py): the game swaps them for its Prop_resort_bar_* materials.
teal = mat("resort_bar_teal", (.08, .47, .48), 0.5)
wood = mat("resort_bar_wood", (.57, .34, .17), 0.55)
brass = mat("resort_bar_brass", (.79, .60, .26), 0.3, 0.7)
ivory = mat("resort_bar_ivory", (.96, .91, .80), 0.5)

parts = []

def add(o, m, smooth=True):
    o.data.materials.append(m)
    for f in o.data.polygons:
        f.use_smooth = smooth
    parts.append(o)
    return o

# Cushion: a soft round pad, the top domed a little, the edge rounded.
bpy.ops.mesh.primitive_cylinder_add(vertices=48, radius=CUSHION_R, depth=CUSHION_H, location=(0, 0, SEAT_TOP - CUSHION_H / 2))
c = bpy.context.object
bm = bmesh.new(); bm.from_mesh(c.data)
bmesh.ops.bevel(bm, geom=[e for e in bm.edges if e.calc_length() < 0.05], offset=0.03, segments=4, affect='EDGES')
for v in bm.verts:
    if v.co.z > CUSHION_H / 2 - 0.001:  # the top face: a slight dome
        v.co.z += 0.012 * (1 - (v.co.xy.length / CUSHION_R) ** 2)
bm.to_mesh(c.data); bm.free()
add(c, teal)
# Piping round the cushion's waist.
bpy.ops.mesh.primitive_torus_add(major_radius=CUSHION_R + 0.002, minor_radius=0.006, major_segments=48, minor_segments=8,
                                 location=(0, 0, SEAT_TOP - CUSHION_H * 0.55))
add(bpy.context.object, ivory)
# Wooden seat under it.
pan_top = SEAT_TOP - CUSHION_H
bpy.ops.mesh.primitive_cylinder_add(vertices=40, radius=CUSHION_R - 0.015, depth=0.035, location=(0, 0, pan_top - 0.0175))
add(bpy.context.object, wood)
leg_top = pan_top - 0.035

def leg_radius(z):
    """How far out from the centre a leg is at height z."""
    t = (leg_top - z) / leg_top
    return LEG_TOP_R + (LEG_FOOT_R - LEG_TOP_R) * t

# Four legs on the diagonals (the sitter's feet go between the front two).
for k in range(4):
    a = math.radians(45 + 90 * k)
    d = Vector((math.cos(a), math.sin(a), 0))
    top = d * LEG_TOP_R + Vector((0, 0, leg_top))
    foot = d * LEG_FOOT_R + Vector((0, 0, 0.012))
    mid, axis = (top + foot) / 2, (top - foot)
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.021, depth=axis.length, location=mid)
    o = bpy.context.object
    o.rotation_euler = axis.to_track_quat('Z', 'Y').to_euler()
    add(o, wood)
    # Brass shoe on each foot.
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.025, depth=0.03, location=foot + Vector((0, 0, 0.003)))
    add(bpy.context.object, brass)

# Footrest ring (brass) on four short struts out from the legs.
ring_z = SEAT_TOP - RING_DROP
bpy.ops.mesh.primitive_torus_add(major_radius=RING_R, minor_radius=0.014, major_segments=56, minor_segments=10, location=(0, 0, ring_z))
add(bpy.context.object, brass)
for k in range(4):
    a = math.radians(45 + 90 * k)
    d = Vector((math.cos(a), math.sin(a), 0))
    inner, outer = d * (leg_radius(ring_z) - 0.01), d * RING_R
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.009, depth=(outer - inner).length, location=(inner + outer) / 2 + Vector((0, 0, ring_z)))
    o = bpy.context.object
    o.rotation_euler = (outer - inner).to_track_quat('Z', 'Y').to_euler()
    add(o, brass)

# Lower rail: four wooden bars between the legs.
rail_z = SEAT_TOP - RAIL_DROP
for k in range(4):
    a0, a1 = math.radians(45 + 90 * k), math.radians(135 + 90 * k)
    r = leg_radius(rail_z)
    p0 = Vector((math.cos(a0) * r, math.sin(a0) * r, rail_z))
    p1 = Vector((math.cos(a1) * r, math.sin(a1) * r, rail_z))
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.014, depth=(p1 - p0).length, location=(p0 + p1) / 2)
    o = bpy.context.object
    o.rotation_euler = (p1 - p0).to_track_quat('Z', 'Y').to_euler()
    add(o, wood)

bpy.ops.object.select_all(action='DESELECT')
for o in parts:
    o.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
o = bpy.context.object
o.name = o.data.name = "bar_stool"
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', use_selection=True)
print("BAR_STOOL", [round(d, 3) for d in o.dimensions], "triangles", sum(len(p.vertices) - 2 for p in o.data.polygons))
