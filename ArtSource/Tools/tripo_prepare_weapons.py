"""Pirate weapons (Tripo, ArtSource/Tripo/Weapons) -> Assets/_Game/Resources/NpcWeapons/<name>.glb, held by the
pirates' hands (Story/StoryNpc.Weapon.cs).

Each model is turned so that it points forward (Unity +Z = Blender -Y), the top up and the trigger / edge down,
scaled to its real length and cut to a game polycount. The origin goes to the right hand's palm point on the grip;
empties mark the muzzle (guns), the support hand on the fore-end (the musket) and the blade's tip (the cutlass).

Blender -b --factory-startup -P ArtSource/Tools/tripo_prepare_weapons.py -- [--measure] [names...]
  --measure  print a profile along the length (to place the grip) instead of exporting.

The positions below are fractions of the length from the back end (u) and of the height from the bottom (v),
read off the --measure profile and the side views (ArtSource/Tools/tripo_sides.py).
"""
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC = os.path.join(ROOT, "ArtSource", "Tripo", "Weapons")
OUT = os.path.join(ROOT, "Assets", "_Game", "Resources", "NpcWeapons")

# length (m), triangles, flip the long axis, flip up, grip (u, v), extra empties {name: (u, v)}
WEAPONS = {
    "flintlock_pistol": dict(length=0.42, tris=6000, flip=False, up_flip=False, level=True, lock=(0.0, 0.12), grip=(0.12, 0.30),
                             marks={"Muzzle": (1.0, None)}),
    "flintlock_musket": dict(length=1.45, tris=8000, flip=False, up_flip=False, level=True, lock=(0.0, 0.12), grip=(0.25, 0.50),
                             marks={"Muzzle": (1.0, None), "GripLeft": (0.55, 0.30)}),
    "cutlass": dict(length=0.85, tris=6000, flip=False, up_flip=False, level=False, lock=None, grip=(0.09, 0.50),
                    marks={"Tip": (1.0, None)}),
}

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
MEASURE = "--measure" in args
PREVIEW = args[args.index("--preview") + 1] if "--preview" in args else None
ONLY = [a for a in args if not a.startswith("--") and a != PREVIEW]


def load(name):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=os.path.join(SRC, name + ".glb"))
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    # Unparent from any import root, keeping the world transform, then bake it in.
    mw = obj.matrix_world.copy()
    obj.parent = None
    obj.matrix_world = mw
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for o in list(bpy.context.scene.objects):
        if o != obj:
            bpy.data.objects.remove(o, do_unlink=True)
    return obj


def verts(obj):
    return np.array([v.co[:] for v in obj.data.vertices])


def align(obj, cfg):
    """Long axis -> -Y (forward), the flat side's normal -> X, the rest (height) -> Z."""
    p = verts(obj)
    c = p.mean(axis=0)
    _, _, vt = np.linalg.svd(p - c, full_matrices=False)
    along, height, thin = vt[0], vt[1], vt[2]
    # Muzzle / tip end: the thinner end (a gun's butt and a sword's guard are bulky).
    u = (p - c) @ along
    lo, hi = u.min(), u.max()
    span = hi - lo
    h = (p - c) @ height
    back = np.ptp(h[u < lo + span * 0.15])
    front = np.ptp(h[u > hi - span * 0.15])
    if front > back:
        along = -along
    if cfg["flip"]:
        along = -along
    # Up: the barrel / back of the blade runs straight along the top; the grip / edge hangs below. The side whose
    # extreme stays nearly constant along the length is the top.
    u = (p - c) @ along
    h = (p - c) @ height
    bins = np.linspace(u.min(), u.max(), 21)
    tops, bots = [], []
    for a, b in zip(bins[:-1], bins[1:]):
        m = (u >= a) & (u < b)
        if m.sum() > 3:
            tops.append(h[m].max())
            bots.append(h[m].min())
    if np.std(tops) > np.std(bots):
        height = -height
    if cfg["up_flip"]:
        height = -height
    side = np.cross(height, along)  # right-handed frame
    # Rows: where each axis goes. along -> -Y, height -> +Z, side -> +X? (Blender X maps to Unity -X.)
    target = np.array([[0, -1, 0], [0, 0, 1], [1, 0, 0]], dtype=float)
    source = np.array([along, height, side])
    rot = target.T @ source
    if np.linalg.det(rot) < 0:
        rot[:, 0] = -rot[:, 0]
    m = Matrix(rot.tolist()).to_4x4() @ Matrix.Translation(Vector((-c).tolist()))
    obj.data.transform(m)
    obj.data.update()


def scale_to(obj, length):
    p = verts(obj)
    s = length / (p[:, 1].max() - p[:, 1].min())
    obj.data.transform(Matrix.Scale(s, 4))
    obj.data.update()


def level(obj, cfg):
    """Guns: the barrel level (PCA tilts it toward the butt), then the butt down."""
    p = verts(obj)
    y0, y1 = p[:, 1].max(), p[:, 1].min()
    L = y0 - y1
    if cfg.get("level"):
        ys, zs = [], []
        for u in np.linspace(0.62, 0.97, 12):
            s = slice_mid(p, y0 - L * u, L / 40)
            if s is not None:
                ys.append(s[:, 1].mean())
                zs.append((s[:, 2].min() + s[:, 2].max()) / 2)
        b = np.polyfit(ys, zs, 1)[0]
        ang = np.arctan(b)
        obj.data.transform(Matrix.Rotation(-ang, 4, "X"))
        obj.data.update()
        p = verts(obj)
    # The line through the front (barrel) and how far the butt reaches above and below it: a butt drops.
    if cfg["lock"] is None:
        return
    s_front = slice_mid(p, y0 - L * 0.85, L / 30)
    line = (s_front[:, 2].min() + s_front[:, 2].max()) / 2
    a, b = cfg["lock"]
    m = (p[:, 1] < y0 - L * a) & (p[:, 1] > y0 - L * b)
    above, below = p[m, 2].max() - line, line - p[m, 2].min()
    if above > below:
        obj.data.transform(Matrix.Rotation(np.pi, 4, "Y"))
        obj.data.update()


def decimate(obj, tris):
    n = sum(len(f.vertices) - 2 for f in obj.data.polygons)
    if n <= tris:
        return
    mod = obj.modifiers.new("dec", "DECIMATE")
    mod.ratio = tris / n
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=mod.name)


def frame(obj):
    """u: 0 at the back end, 1 at the front (forward is -Y); v: 0 at the bottom, 1 at the top."""
    p = verts(obj)
    y0, y1 = p[:, 1].max(), p[:, 1].min()
    z0, z1 = p[:, 2].min(), p[:, 2].max()
    return p, (lambda u: y0 + (y1 - y0) * u), (lambda v: z0 + (z1 - z0) * v), (y0, y1, z0, z1)


def slice_mid(p, y, width):
    m = np.abs(p[:, 1] - y) < width
    if m.sum() == 0:
        return None
    return p[m]


def measure(name, obj):
    p, Y, Z, (y0, y1, z0, z1) = frame(obj)
    print(f"[weapons] {name}: length {abs(y1 - y0):.3f}  height {z1 - z0:.3f}  width {np.ptp(p[:, 0]):.3f}")
    for i in range(21):
        u = i / 20
        s = slice_mid(p, Y(u), abs(y1 - y0) / 40)
        if s is None:
            continue
        v_lo = (s[:, 2].min() - z0) / (z1 - z0)
        v_hi = (s[:, 2].max() - z0) / (z1 - z0)
        print(f"  u {u:.2f}: v {v_lo:.2f}..{v_hi:.2f}  x {s[:, 0].min():+.3f}..{s[:, 0].max():+.3f}")


def point(p, Y, Z, z0, z1, u, v, length):
    y = Y(u)
    s = slice_mid(p, y, length / 40)
    if v is None:
        # The middle of the slice's top part (the barrel / the blade's line).
        top = s[s[:, 2] > s[:, 2].max() - 0.04 * length] if u < 1.0 else s
        z = top[:, 2].mean() if u < 1.0 else s[:, 2].mean()
    else:
        z = z0 + (z1 - z0) * v
    x = s[:, 0].mean()
    return Vector((x, y, z))


def build(name, cfg):
    obj = load(name)
    align(obj, cfg)
    scale_to(obj, cfg["length"])
    level(obj, cfg)
    scale_to(obj, cfg["length"])
    decimate(obj, cfg["tris"])
    if MEASURE:
        measure(name, obj)
        return
    p, Y, Z, (y0, y1, z0, z1) = frame(obj)
    L = cfg["length"]
    gu, gv = cfg["grip"]
    grip = point(p, Y, Z, z0, z1, gu, gv, L)
    marks = {k: point(p, Y, Z, z0, z1, u, v, L) for k, (u, v) in cfg["marks"].items()}
    if "Muzzle" in marks:
        marks["Muzzle"].y -= 0.005
    # Origin on the grip.
    obj.data.transform(Matrix.Translation(-grip))
    obj.data.update()
    obj.name = name
    for k, at in marks.items():
        e = bpy.data.objects.new(k, None)
        e.location = at - grip
        bpy.context.scene.collection.objects.link(e)
        e.parent = obj
        print(f"[weapons] {name}: {k} at {tuple(round(c, 3) for c in e.location)} (Blender, from the grip)")
    for mat in obj.data.materials:
        if mat and mat.use_nodes:
            bsdf = mat.node_tree.nodes.get("Principled BSDF")
            if bsdf:
                bsdf.inputs["Roughness"].default_value = max(0.35, bsdf.inputs["Roughness"].default_value)
    os.makedirs(OUT, exist_ok=True)
    out = os.path.join(OUT, name + ".glb")
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", use_selection=True, export_apply=True,
                              export_yup=True)
    tris = sum(len(f.vertices) - 2 for f in obj.data.polygons)
    print(f"[weapons] {name}: {tris} triangles -> {out}")
    if PREVIEW:
        preview(name, obj, [Vector((0, 0, 0))] + [e.location.copy() for e in obj.children])


def preview(name, obj, points):
    """A side view (from the gun's right, Blender -X) with red balls on the grip and the marks."""
    import math
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = 900, 360
    scene.world = bpy.data.worlds.new("w")
    scene.world.color = (0.55, 0.62, 0.7)
    red = bpy.data.materials.new("red")
    red.diffuse_color = (1, 0, 0, 1)
    red.use_nodes = True
    red.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (1, 0, 0, 1)
    red.node_tree.nodes["Principled BSDF"].inputs["Emission Color"].default_value = (1, 0, 0, 1)
    red.node_tree.nodes["Principled BSDF"].inputs["Emission Strength"].default_value = 3
    L = WEAPONS[name]["length"]
    for pt in points:
        bpy.ops.mesh.primitive_uv_sphere_add(radius=L * 0.012, location=pt)
        bpy.context.active_object.data.materials.append(red)
    p = verts(obj)
    c = Vector(((p[:, 0].min() + p[:, 0].max()) / 2, (p[:, 1].min() + p[:, 1].max()) / 2, (p[:, 2].min() + p[:, 2].max()) / 2))
    for side, sx in (("right", -1), ("left", 1)):
        cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
        cam.data.type = "ORTHO"
        cam.data.ortho_scale = L * 1.1
        cam.location = c + Vector((sx * 2, 0, 0))
        cam.rotation_euler = (math.pi / 2, 0, sx * math.pi / 2)
        scene.collection.objects.link(cam)
        scene.camera = cam
        sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
        sun.rotation_euler = (0.6, sx * 0.8, 0)
        scene.collection.objects.link(sun)
        os.makedirs(PREVIEW, exist_ok=True)
        scene.render.filepath = os.path.join(PREVIEW, f"{name}_{side}.png")
        bpy.ops.render.render(write_still=True)


for name, cfg in WEAPONS.items():
    if ONLY and name not in ONLY:
        continue
    build(name, cfg)
