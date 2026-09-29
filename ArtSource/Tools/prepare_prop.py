# Blender batch script: turn a raw Meshy prop/vehicle/creature GLB into a game-ready one.
#   blender -b --factory-startup -P prepare_prop.py -- <in.glb> <out.glb> --length 3 [--nose=-x|+x|-y|+y]
#       [--tris 12000] [--cut-below 0.1] [--split-tail 0.78] [--preview <prefix>]
# Result (Blender axes): nose toward -Y (= Unity +Z after glTF import), length along Y = --length metres,
# lowest point at z = 0, centred on x/y. --cut-below drops everything under that fraction of the height
# (e.g. a display stand under a boat hull). --split-tail moves the faces behind that fraction of the length
# (from the nose) into a second object "Tail" whose origin sits on the cut, so it can wag in game.
import sys, math, argparse
import bpy, bmesh, mathutils

argv = sys.argv[sys.argv.index("--") + 1:]
ap = argparse.ArgumentParser()
ap.add_argument("src"); ap.add_argument("dst")
ap.add_argument("--length", type=float, required=True)
ap.add_argument("--nose", default="-y")
ap.add_argument("--tris", type=int, default=12000)
ap.add_argument("--cut-below", type=float, default=0.0)
ap.add_argument("--split-tail", type=float, default=0.0)
ap.add_argument("--name", default="Body")
ap.add_argument("--preview", default="")
a = ap.parse_args(argv)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=a.src)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in meshes: o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1: bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active
obj.parent = None
obj.name = a.name
obj.data.name = a.name
# Bake every transform into the vertices, then turn the nose to -Y.
obj.data.transform(obj.matrix_world)
obj.matrix_world = mathutils.Matrix.Identity(4)
turn = {"-y": 0, "+x": -90, "+y": 180, "-x": 90}[a.nose]
obj.data.transform(mathutils.Matrix.Rotation(math.radians(turn), 4, 'Z'))

def bounds():
    vs = [v.co for v in obj.data.vertices]
    return (mathutils.Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs))),
            mathutils.Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs))))

mn, mx = bounds()
if a.cut_below > 0:
    cut = mn.z + (mx.z - mn.z) * a.cut_below
    bm = bmesh.new(); bm.from_mesh(obj.data)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < cut], context='VERTS')
    bm.to_mesh(obj.data); bm.free()
    mn, mx = bounds()

s = a.length / (mx.y - mn.y)
obj.data.transform(mathutils.Matrix.Translation(-mathutils.Vector(((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, mn.z))))
obj.data.transform(mathutils.Matrix.Scale(s, 4))
obj.data.update()

def tris(o): return sum(len(p.vertices) - 2 for p in o.data.polygons)
before = tris(obj)
if before > a.tris:
    mod = obj.modifiers.new("Decimate", 'DECIMATE')
    mod.decimate_type = 'COLLAPSE'; mod.ratio = a.tris / before; mod.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=mod.name)
print(f"[prop] {a.src}: {before} -> {tris(obj)} triangles")

parts = [obj]
if a.split_tail > 0:
    mn, mx = bounds()
    ycut = mn.y + (mx.y - mn.y) * a.split_tail
    tail = obj.copy(); tail.data = obj.data.copy(); tail.name = tail.data.name = "Tail"
    bpy.context.scene.collection.objects.link(tail)
    for o, keep_front in ((obj, True), (tail, False)):
        bm = bmesh.new(); bm.from_mesh(o.data)
        drop = [f for f in bm.faces if (f.calc_center_median().y > ycut) == keep_front]
        bmesh.ops.delete(bm, geom=drop, context='FACES')
        bm.to_mesh(o.data); bm.free()
    # Tail origin on the cut, at the height of the tail's root.
    root = [v.co for v in tail.data.vertices if v.co.y < ycut + 0.05 * a.length]
    pivot = mathutils.Vector((0, ycut, sum(v.z for v in root) / max(1, len(root))))
    tail.data.transform(mathutils.Matrix.Translation(-pivot))
    tail.location = pivot
    parts.append(tail)
    print(f"[prop] tail pivot {tuple(round(c, 3) for c in pivot)} (Blender axes)")

mn, mx = bounds() if a.split_tail <= 0 else (None, None)
allv = [o.matrix_world @ v.co for o in parts for v in o.data.vertices]
size = [max(v[i] for v in allv) - min(v[i] for v in allv) for i in range(3)]
print(f"[prop] size width {size[0]:.3f} length {size[1]:.3f} height {size[2]:.3f}")

bpy.ops.object.select_all(action='DESELECT')
for o in parts: o.select_set(True)
bpy.ops.export_scene.gltf(filepath=a.dst, export_format='GLB', export_image_format='AUTO', use_selection=True, export_apply=True)

if a.preview:
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 800; scene.render.resolution_y = 520
    world = bpy.data.worlds.new("w"); scene.world = world; world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.8, 0.85, 0.9, 1)
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); scene.collection.objects.link(sun)
    sun.data.energy = 3; sun.rotation_euler = (0.8, 0.2, 0.6)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scene.collection.objects.link(cam)
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = max(size) * 1.15
    scene.camera = cam
    centre = mathutils.Vector((0, 0, size[2] / 2))
    for name, d in [("side", (1, 0, 0)), ("front", (0, -1, 0)), ("top", (0, 0.001, 1)), ("persp", (1, -1, 0.6))]:
        v = mathutils.Vector(d).normalized()
        cam.location = centre + v * 20
        cam.rotation_euler = (-v).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = f"{a.preview}_{name}.png"
        bpy.ops.render.render(write_still=True)
