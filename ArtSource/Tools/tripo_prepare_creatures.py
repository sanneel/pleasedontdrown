"""Tripo creatures -> game GLBs for Creature.Body (Assets/_Game/Resources/CreatureModels/<kind>.glb).

Blender -b --factory-startup -P ArtSource/Tools/tripo_prepare_creatures.py [-- <name> ...]

Each model is turned to face Blender -Y (glTF +Z, Unity +Z), sized to the game's creature, feet on the origin, its
triangles cut down, and split into the parts the game moves: Body, Head, Jaw, Tail (each part's origin is its hinge),
plus empties the game reads (HatSpot: on the monkey's head; Eyes: in front of the Kleptopus's shades).
Sources: ArtSource/Tripo/island3/{jungle_monkey,alligator,old_snapper}.glb, ArtSource/Tripo/Island2/kleptopus.glb.
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "_Game", "Resources", "CreatureModels")

# front: the Blender axis the model faces as generated; size: (which extent, metres); tris: budget.
# head: the head starts this far back from the front (fraction of the length); jaw: the mouth line, as a fraction of
# the head's own height at each slice (faces under it go with the jaw); tail: ends this far from the back.
CREATURES = {
    "monkey": dict(src="island3/jungle_monkey.glb", front="+Y", size=("height", 0.78), tris=12000, hat=True),
    "alligator": dict(src="island3/alligator.glb", front="+X", size=("length", 3.6), tris=15000, align=True,
                      head=0.17, jaw=0.42, tail=0.4, tail_below=1.0, tail_half=0.24),
    "old_snapper": dict(src="island3/old_snapper.glb", front="-Y", size=("length", 7.4), tris=25000, align=True,
                        head=0.2, jaw=0.4, tail=0.27, tail_below=0.42),
    "kleptopus": dict(src="Island2/kleptopus.glb", front="+X", size=("height", 4.7), tris=25000, eyes=True),
}
TURN = {"-Y": 0.0, "+Y": 180.0, "+X": -90.0, "-X": 90.0}


def log(*a):
    print("[creatures]", *a)


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=path)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    obj.parent = None
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for o in list(bpy.context.scene.objects):
        if o is not obj:
            bpy.data.objects.remove(o, do_unlink=True)
    return obj


def place(obj, spec):
    me = obj.data
    me.transform(Matrix.Rotation(math.radians(TURN[spec["front"]]), 4, "Z"))
    if spec.get("align"):
        # Long animals: turn the long axis (the body's main direction seen from above) onto Y exactly.
        n = len(me.vertices)
        mx = sum(v.co.x for v in me.vertices) / n
        my = sum(v.co.y for v in me.vertices) / n
        sxx = sum((v.co.x - mx) ** 2 for v in me.vertices)
        syy = sum((v.co.y - my) ** 2 for v in me.vertices)
        sxy = sum((v.co.x - mx) * (v.co.y - my) for v in me.vertices)
        angle = 0.5 * math.atan2(2 * sxy, sxx - syy)  # the long axis' angle from +X
        me.transform(Matrix.Rotation(math.pi / 2 - angle, 4, "Z"))
        # Still facing -Y (the coarse turn got the side right): the end with more mass high up is the back... keep
        # it simple: if the turn flipped the model, turn it back.
        if abs(math.pi / 2 - angle) > math.pi / 2:
            me.transform(Matrix.Rotation(math.pi, 4, "Z"))
        log("aligned by", round(math.degrees(math.pi / 2 - angle), 1), "degrees")
    xs = [v.co.x for v in me.vertices]
    ys = [v.co.y for v in me.vertices]
    zs = [v.co.z for v in me.vertices]
    kind, metres = spec["size"]
    extent = (max(zs) - min(zs)) if kind == "height" else (max(ys) - min(ys))
    s = metres / extent
    centre = Vector(((max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2, min(zs)))
    me.transform(Matrix.Diagonal((s, s, s, 1.0)) @ Matrix.Translation(-centre))
    me.update()


def decimate(obj, tris):
    have = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    if have <= tris:
        return
    mod = obj.modifiers.new("dec", "DECIMATE")
    mod.ratio = tris / have
    mod.use_collapse_triangulate = True
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=mod.name)
    log(obj.name, "triangles", have, "->", sum(len(p.vertices) - 2 for p in obj.data.polygons))


def slice_heights(me, u):
    """Bottom and top of the mesh in a thin slice across the forward axis at u (u = -y: forward)."""
    zs = [v.co.z for v in me.vertices if abs(-v.co.y - u) < 0.03]
    return (min(zs), max(zs)) if zs else (0.0, 0.0)


PARTS = {"Jaw": 1, "Head": 2, "Tail": 3}


def tag(obj, groups):
    """Writes each face's part (PARTS) into a face attribute: indices change as parts are split off."""
    attr = obj.data.attributes.new("part", "INT", "FACE")
    for name, faces in groups.items():
        for i in faces:
            attr.data[i].value = PARTS[name]


def split(obj, name, origin):
    """Moves the faces tagged name into their own object named name, its origin at origin (Blender coords)."""
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="DESELECT")
    bpy.ops.object.mode_set(mode="OBJECT")
    attr = obj.data.attributes["part"]
    faces = [p.index for p in obj.data.polygons if attr.data[p.index].value == PARTS[name]]
    for p in obj.data.polygons:
        p.select = attr.data[p.index].value == PARTS[name]
    before = set(bpy.context.scene.objects)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.separate(type="SELECTED")
    bpy.ops.object.mode_set(mode="OBJECT")
    part = [o for o in bpy.context.scene.objects if o not in before][0]
    part.name = part.data.name = name
    part.data.transform(Matrix.Translation(-origin))
    part.location = origin
    log(name, len(faces), "faces, hinge", tuple(round(c, 2) for c in origin))
    return part


def empty(name, at):
    e = bpy.data.objects.new(name, None)
    e.location = at
    bpy.context.scene.collection.objects.link(e)
    log(name, tuple(round(c, 2) for c in at))


def build(name, spec):
    obj = load(os.path.join(ROOT, "ArtSource", "Tripo", spec["src"]))
    obj.name = obj.data.name = "Body"
    place(obj, spec)
    decimate(obj, spec["tris"])
    me = obj.data
    us = [-v.co.y for v in me.vertices]
    front, back = max(us), min(us)
    length = front - back
    height = max(v.co.z for v in me.vertices)

    if "head" in spec:
        u_head = front - spec["head"] * length
        u_tail = back + spec["tail"] * length
        centres = {p.index: (-p.center.y, p.center.z) for p in me.polygons}
        # The mouth line: per slice through the head, a fraction of the way up from the slice's bottom.
        mouth = {}
        def mouth_at(u):
            key = round(u, 2)
            if key not in mouth:
                lo, hi = slice_heights(me, key)
                mouth[key] = lo + spec["jaw"] * (hi - lo)
            return mouth[key]
        head = {i for i, (u, z) in centres.items() if u > u_head}
        jaw = {i for i in head if centres[i][1] < mouth_at(centres[i][0])}
        half = spec.get("tail_half", 99.0)  # near the cut: narrower than the hind feet
        tail = {i for i, (u, z) in centres.items()
                if u < u_tail and z < spec["tail_below"] * height and (abs(me.polygons[i].center.x) < half or u < u_tail - 0.6)}
        lo, hi = slice_heights(me, u_head)
        hinge = Vector((0.0, -u_head, lo + spec["jaw"] * (hi - lo)))
        tlo, thi = slice_heights(me, u_tail)
        tag(obj, {"Jaw": jaw, "Head": head - jaw, "Tail": tail})
        split(obj, "Jaw", hinge)
        split(obj, "Head", Vector((0.0, -u_head, (lo + hi) / 2)))
        split(obj, "Tail", Vector((0.0, -u_tail, (tlo + thi) / 2)))
        for o in bpy.context.scene.objects:
            if o.type == "MESH" and "part" in o.data.attributes:
                o.data.attributes.remove(o.data.attributes["part"])
    if spec.get("hat"):
        # The top of the head: the highest point in the front third.
        top = max((v.co for v in me.vertices if -v.co.y > front - 0.33 * length), key=lambda c: c.z)
        empty("HatSpot", Vector((0.0, top.y, top.z - 0.02)))
    if spec.get("eyes"):
        # In front of the shades: the front-most point in the band where the face is (55-75 % of the height).
        band = [v.co for v in me.vertices if 0.5 * height < v.co.z < 0.72 * height]
        face = min(band, key=lambda c: c.y)
        empty("Eyes", Vector((0.0, face.y - 0.05, face.z)))

    os.makedirs(OUT, exist_ok=True)
    out = os.path.join(OUT, name + ".glb")
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", use_selection=True, export_apply=True,
                              export_yup=True, export_image_format="AUTO")
    log("wrote", out, "length", round(length, 2), "height", round(height, 2))


names = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(CREATURES)
for n in names:
    build(n, CREATURES[n])
