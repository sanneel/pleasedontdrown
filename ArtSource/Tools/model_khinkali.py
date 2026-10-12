# Khinkali on a plate for the hotel bar menu (Assets/_Game/Art/Props/khinkali.glb).
# Three dumplings: round bottoms, pleated tops pinched into a knot. Origin at the plate's bottom centre, +Y up in Unity.
# blender -b -P model_khinkali.py -- <out.glb>
import bpy, bmesh, math, sys, mathutils
out = sys.argv[sys.argv.index("--") + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)

def mat(name, rgb, rough=0.7):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = rough
    return m

dough = mat("dough", (0.86, 0.72, 0.5))
plate = mat("plate_white", (0.93, 0.93, 0.9), 0.35)
rim = mat("plate_rim", (0.2, 0.45, 0.55), 0.35)

# The plate: a shallow dish with a blue rim line.
bpy.ops.mesh.primitive_cylinder_add(vertices=40, radius=0.1, depth=0.012, location=(0, 0, 0.006))
p = bpy.context.object; p.name = "Plate"; p.data.materials.append(plate)
bpy.ops.mesh.primitive_torus_add(major_radius=0.097, minor_radius=0.004, major_segments=40, minor_segments=8, location=(0, 0, 0.013))
r = bpy.context.object; r.name = "Rim"; r.data.materials.append(rim)

def khinkali(x, y, yaw, size):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=36, ring_count=18, radius=1.0, location=(0, 0, 0))
    o = bpy.context.object
    me = o.data
    for v in me.vertices:
        c = v.co
        z = c.z
        theta = math.atan2(c.y, c.x)
        rad = math.hypot(c.x, c.y)
        if z < 0:
            # A soft round bottom sitting flat.
            nz = z * 0.55
            nr = rad * 1.02
        else:
            # Pleats round the top, pinched in toward the knot.
            pinch = 1.0 - 0.82 * (z ** 1.6)
            pleat = 1.0 + 0.13 * math.sin(16 * theta + z * 3.0) * z
            nr = rad * pinch * pleat
            nz = z * 0.95
        c.x = math.cos(theta) * nr
        c.y = math.sin(theta) * nr
        c.z = nz
    o.scale = (size, size, size)
    o.location = (x, y, 0.012 + 0.55 * size)
    o.rotation_euler = (0, 0, yaw)
    me.materials.append(dough)
    for f in me.polygons: f.use_smooth = True
    # The knot on top (you hold it to eat, and leave it on the plate).
    bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=size * 0.22, radius2=size * 0.12, depth=size * 0.5,
                                    location=(x, y, 0.012 + 0.55 * size + 0.95 * size + size * 0.2))
    k = bpy.context.object; k.data.materials.append(dough)
    return [o, k]

parts = []
for i, (a, s) in enumerate([(90, 0.034), (210, 0.032), (330, 0.033)]):
    t = math.radians(a)
    parts += khinkali(math.cos(t) * 0.045, math.sin(t) * 0.045, t, s)

bpy.ops.object.select_all(action='SELECT')
bpy.context.view_layer.objects.active = p
bpy.ops.object.join()
o = bpy.context.object; o.name = "khinkali"; o.data.name = "khinkali"
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', use_selection=True)
dims = o.dimensions
print("KHINKALI", [round(d, 3) for d in dims])
