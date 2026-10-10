"""Island 3 Tripo models -> game GLBs: front turned to face Blender -Y (glTF +Z, Unity +Z), real size, feet at the
origin, one-sided materials, openings cut where the game needs to walk or swim in, and polished: smooth shading with
crisp edges (weighted normals; vertices are not moved, so nothing tears at the texture seams), a softer bump map and
matte surfaces instead of Tripo's plastic sheen.

Blender -b --factory-startup -P ArtSource/Tools/tripo_prepare_island3.py
Sources: ArtSource/Tripo/island3/*.glb (decoded by tripo_decode.py). Output: Assets/_Game/Art/Tripo/Island3/*.glb
"""
import json
import math
import os

import bmesh
import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC = os.path.join(ROOT, "ArtSource", "Tripo", "island3")
OUT = os.path.join(ROOT, "Assets", "_Game", "Art", "Tripo", "Island3")

# The shacks are walk-in: the game builds a plank room inside each one. The room is measured on the sized model (before
# any cut): its floor from below, each wall by many rays at standing height, taking the wall itself, not the porch
# posts and rails in front of it. Written to shack_rooms.json for the builder (GameSceneBuilder.Island3).
SHACK_ROOMS = {}


def measure_room(me, name):
    spec = DOORS[name]
    bvh = BVHTree.FromPolygons([v.co.copy() for v in me.vertices], [list(p.vertices) for p in me.polygons])
    if spec["floor"] == "below":
        floor = bvh.ray_cast(Vector((0.0, 0.0, -10.0)), Vector((0.0, 0.0, 1.0)))[0].z + 0.15
    else:
        floor = bvh.ray_cast(Vector((spec["x"], 0.0, 4.5)), Vector((0.0, 0.0, -1.0)))[0].z + 0.02  # mid-hall, under its ceiling
    walls = {}
    for axis, sign in (("x", -1), ("x", 1), ("y", -1), ("y", 1)):
        hits = []
        for k in range(17):
            t = -1.2 + 2.4 * k / 16
            if axis == "y" and sign == -1 and abs(t - spec["x"]) < spec["half"] + 0.3:
                t = spec["x"] + math.copysign(spec["half"] + 0.3 + abs(t - spec["x"]), t - spec["x"] or 1.0)  # beside the door, not through it
            for z in (floor + 1.2, floor + 1.8):
                d = Vector((-sign, 0, 0)) if axis == "x" else Vector((0, -sign, 0))
                start = (Vector((sign * 20, t, z)) if axis == "x" else Vector((t, sign * 20, z)))
                loc = bvh.ray_cast(start, d)[0]
                if loc is not None:
                    hits.append(abs(loc.x if axis == "x" else loc.y))
        hits.sort()
        walls[(axis, sign)] = hits[len(hits) // 4] - 0.25  # the inner quarter: the wall, not posts in front of it
    top = floor + spec["room"]
    front = spec.get("front", -walls[("y", -1)])
    return (-walls[("x", -1)], walls[("x", 1)]), (front, walls[("y", 1)]), floor, top


# Walk-in models: door centre x, half width, height above the room floor, room height, and where the floor is found
# ("below": a ray up from under the model, for stilt shacks; "door": a ray down just inside the door).
DOORS = {
    "pirate_shack_thatch": dict(x=0.0, half=0.6, top=2.1, room=2.3, floor="below"),
    "pirate_shack_tarp": dict(x=0.0, half=0.6, top=2.1, room=2.3, floor="below"),
    "pirate_shack_shingle": dict(x=0.0, half=0.6, top=2.1, room=2.3, floor="below"),
    # The tavern's arch is already open in the model (no frame added) and its front windows let the measuring rays
    # through, so its front wall (y -4.5 on the sized model) is given. The bar room is a tall hall.
    "pirate_tavern": dict(x=0.2, half=1.35, top=2.9, room=3.6, floor="door", front=-4.2, frame=False, porch=-8.9),
}


def shack_cut(name):
    def cut(x, y, z):
        (x0, x1), (y0, y1), floor, top = SHACK_ROOMS[name]
        d = DOORS[name]
        inside = x0 < x < x1 and y0 < y < y1 and floor + 0.05 < z < top
        door = abs(x - d["x"]) < d["half"] and floor - 0.05 < z < floor + d["top"] and y0 - 0.8 < y < y0 + 0.05
        # Ropes and rails strung across the porch in front of the door.
        porch = abs(x - d["x"]) < d["half"] and floor + 0.6 < z < floor + d["top"] and y0 - 2.6 < y <= y0 - 0.8
        return inside or door or porch
    return cut


def slice_box(bm, lo, hi, margin=0.4):
    """Slices every face reaching into the box lo..hi along the box's six planes first, so cutting by face centre leaves
    straight clean edges instead of the jagged teeth big triangles leave. UVs are interpolated, so no texture tears."""
    for axis in range(3):
        for value in (lo[axis], hi[axis]):
            near = [f for f in bm.faces if all(lo[i] - margin < c < hi[i] + margin for i, c in
                                               enumerate(f.calc_center_median()) if i != axis)
                    and min(v.co[axis] for v in f.verts) < value < max(v.co[axis] for v in f.verts)]
            if not near:
                continue
            edges = list({e for f in near for e in f.edges})
            verts = list({v for f in near for v in f.verts})
            no = Vector((0, 0, 0)); no[axis] = 1.0
            co = Vector((0, 0, 0)); co[axis] = value
            bmesh.ops.bisect_plane(bm, geom=near + edges + verts, plane_co=co, plane_no=no)


def wood_material(name, colour):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*colour, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.85
    return mat


def add_box(bm, lo, hi, material):
    lo, hi = Vector(lo), Vector(hi)
    geom = bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.LocRotScale((lo + hi) / 2, None, hi - lo))
    for f in {f for v in geom["verts"] for f in v.link_faces}:
        f.material_index = material


def frame_door(obj, bm, name):
    """A plank door frame round the opening (two posts, a lintel, a sill), deep enough to hide the wall's cut edge. The
    game hangs a real door (opens and shuts) in it."""
    (x0, x1), (y0, y1), floor, top = SHACK_ROOMS[name]
    obj.data.materials.append(wood_material("Door frame", (0.075, 0.035, 0.015)))
    frame = len(obj.data.materials) - 1
    d = DOORS[name]
    front, back = y0 - 0.6, y0 + 0.08
    cx, w, h, t = d["x"], d["half"], d["top"], 0.16
    add_box(bm, (cx - w - t, front, floor - 0.12), (cx - w, back, floor + h + t), frame)
    add_box(bm, (cx + w, front, floor - 0.12), (cx + w + t, back, floor + h + t), frame)
    add_box(bm, (cx - w - t - 0.06, front - 0.05, floor + h), (cx + w + t + 0.06, back, floor + h + t + 0.04), frame)
    add_box(bm, (cx - w, front, floor - 0.12), (cx + w, back, floor - 0.04), frame)
    # The thatch shack's ladder came out of Tripo untextured grey: paint it plank brown.
    if name == "pirate_shack_thatch":
        obj.data.materials.append(wood_material("Ladder", (0.17, 0.075, 0.03)))
        for f in bm.faces:
            c = f.calc_center_median()
            if abs(c.x) < 0.8 and c.z < floor - 0.02 and c.y < y0 - 1.75:
                f.material_index = len(obj.data.materials) - 1


# name: (degrees about Z that turn its front to -Y, size, cut)
# size: a number = uniform, fitted to the axis given by "fit"; a tuple = exact (width X, depth Y, height Z) in metres.
# cut(x, y, z) on the sized model (feet at 0, centred) -> True deletes that face.
MODELS = {
    "pirate_shack_thatch": dict(turn=-90, fit=("x", 6.0), cut=shack_cut("pirate_shack_thatch")),
    "pirate_shack_tarp": dict(turn=-90, fit=("x", 6.0), cut=shack_cut("pirate_shack_tarp")),
    "pirate_shack_shingle": dict(turn=0, fit=("x", 6.0), cut=shack_cut("pirate_shack_shingle")),
    # The big bar: its timber house (9 x 7.7 m in the model) stretched to a 2 x 2 houses block, about 12 x 11 m.
    "pirate_tavern": dict(turn=-90, size=(17.3, 22.1, 18.5), cut=shack_cut("pirate_tavern")),
    "castle_round_tower": dict(turn=0, fit=("z", 24.0)),
    "castle_gatehouse": dict(turn=-90, fit=("x", 24.0)),
    # The keep is a shell over the game's walk-in hall (16 x 14 m, 12 m high; the model's walls stand at ~60% of its
    # half-size): open its front door, and clear out
    # everything inside the hall (its other doors' recesses) so only the outer walls are left round the hall.
    "castle_keep": dict(turn=-90, size=(30.0, 24.0, 26.0),
                        cut=lambda x, y, z: (abs(x) < 2.4 and z < 6.4 and y < -6.0) or (abs(x) < 7.9 and abs(y) < 6.6 and z < 12.5)),
    # Long and low like the source (it is ~4.5x longer than tall): 30 m pieces keep the stonework's proportions.
    "castle_wall": dict(turn=90, size=(30.0, 3.0, 10.5)),
    "pirate_throne": dict(turn=-90, fit=("z", 3.3)),
    # A rock facade in front of the money cave: the arch is cut straight through so you swim into the cave behind.
    "sea_cave_entrance": dict(turn=-90, size=(28.0, 7.0, 21.0),
                              cut=lambda x, y, z: abs(x) < 4.6 and z < 10.0),
    "treasure_chest_open": dict(turn=0, fit=("z", 1.4)),
    "gold_coin_pile": dict(turn=0, fit=("x", 3.0)),
    "money_sack": dict(turn=-90, fit=("z", 1.1)),
    "casino_chip_stacks": dict(turn=0, fit=("z", 0.5)),
    "sea_rock_pinnacle": dict(turn=0, fit=("z", 20.0)),
    # The jungle (hundreds of copies): fewer triangles, and the leaves drawn from both sides.
    "jungle_tree": dict(turn=0, fit=("z", 10.0), decimate=6000, two_sided=True),
    "jungle_bush": dict(turn=0, fit=("z", 1.8), decimate=2500, two_sided=True),
    "jungle_fern": dict(turn=0, fit=("z", 1.3), decimate=1500, two_sided=True),
}

os.makedirs(OUT, exist_ok=True)
for name, spec in MODELS.items():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=os.path.join(SRC, name + ".glb"), merge_vertices=True)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    for o in [o for o in bpy.context.scene.objects if o != obj]:
        bpy.data.objects.remove(o)
    obj.parent = None
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    me = obj.data
    me.transform(Matrix.Rotation(math.radians(spec["turn"]), 4, "Z"))
    lo = Vector([min(v.co[i] for v in me.vertices) for i in range(3)])
    hi = Vector([max(v.co[i] for v in me.vertices) for i in range(3)])
    dims = hi - lo
    if "size" in spec:
        scale = Vector([spec["size"][i] / dims[i] for i in range(3)])
    else:
        axis, metres = spec["fit"]
        s = metres / dims["xyz".index(axis)]
        scale = Vector((s, s, s))
    centre = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
    for v in me.vertices:
        v.co = Vector(((v.co.x - centre.x) * scale.x, (v.co.y - centre.y) * scale.y, (v.co.z - centre.z) * scale.z))
    removed = 0
    if name in DOORS:
        SHACK_ROOMS[name] = measure_room(me, name)
        print(f"[prepare] {name} room: {SHACK_ROOMS[name]}")
    if "cut" in spec:
        bm = bmesh.new()
        bm.from_mesh(me)
        if name in SHACK_ROOMS:
            (x0, x1), (y0, y1), floor, top = SHACK_ROOMS[name]
            d = DOORS[name]
            slice_box(bm, (d["x"] - d["half"], y0 - 0.8, floor - 0.05), (d["x"] + d["half"], y0 + 0.05, floor + d["top"]))
            slice_box(bm, (d["x"] - d["half"], y0 - 2.6, floor + 0.6), (d["x"] + d["half"], y0 - 0.8, floor + d["top"]))
            slice_box(bm, (x0, y0, floor + 0.05), (x1, y1, top))
        doomed = [f for f in bm.faces if spec["cut"](*f.calc_center_median())]
        removed = len(doomed)
        bmesh.ops.delete(bm, geom=doomed, context="FACES")
        if name in SHACK_ROOMS and DOORS[name].get("frame", True):
            frame_door(obj, bm, name)
        if name == "pirate_tavern":
            # Tripo carved nonsense letters ("TARB") into the sign: take its front off and hang a plain board with a
            # gold rim and a gold tankard in its place.
            board = [f for f in bm.faces if -2.4 < f.calc_center_median().x < 3.6 and 7.35 < f.calc_center_median().z < 9.3
                     and f.calc_center_median().y < -5.85]
            bmesh.ops.delete(bm, geom=board, context="FACES")
            obj.data.materials.append(wood_material("Sign board", (0.09, 0.045, 0.02)))
            wood = len(obj.data.materials) - 1
            obj.data.materials.append(wood_material("Sign gold", (0.8, 0.5, 0.08)))
            gold = len(obj.data.materials) - 1
            add_box(bm, (-2.1, -6.15, 7.45), (3.3, -5.85, 8.65), wood)
            for lo_, hi_ in (((-2.2, -6.2, 8.6), (3.4, -6.1, 8.72)), ((-2.2, -6.2, 7.38), (3.4, -6.1, 7.5)),
                             ((-2.2, -6.2, 7.38), (-2.08, -6.1, 8.72)), ((3.28, -6.2, 7.38), (3.4, -6.1, 8.72))):
                add_box(bm, lo_, hi_, gold)
            add_box(bm, (0.2, -6.24, 7.7), (0.95, -6.15, 8.45), gold)    # tankard
            add_box(bm, (0.95, -6.24, 7.85), (1.2, -6.15, 8.3), gold)    # its handle
            obj.data.materials.append(wood_material("Sign foam", (0.95, 0.92, 0.8)))
            add_box(bm, (0.12, -6.26, 8.38), (1.03, -6.15, 8.55), len(obj.data.materials) - 1)  # its foam
        bm.to_mesh(me)
        bm.free()
    me.update()
    # Polish: smooth shading, edges sharper than 35 degrees stay crisp, face-area weighted normals (flat walls read flat).
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    if "decimate" in spec:
        decimate = obj.modifiers.new("Decimate", "DECIMATE")
        decimate.ratio = min(1.0, spec["decimate"] / sum(len(p.vertices) - 2 for p in me.polygons))
    bpy.ops.object.shade_auto_smooth(angle=math.radians(35))
    weighted = obj.modifiers.new("Weighted normals", "WEIGHTED_NORMAL")
    weighted.keep_sharp = True
    weighted.weight = 50
    for modifier in list(obj.modifiers):
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    for mat in me.materials:
        if not mat:
            continue
        mat.use_backface_culling = not spec.get("two_sided", False)  # exported as doubleSided
        if mat.use_nodes:
            for node in mat.node_tree.nodes:
                if node.type == "BSDF_PRINCIPLED":
                    node.inputs["Metallic"].default_value = 0.0
                    for link in list(node.inputs["Metallic"].links) + list(node.inputs["Roughness"].links):
                        mat.node_tree.links.remove(link)
                    node.inputs["Roughness"].default_value = 0.82
                elif node.type == "NORMAL_MAP":
                    node.inputs["Strength"].default_value = 0.35
    size = [round(max(v.co[i] for v in me.vertices) - min(v.co[i] for v in me.vertices), 2) for i in range(3)]
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, name + ".glb"), export_format="GLB", use_selection=False)
    print(f"[prepare] {name}: size {size} m, {tris} triangles, cut {removed} faces")

# Unity's frame: x mirrored, front is +z. x from-to, z from-to, floor, ceiling.
with open(os.path.join(OUT, "shack_rooms.json"), "w") as f:
    json.dump({"rooms": [{"name": n, "x0": -x1, "x1": -x0, "z0": -y1, "z1": -y0, "floor": fl, "top": tp,
                          "doorX": -DOORS[n]["x"], "doorWidth": 2 * DOORS[n]["half"], "doorHeight": DOORS[n]["top"],
                          "porchZ": -DOORS[n].get("porch", 0.0)}
                         for n, ((x0, x1), (y0, y1), fl, tp) in SHACK_ROOMS.items()]}, f, indent=1)
