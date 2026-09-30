# Blender batch script: detailed low-poly SMG and pump shotgun for the game, modelled in the gun's own Unity space
# (metres; x right, y up, z toward the muzzle; the same numbers as GameSceneBuilder.Weapons.cs) so the grips, sights,
# muzzles and moving parts built there still fit. Moving / swappable parts (the shotgun's pump, the SMG's charging
# knob, magazines, shop attachments) stay in Unity; this is the static body.
#   blender -b --factory-startup -P model_guns.py -- <out_dir> [--preview <prefix>]
# Output: <out_dir>/smg.glb, <out_dir>/shotgun.glb. In Unity the model goes under the gun root with rotation
# (0, 90, 0) and scale 1 (glTF import puts Blender's +X on Unity -X; +90 about y brings the muzzle to +z).
import sys, os, math, argparse
import bpy, bmesh, mathutils

argv = sys.argv[sys.argv.index("--") + 1:]
ap = argparse.ArgumentParser()
ap.add_argument("out")
ap.add_argument("--preview", default="")
a = ap.parse_args(argv)

PAINT = {  # sRGB, matte (the stylized Meshy guns' palette plus wood, rubber and a worn steel)
    "polymer": (0.21, 0.22, 0.24),
    "blued":   (0.38, 0.40, 0.44),
    "steel":   (0.58, 0.60, 0.63),
    "rubber":  (0.11, 0.11, 0.12),
    "wood":    (0.55, 0.33, 0.17),
    "wooddark": (0.40, 0.23, 0.11),
    "brass":   (0.78, 0.60, 0.28),
}

def srgb_to_linear(c): return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4

def material(name):
    m = bpy.data.materials.get(name)
    if m: return m
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*[srgb_to_linear(c) for c in PAINT[name]], 1)
    b.inputs["Roughness"].default_value = 0.85
    b.inputs["Metallic"].default_value = 0.0
    return m

def rot_x(deg):
    # Unity's rotation about +x (same matrix as UnityEngine.Quaternion.Euler(deg, 0, 0)).
    t = math.radians(deg); c, s = math.cos(t), math.sin(t)
    return mathutils.Matrix(((1, 0, 0), (0, c, -s), (0, s, c)))

def rot_z(deg):
    t = math.radians(deg); c, s = math.cos(t), math.sin(t)
    return mathutils.Matrix(((c, -s, 0), (s, c, 0), (0, 0, 1)))

def to_blender(p):  # Unity (x right, y up, z forward) -> Blender (X forward, Y left, Z up)
    return mathutils.Vector((p[2], -p[0], p[1]))

parts = []

def add(name, verts_u, faces, mat, bevel=0.0, smooth=False):
    me = bpy.data.meshes.new(name)
    me.from_pydata([to_blender(v) for v in verts_u], [], faces)
    me.update()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.data.materials.append(material(mat))
    # Faces come in with Unity winding; recompute outward normals.
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(me); bm.free()
    if bevel > 0:
        mod = ob.modifiers.new("bevel", 'BEVEL'); mod.width = bevel; mod.segments = 1; mod.limit_method = 'ANGLE'
        mod.angle_limit = math.radians(40)
        bpy.context.view_layer.objects.active = ob
        bpy.ops.object.modifier_apply(modifier=mod.name)
    parts.append(ob)
    return ob

def box(name, centre, size, mat, rx=0.0, rz=0.0, bevel=0.0025):
    hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
    local = [mathutils.Vector((x, y, z)) for x in (-hx, hx) for y in (-hy, hy) for z in (-hz, hz)]
    R = rot_x(rx) @ rot_z(rz)
    c = mathutils.Vector(centre)
    verts = [c + R @ v for v in local]
    faces = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    return add(name, verts, faces, mat, bevel=min(bevel, min(size) * 0.3))

def cyl(name, centre, radius, length, mat, seg=12, rx=0.0, bevel=0.0, radius2=None):
    # Along Unity z (the barrel axis), then turned about x by rx.
    r2 = radius if radius2 is None else radius2
    R = rot_x(rx); c = mathutils.Vector(centre)
    verts = []
    for zi, (z, r) in enumerate(((-length / 2, radius), (length / 2, r2))):
        for i in range(seg):
            t = 2 * math.pi * i / seg + math.pi / seg
            verts.append(c + R @ mathutils.Vector((math.cos(t) * r, math.sin(t) * r, z)))
    faces = [(i, (i + 1) % seg, seg + (i + 1) % seg, seg + i) for i in range(seg)]
    faces += [tuple(range(seg))[::-1], tuple(range(seg, 2 * seg))]
    return add(name, verts, faces, mat, bevel=bevel)

def profile(name, pts_zy, x0, x1, mat, bevel=0.003):
    # A side profile (points as (z, y), counter-clockwise seen from the right) extruded from x0 to x1.
    n = len(pts_zy)
    verts = [(x0, y, z) for z, y in pts_zy] + [(x1, y, z) for z, y in pts_zy]
    faces = [tuple(range(n)), tuple(range(n, 2 * n))[::-1]]
    faces += [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]
    return add(name, verts, faces, mat, bevel=bevel)

def pin(name, centre, radius=0.0025, mat="steel", width=None, seg=8):
    # A cross pin through the receiver (along x), showing on both sides.
    w = width or 0.001
    R = mathutils.Matrix(((0, 0, 1), (0, 1, 0), (-1, 0, 0)))  # z axis -> x
    c = mathutils.Vector(centre)
    verts = []
    for x in (-w / 2, w / 2):
        for i in range(seg):
            t = 2 * math.pi * i / seg
            verts.append(c + R @ mathutils.Vector((math.cos(t) * radius, math.sin(t) * radius, x)))
    faces = [(i, (i + 1) % seg, seg + (i + 1) % seg, seg + i) for i in range(seg)]
    faces += [tuple(range(seg))[::-1], tuple(range(seg, 2 * seg))]
    return add(name, verts, faces, mat)

def export(path):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in parts: ob.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = os.path.splitext(os.path.basename(path))[0]
    for p in ob.data.polygons: p.use_smooth = False
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True)
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    print(f"[guns] {path}: {tris} triangles")
    return ob

def preview(ob, prefix):
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 1000; scene.render.resolution_y = 560
    world = bpy.data.worlds.new("w"); scene.world = world; world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.75, 0.9, 1)
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); scene.collection.objects.link(sun)
    sun.data.energy = 4; sun.rotation_euler = (0.7, 0.3, 0.9)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scene.collection.objects.link(cam)
    scene.camera = cam
    vs = [ob.matrix_world @ v.co for v in ob.data.vertices]
    mn = mathutils.Vector([min(v[i] for v in vs) for i in range(3)]); mx = mathutils.Vector([max(v[i] for v in vs) for i in range(3)])
    centre = (mn + mx) / 2; size = max(mx - mn)
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = size * 1.1
    for name, d in [("right", (0, -1, 0)), ("left", (0, 1, 0)), ("persp", (-0.8, -1, 0.6)), ("back", (-1, -0.35, 0.25))]:
        v = mathutils.Vector(d).normalized()
        cam.location = centre + v * 5
        cam.rotation_euler = (-v).to_track_quat('-Z', 'Z').to_euler()
        scene.render.filepath = f"{prefix}_{name}.png"
        bpy.ops.render.render(write_still=True)

def clear():
    global parts
    for ob in list(bpy.data.objects): bpy.data.objects.remove(ob)
    parts = []

bpy.ops.wm.read_factory_settings(use_empty=True)
os.makedirs(a.out, exist_ok=True)

# ----------------------------------------------------------------------------------------------------- SMG
# Upper receiver: body with a stepped top, an ejection port on the right, the cocking slot on top.
box("Upper", (0, 0.033, 0.05), (0.045, 0.04, 0.28), "blued", bevel=0.004)
box("UpperTopStep", (0, 0.0545, 0.07), (0.034, 0.006, 0.22), "blued", bevel=0.002)
box("EjectPort", (0.0226, 0.036, 0.075), (0.002, 0.014, 0.05), "rubber", bevel=0)
box("EjectLip", (0.0232, 0.0445, 0.075), (0.002, 0.003, 0.056), "steel", bevel=0)
box("CockSlot", (0, 0.0575, 0.035), (0.006, 0.002, 0.09), "rubber", bevel=0)
for i in range(9):  # picatinny teeth on the top rail
    box(f"RailTooth{i}", (0, 0.0605, -0.035 + i * 0.019), (0.022, 0.004, 0.009), "polymer", bevel=0.001)
box("RailBase", (0, 0.0585, 0.04), (0.02, 0.003, 0.17), "polymer", bevel=0.001)
# Lower: polymer frame with a magwell flare, trigger guard loop, trigger.
box("Lower", (0, 0.002, 0.035), (0.042, 0.024, 0.25), "polymer", bevel=0.004)
box("Magwell", (0, -0.021, 0.085), (0.036, 0.032, 0.054), "polymer", bevel=0.004)
box("MagwellFlare", (0, -0.036, 0.085), (0.04, 0.006, 0.06), "polymer", bevel=0.002)
box("MagRelease", (0.0205, -0.014, 0.058), (0.004, 0.008, 0.008), "steel", bevel=0.001)
box("GuardBottom", (0, -0.046, 0.02), (0.011, 0.006, 0.058), "polymer", bevel=0.002)
box("GuardFront", (0, -0.03, 0.047), (0.011, 0.034, 0.006), "polymer", bevel=0.002)
box("GuardBack", (0, -0.038, -0.006), (0.011, 0.018, 0.005), "polymer", bevel=0.001)
box("Trigger", (0, -0.023, 0.016), (0.006, 0.018, 0.005), "steel", rx=18, bevel=0.001)
box("TriggerTip", (0, -0.033, 0.021), (0.006, 0.008, 0.005), "steel", rx=40, bevel=0.001)
box("SafetyR", (0.0215, 0.006, -0.02), (0.003, 0.008, 0.014), "steel", bevel=0.001)
box("SafetyL", (-0.0215, 0.006, -0.02), (0.003, 0.008, 0.014), "steel", bevel=0.001)
for z in (-0.06, 0.0, 0.14):
    pin(f"Pin{z}", (0, 0.004, z), width=0.0435)
# Grip with finger grooves on the front and rubber side panels.
GRIP_C, GRIP_RX = mathutils.Vector((0, -0.055, -0.035)), -12
def on_grip(local): return tuple(GRIP_C + rot_x(GRIP_RX) @ mathutils.Vector(local))
box("Grip", tuple(GRIP_C), (0.032, 0.1, 0.045), "polymer", rx=GRIP_RX, bevel=0.005)
for i, y in enumerate((0.028, 0.004, -0.02)):
    box(f"GripGroove{i}", on_grip((0, y, 0.0235)), (0.028, 0.007, 0.005), "polymer", rx=GRIP_RX, bevel=0.002)
box("GripPanelR", on_grip((0.0165, -0.004, -0.002)), (0.002, 0.07, 0.034), "rubber", rx=GRIP_RX, bevel=0)
box("GripPanelL", on_grip((-0.0165, -0.004, -0.002)), (0.002, 0.07, 0.034), "rubber", rx=GRIP_RX, bevel=0)
box("GripBase", on_grip((0, -0.052, 0)), (0.034, 0.006, 0.048), "polymer", rx=GRIP_RX, bevel=0.002)
# Barrel: a vented shroud, barrel, muzzle nut with threads.
cyl("Shroud", (0, 0.03, 0.203), 0.016, 0.028, "polymer", seg=10, bevel=0.002)
for i in range(4):
    box(f"ShroudVent{i}", (0.0152, 0.03, 0.194 + i * 0.006), (0.003, 0.008, 0.003), "rubber", bevel=0)
    box(f"ShroudVentL{i}", (-0.0152, 0.03, 0.194 + i * 0.006), (0.003, 0.008, 0.003), "rubber", bevel=0)
cyl("Barrel", (0, 0.03, 0.235), 0.009, 0.05, "steel", seg=10)
cyl("MuzzleNut", (0, 0.03, 0.252), 0.012, 0.012, "blued", seg=8, bevel=0.001)
cyl("MuzzleBore", (0, 0.03, 0.2585), 0.004, 0.002, "rubber", seg=8)
for i in range(3):
    cyl(f"Thread{i}", (0, 0.03, 0.2215 + i * 0.004), 0.0102, 0.0015, "blued", seg=10)
# Sights: rear aperture with wings, front post with a guard.
box("RearBase", (0, 0.059, -0.06), (0.024, 0.006, 0.014), "blued", bevel=0.001)
box("RearWingL", (-0.008, 0.066, -0.06), (0.004, 0.01, 0.01), "blued", bevel=0.001)
box("RearWingR", (0.008, 0.066, -0.06), (0.004, 0.01, 0.01), "blued", bevel=0.001)
box("RearAperture", (0, 0.064, -0.06), (0.006, 0.006, 0.006), "steel", bevel=0.001)
box("FrontBase", (0, 0.059, 0.17), (0.016, 0.006, 0.014), "blued", bevel=0.001)
box("FrontPost", (0, 0.066, 0.17), (0.003, 0.012, 0.004), "steel", bevel=0)
box("FrontGuardL", (-0.0065, 0.064, 0.17), (0.003, 0.01, 0.01), "blued", bevel=0.001)
box("FrontGuardR", (0.0065, 0.064, 0.17), (0.003, 0.01, 0.01), "blued", bevel=0.001)
# Folding stock: two round rods, a hinge block, a rubber butt plate with a sling loop.
box("StockHinge", (0, 0.015, -0.095), (0.036, 0.06, 0.016), "blued", bevel=0.003)
cyl("StockRodTop", (0, 0.035, -0.175), 0.0075, 0.16, "steel", seg=8)
cyl("StockRodBottom", (0, -0.005, -0.175), 0.0075, 0.16, "steel", seg=8)
box("ButtPlate", (0, 0.015, -0.258), (0.034, 0.075, 0.018), "polymer", bevel=0.004)
box("ButtPad", (0, 0.015, -0.269), (0.036, 0.078, 0.006), "rubber", bevel=0.002)
box("SlingLoop", (0, -0.03, -0.258), (0.006, 0.012, 0.012), "steel", bevel=0.001)
ob = export(os.path.join(a.out, "smg.glb"))
if a.preview: preview(ob, a.preview + "_smg")
clear()

# ------------------------------------------------------------------------------------------------ Shotgun
box("Receiver", (0, 0.015, 0), (0.05, 0.07, 0.24), "blued", bevel=0.005)
box("ReceiverTopFlat", (0, 0.0505, -0.005), (0.036, 0.002, 0.22), "blued", bevel=0)
box("EjectPort", (0.0255, 0.028, 0.03), (0.002, 0.022, 0.07), "rubber", bevel=0)
box("EjectPortEdge", (0.026, 0.04, 0.03), (0.002, 0.003, 0.074), "steel", bevel=0)
box("LoadingPort", (0, -0.0205, 0.035), (0.034, 0.002, 0.09), "rubber", bevel=0)
box("LoadingGate", (0, -0.0205, 0.004), (0.03, 0.003, 0.03), "steel", bevel=0.001)
box("ActionRelease", (-0.0255, -0.012, -0.02), (0.003, 0.012, 0.008), "steel", bevel=0.001)
box("SafetyButton", (0, 0.0, -0.105), (0.052, 0.006, 0.006), "steel", bevel=0.001)
for z, y in ((-0.08, 0.005), (0.07, 0.005), (0.09, -0.01)):
    pin(f"Pin{z}", (0, y, z), width=0.0505)
# Trigger guard (a loop) and a curved trigger.
box("GuardBottom", (0, -0.047, -0.02), (0.012, 0.006, 0.066), "blued", bevel=0.002)
box("GuardFront", (0, -0.033, 0.012), (0.012, 0.03, 0.006), "blued", bevel=0.002)
box("GuardBack", (0, -0.036, -0.052), (0.012, 0.024, 0.006), "blued", rx=-20, bevel=0.002)
box("Trigger", (0, -0.027, -0.015), (0.006, 0.02, 0.005), "steel", rx=15, bevel=0.001)
box("TriggerTip", (0, -0.038, -0.011), (0.006, 0.008, 0.005), "steel", rx=40, bevel=0.001)
# Barrel with a vent rib on posts, a bead, and the clamp that ties it to the magazine tube.
cyl("Barrel", (0, 0.035, 0.37), 0.016, 0.5, "blued", seg=14)
cyl("MuzzleCrown", (0, 0.035, 0.617), 0.0172, 0.01, "steel", seg=14)
cyl("MuzzleBore", (0, 0.035, 0.6225), 0.011, 0.002, "rubber", seg=12)
box("RibTop", (0, 0.057, 0.37), (0.009, 0.003, 0.48), "blued", bevel=0.001)
for i in range(8):
    box(f"RibPost{i}", (0, 0.053, 0.15 + i * 0.062), (0.006, 0.006, 0.008), "blued", bevel=0)
box("BeadBase", (0, 0.06, 0.607), (0.006, 0.003, 0.008), "steel", bevel=0)
cyl("BarrelClamp", (0, 0.018, 0.5), 0.02, 0.012, "blued", seg=10, bevel=0.001)
box("ClampWeb", (0, 0.018, 0.5), (0.012, 0.034, 0.012), "blued", bevel=0.001)
cyl("MagCap", (0, 0.0, 0.508), 0.0145, 0.02, "steel", seg=10, bevel=0.001)
cyl("BarrelRing", (0, 0.035, 0.125), 0.0185, 0.012, "steel", seg=14)
# Wooden stock: one piece from the receiver's back through a pistol-grip wrist to the butt, checkered wrist,
# a rubber recoil pad and a sling swivel.
stock = [(-0.117, 0.047), (-0.13, 0.042), (-0.2, 0.03), (-0.32, 0.018), (-0.43, 0.012),   # comb, top line
         (-0.432, -0.098), (-0.36, -0.086), (-0.24, -0.06),                                 # butt, toe line
         (-0.175, -0.05), (-0.155, -0.075), (-0.13, -0.088), (-0.098, -0.082),               # pistol-grip bottom
         (-0.075, -0.045), (-0.117, -0.02)]                                                  # grip front, into receiver
profile("Stock", [(z, y) for z, y in stock], -0.021, 0.021, "wood", bevel=0.006)
box("Checker", (0.0212, -0.052, -0.118), (0.002, 0.04, 0.05), "wooddark", rx=-35, bevel=0)
box("CheckerL", (-0.0212, -0.052, -0.118), (0.002, 0.04, 0.05), "wooddark", rx=-35, bevel=0)
box("GripCap", (0, -0.087, -0.118), (0.036, 0.006, 0.036), "rubber", rx=-12, bevel=0.002)
box("RecoilPad", (0, -0.043, -0.44), (0.044, 0.114, 0.018), "rubber", bevel=0.005)
box("PadSpacer", (0, -0.043, -0.4305), (0.043, 0.112, 0.003), "steel", bevel=0)
box("Swivel", (0, -0.094, -0.37), (0.005, 0.012, 0.014), "steel", bevel=0.001)
box("StockBolt", (0, 0.03, -0.121), (0.03, 0.02, 0.01), "blued", bevel=0.002)
ob = export(os.path.join(a.out, "shotgun.glb"))
if a.preview: preview(ob, a.preview + "_shotgun")
