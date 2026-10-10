# Blender batch script: the beach's small props, modelled in code so nothing in the game is a grey box.
# Everything is written in the prop's own Unity space (metres; x right, y up, z forward) with the same sizes the
# scene builder uses for its colliders and hold poses, so a model drops in where the placeholder was:
# no rotation, scale 1 (GameSceneBuilder.Props.cs).
#   blender -b --factory-startup -P model_props.py -- <out_dir> [--only crate,phone] [--preview <abs prefix>]
# Output: <out_dir>/<prop>.glb, and <prefix>_<prop>.png when --preview is given.
import sys, os, math, argparse, random
import bpy, bmesh, mathutils
from mathutils import Vector, noise

argv = sys.argv[sys.argv.index("--") + 1:]
ap = argparse.ArgumentParser()
ap.add_argument("out")
ap.add_argument("--only", default="")
ap.add_argument("--preview", default="")
a = ap.parse_args(argv)

PAINT = {  # sRGB, all matte
    "wood": (0.62, 0.42, 0.25), "wood_light": (0.78, 0.6, 0.38), "wood_dark": (0.40, 0.26, 0.15), "wood_grey": (0.52, 0.47, 0.42),
    "rope": (0.83, 0.74, 0.55), "rope_dark": (0.55, 0.45, 0.3),
    "red": (0.86, 0.17, 0.14), "white": (0.95, 0.95, 0.93), "yellow": (1.0, 0.82, 0.2), "orange": (1.0, 0.5, 0.12),
    "blue": (0.17, 0.45, 0.85), "blue_dark": (0.11, 0.3, 0.62), "teal": (0.2, 0.72, 0.7), "green": (0.22, 0.64, 0.34),
    "black": (0.08, 0.08, 0.1), "dark": (0.18, 0.18, 0.2), "grey": (0.55, 0.57, 0.6), "steel": (0.68, 0.7, 0.73),
    "gold": (0.93, 0.74, 0.28), "brass": (0.8, 0.6, 0.25), "brass_dark": (0.55, 0.4, 0.16),
    "leather": (0.45, 0.27, 0.14), "leather_dark": (0.3, 0.17, 0.09), "stitch": (0.85, 0.72, 0.5), "cash": (0.45, 0.72, 0.42),
    "screen": (0.2, 0.5, 0.92), "screen_light": (0.62, 0.82, 1.0), "pink": (1.0, 0.45, 0.65), "lens": (0.1, 0.13, 0.18),
    "bag": (0.86, 0.9, 0.93), "powder": (0.98, 0.98, 0.96),
    "siding": (0.93, 0.93, 0.9), "siding_warm": (0.9, 0.89, 0.85), "siding_shadow": (0.66, 0.68, 0.69), "trim_blue": (0.24, 0.45, 0.63),
    "glass": (0.36, 0.6, 0.68), "roof_red": (0.8, 0.17, 0.14), "roof_red_dark": (0.56, 0.1, 0.09), "roof_orange": (0.9, 0.48, 0.2),
    "roof_orange_dark": (0.66, 0.32, 0.13), "concrete": (0.64, 0.62, 0.58), "wood_old": (0.58, 0.5, 0.41),
    "husk": (0.47, 0.3, 0.17), "husk_dark": (0.22, 0.13, 0.08), "husk_light": (0.62, 0.45, 0.28),
    "rock": (0.52, 0.52, 0.54), "rock_dark": (0.36, 0.37, 0.4), "rock_light": (0.66, 0.65, 0.64), "moss": (0.35, 0.5, 0.3),
    "cloth": (0.9, 0.9, 0.9), "seat": (0.8, 0.3, 0.25), "cream": (0.96, 0.93, 0.84), "dial": (0.12, 0.2, 0.36),
    "straw": (0.86, 0.72, 0.42), "straw_dark": (0.66, 0.52, 0.28), "straw_light": (0.94, 0.83, 0.55),
    "bamboo": (0.82, 0.7, 0.42), "bamboo_dark": (0.6, 0.48, 0.25), "teal_dark": (0.08, 0.45, 0.47),
    "bottle_green": (0.18, 0.5, 0.25), "bottle_amber": (0.7, 0.4, 0.1), "bottle_blue": (0.2, 0.42, 0.75),
    "chalk": (0.15, 0.2, 0.18),
    "beer_glass": (0.37, 0.15, 0.045), "beer_foil": (0.92, 0.67, 0.24),
    "beer_label": (0.99, 0.95, 0.80), "condensation": (0.64, 0.79, 0.81),
    "coconut_flesh": (0.98, 0.97, 0.90),
    "fries": (1.0, 0.82, 0.33), "fries_dark": (0.88, 0.6, 0.2), "can_metal": (0.8, 0.82, 0.85),
    "cocktail": (1.0, 0.42, 0.22), "cocktail_top": (1.0, 0.78, 0.3), "cocktail_glass": (0.8, 0.92, 0.95), "lime": (0.55, 0.85, 0.25),
}

def srgb_to_linear(c): return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4

def material(name):
    m = bpy.data.materials.get(name)
    if m: return m
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*[srgb_to_linear(c) for c in PAINT[name]], 1)
    b.inputs["Roughness"].default_value = {"beer_glass": 0.16, "beer_foil": 0.24, "condensation": 0.08, "coconut_flesh": 0.42,
                                           "can_metal": 0.3, "cocktail_glass": 0.1, "cocktail": 0.25}.get(name, 0.82)
    b.inputs["Metallic"].default_value = 0.75 if name in ("beer_foil", "can_metal") else 0.0
    return m

def B(p):
    """Unity (x right, y up, z forward) -> Blender, so that the glTF comes into Unity unturned."""
    return Vector((-p[0], -p[2], p[1]))

def U(v):
    """Blender -> Unity (for painting faces by where they are)."""
    return Vector((-v[0], v[2], -v[1]))

def euler(rx=0.0, ry=0.0, rz=0.0):
    """Unity's Quaternion.Euler(rx, ry, rz) as a matrix on Unity-space vectors (z, then x, then y)."""
    def m(axis, deg):
        t = math.radians(deg); c, s = math.cos(t), math.sin(t)
        if axis == 'x': return mathutils.Matrix(((1, 0, 0), (0, c, -s), (0, s, c)))
        if axis == 'y': return mathutils.Matrix(((c, 0, s), (0, 1, 0), (-s, 0, c)))
        return mathutils.Matrix(((c, -s, 0), (s, c, 0), (0, 0, 1)))
    return m('y', ry) @ m('x', rx) @ m('z', rz)

parts = []

def add(name, verts_u, faces, mat, bevel=0.0, segments=1):
    me = bpy.data.meshes.new(name)
    me.from_pydata([B(v) for v in verts_u], [], faces)
    me.update()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.data.materials.append(material(mat))
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(me); bm.free()
    if bevel > 0:
        mod = ob.modifiers.new("bevel", 'BEVEL'); mod.width = bevel; mod.segments = segments; mod.limit_method = 'ANGLE'
        mod.angle_limit = math.radians(35)
        bpy.context.view_layer.objects.active = ob
        bpy.ops.object.modifier_apply(modifier=mod.name)
    parts.append(ob)
    return ob

def paint(ob, pick):
    """Give each face the material pick(centre in Unity space, normal in Unity space) names (None keeps it)."""
    slots = {m.name: i for i, m in enumerate(ob.data.materials)}
    for p in ob.data.polygons:
        name = pick(U(p.center), U(p.normal))
        if name is None: continue
        if name not in slots:
            ob.data.materials.append(material(name)); slots[name] = len(ob.data.materials) - 1
        p.material_index = slots[name]

def text3d(name, s, centre, height, depth, mat, face='+z'):
    """Raised lettering (Blender's own font), centred on centre (Unity space), capitals height tall, depth thick,
    reading left to right for someone looking at the face it is on ('+z': seen from +z, '-x': seen from -x)."""
    cu = bpy.data.curves.new(name, 'FONT'); cu.body = s; cu.extrude = depth / 2; cu.align_x = 'CENTER'; cu.align_y = 'CENTER'
    cu.size = height * 1.38  # (capitals are about 0.72 of the font size)
    ob = bpy.data.objects.new(name, cu); bpy.context.scene.collection.objects.link(ob)
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.convert(target='MESH')
    ob = bpy.context.view_layer.objects.active
    # The font lies in Blender's XY plane facing +Z: stand it up facing the wanted Unity direction.
    turns = {'+z': (math.radians(90), 0, 0), '-z': (math.radians(90), 0, math.radians(180)),
             '+x': (math.radians(90), 0, math.radians(-90)), '-x': (math.radians(90), 0, math.radians(90))}
    ob.rotation_euler = turns[face]
    ob.location = B(centre)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    ob.data.materials.clear(); ob.data.materials.append(material(mat))
    parts.append(ob)
    return ob

def wrap_on_cylinder(ob, radius, turn=0.0):
    """Bend a flat piece made facing +z at the axis (text3d / a disc at x, z = 0) round an upright cylinder of
    `radius` (Unity space: the y axis), so it lies on a bottle's curved label instead of standing off it."""
    for v in ob.data.vertices:
        u = U(v.co)                      # Unity space
        theta = u.x / radius + turn
        out = radius + u.z
        v.co = B((math.sin(theta) * out, u.y, math.cos(theta) * out))
    ob.data.update()
    return ob

def box(name, centre, size, mat, rot=(0, 0, 0), bevel=0.004, segments=1):
    hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
    R = euler(*rot); c = Vector(centre)
    verts = [c + R @ Vector((x, y, z)) for x in (-hx, hx) for y in (-hy, hy) for z in (-hz, hz)]
    faces = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    return add(name, verts, faces, mat, bevel=min(bevel, min(size) * 0.3), segments=segments)

def lathe(name, centre, profile, mat, seg=16, axis='y', rot=(0, 0, 0), bevel=0.0, turn=0.5, caps=True):
    """A shape turned round an axis: profile = [(radius, height along the axis)...] from one end to the other."""
    R = euler(*rot); c = Vector(centre)
    def place(r, h, i):
        t = 2 * math.pi * (i + turn) / seg
        u, v = math.cos(t) * r, math.sin(t) * r
        local = Vector((u, h, v)) if axis == 'y' else Vector((h, u, v)) if axis == 'x' else Vector((u, v, h))
        return c + R @ local
    verts, faces, rings = [], [], []
    for r, h in profile:
        if r < 1e-7:
            rings.append([len(verts)]); verts.append(place(0, h, 0))
        else:
            rings.append(list(range(len(verts), len(verts) + seg))); verts += [place(r, h, i) for i in range(seg)]
    for k in range(len(rings) - 1):
        r0, r1 = rings[k], rings[k + 1]
        for i in range(seg):
            j = (i + 1) % seg
            if len(r0) == 1 and len(r1) == 1: continue
            if len(r0) == 1: faces.append((r0[0], r1[i], r1[j]))
            elif len(r1) == 1: faces.append((r0[i], r0[j], r1[0]))
            else: faces.append((r0[i], r0[j], r1[j], r1[i]))
    if caps and len(rings[0]) > 1: faces.append(tuple(rings[0]))
    if caps and len(rings[-1]) > 1: faces.append(tuple(rings[-1]))
    return add(name, verts, faces, mat, bevel=bevel)

def arc_profile(radius, y0, y1, steps, squash=1.0):
    """Half-circle-ish profile between two heights (for balls, floats, knobs)."""
    out = []
    for i in range(steps + 1):
        t = math.pi * i / steps
        out.append((math.sin(t) * radius, (y0 + y1) / 2 - math.cos(t) * (y1 - y0) / 2 * squash))
    return out

def tube(name, path, radius, mat, seg=8, closed=False, caps=True):
    """A round rod or rope along a path of Unity points."""
    pts = [Vector(p) for p in path]
    n = len(pts)
    verts, faces = [], []
    normal = None
    for i, p in enumerate(pts):
        nxt = pts[(i + 1) % n] if (closed or i < n - 1) else p
        prv = pts[(i - 1) % n] if (closed or i > 0) else p
        tangent = (nxt - prv).normalized()
        if normal is None:
            normal = tangent.orthogonal().normalized()
        else:
            normal = (normal - tangent * normal.dot(tangent))
            normal = normal.normalized() if normal.length > 1e-6 else tangent.orthogonal().normalized()
        binormal = tangent.cross(normal)
        r = radius(i / max(1, n - 1)) if callable(radius) else radius
        for k in range(seg):
            t = 2 * math.pi * k / seg
            verts.append(p + (normal * math.cos(t) + binormal * math.sin(t)) * r)
    rings = n if closed else n - 1
    for i in range(rings):
        a0, b0 = i * seg, ((i + 1) % n) * seg
        for k in range(seg):
            faces.append((a0 + k, a0 + (k + 1) % seg, b0 + (k + 1) % seg, b0 + k))
    if caps and not closed:
        faces.append(tuple(range(seg))); faces.append(tuple(range((n - 1) * seg, n * seg)))
    return add(name, verts, faces, mat)

def circle(centre, radius, n=24, axis='y', start=0.0, sweep=360.0, rx=None):
    """Points round a circle (a full turn leaves the last point off, for closed tubes)."""
    c = Vector(centre); out = []
    count = n if sweep >= 360 else n + 1
    for i in range(count):
        t = math.radians(start + sweep * i / n)
        u, v = math.cos(t) * radius, math.sin(t) * (rx if rx is not None else radius)
        out.append(c + (Vector((u, 0, v)) if axis == 'y' else Vector((0, u, v)) if axis == 'x' else Vector((u, v, 0))))
    return out

def rounded_rect(w, d, r, n=4):
    """Outline of a rounded rectangle, w wide (first axis) and d deep (second), as 2D points."""
    r = min(r, w / 2 - 1e-5, d / 2 - 1e-5); pts = []
    for cx, cz, a0 in ((w / 2 - r, d / 2 - r, 0), (-w / 2 + r, d / 2 - r, 90), (-w / 2 + r, -d / 2 + r, 180), (w / 2 - r, -d / 2 + r, 270)):
        for i in range(n + 1):
            t = math.radians(a0 + 90 * i / n)
            pts.append((cx + math.cos(t) * r, cz + math.sin(t) * r))
    return pts

def prism(name, outline, lo, hi, mat, plane='xz', centre=(0, 0, 0), rot=(0, 0, 0), bevel=0.0, segments=1, taper=1.0):
    """A flat outline pulled into a solid: plane 'xz' pulls along y, 'xy' along z, 'zy' along x."""
    R = euler(*rot); c = Vector(centre)
    def place(p, h, s):
        u, v = p[0] * s, p[1] * s
        local = Vector((u, h, v)) if plane == 'xz' else Vector((u, v, h)) if plane == 'xy' else Vector((h, v, u))
        return c + R @ local
    n = len(outline)
    verts = [place(p, lo, 1.0) for p in outline] + [place(p, hi, taper) for p in outline]
    faces = [tuple(range(n)), tuple(range(n, 2 * n))] + [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]
    return add(name, verts, faces, mat, bevel=bevel, segments=segments)

def blob(name, centre, radii, mat, subdiv=2, rough=0.1, freq=1.5, seed=0.0, rot=(0, 0, 0), flat_below=None):
    """A lumpy ball: an icosphere pushed in and out by smooth noise (rocks, a coconut, a bag of powder)."""
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    R = euler(*rot); c = Vector(centre)
    verts = []
    for v in bm.verts:
        d = v.co.normalized()
        k = 1.0 + rough * noise.noise(d * freq + Vector((seed, seed * 0.37, -seed)))
        p = Vector((d.x * radii[0] * k, d.y * radii[1] * k, d.z * radii[2] * k))
        if flat_below is not None and p.y < flat_below: p.y = flat_below
        verts.append(c + R @ p)
    faces = [tuple(v.index for v in f.verts) for f in bm.faces]
    bm.free()
    return add(name, verts, faces, mat)

def finish(name, sharp=38.0, flat=False):
    """Join the parts into one mesh named after the prop, with soft shading except across real edges."""
    global parts
    bpy.ops.object.select_all(action='DESELECT')
    for ob in parts: ob.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    if len(parts) > 1: bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = name; ob.data.name = name
    bm = bmesh.new(); bm.from_mesh(ob.data)
    if name == "defibrillator":
        # The tiny screen trace and paddle leads can create collapsed end-cap
        # faces. Remove only zero-area geometry before exporting this prop.
        bmesh.ops.dissolve_degenerate(bm, edges=list(bm.edges), dist=1e-7)
    limit = math.radians(sharp)
    for f in bm.faces: f.smooth = not flat
    for e in bm.edges:
        e.smooth = (not flat) and len(e.link_faces) == 2 and e.calc_face_angle(0.0) < limit
    bm.to_mesh(ob.data); bm.free()
    parts = [ob]
    return ob

def export(ob, path):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True, export_apply=True)
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    vs = [U(v.co) for v in ob.data.vertices]
    lo = [min(v[i] for v in vs) for i in range(3)]; hi = [max(v[i] for v in vs) for i in range(3)]
    print(f"[props] {os.path.basename(path)}: {tris} triangles, x {lo[0]:.3f}..{hi[0]:.3f}  y {lo[1]:.3f}..{hi[1]:.3f}  z {lo[2]:.3f}..{hi[2]:.3f}")

def preview(ob, path, view=(0.9, 0.75, -1.0)):
    """A picture from the front right, a little above (view = where the camera is, in Unity directions)."""
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 640; scene.render.resolution_y = 520
    scene.view_settings.view_transform = 'Standard'
    if scene.world is None:
        world = bpy.data.worlds.new("w"); scene.world = world; world.use_nodes = True
        world.node_tree.nodes["Background"].inputs[0].default_value = (0.75, 0.82, 0.9, 1)
        world.node_tree.nodes["Background"].inputs[1].default_value = 0.9
        sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); scene.collection.objects.link(sun)
        sun.data.energy = 3.2; sun.rotation_euler = (0.85, 0.15, 0.6)
        cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scene.collection.objects.link(cam)
        scene.camera = cam
    cam = scene.camera
    vs = [ob.matrix_world @ v.co for v in ob.data.vertices]
    mn = Vector([min(v[i] for v in vs) for i in range(3)]); mx = Vector([max(v[i] for v in vs) for i in range(3)])
    centre = (mn + mx) / 2; size = (mx - mn).length
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = size * 1.05
    cam.data.clip_start = 0.01; cam.data.clip_end = size * 20 + 10
    v = B(view).normalized()
    cam.location = centre + v * (size * 4 + 1)
    cam.rotation_euler = (-v).to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)

PROPS = {}
def prop(fn):
    PROPS[fn.__name__] = fn
    return fn

# ======================================================================================== things you carry

@prop
def crate():
    """0.6 m cube of planks on corner posts, a brace across two sides."""
    s, t = 0.3, 0.028
    box("Inside", (0, 0, 0), (0.52, 0.52, 0.52), "wood_dark", bevel=0)
    rng = random.Random(4)
    tones = ["wood_light", "wood_light", "wood"]
    for side in range(4):  # four walls of four planks each
        ry = side * 90
        for i in range(4):
            y = -0.2175 + i * 0.145
            box(f"Plank{side}_{i}", euler(0, ry) @ Vector((rng.uniform(-0.004, 0.004), y, s - t / 2)), (0.5, 0.132, t), rng.choice(tones), rot=(0, ry, rng.uniform(-0.6, 0.6)), bevel=0.006)
    for top in (-1, 1):
        for i in range(4):
            x = -0.2175 + i * 0.145
            box(f"Lid{top}_{i}", (x, top * (s - t / 2), rng.uniform(-0.004, 0.004)), (0.132, t, 0.56), rng.choice(tones), rot=(0, rng.uniform(-0.5, 0.5), 0), bevel=0.006)
    for x in (-1, 1):
        for z in (-1, 1):
            box(f"Post{x}{z}", (x * 0.272, 0, z * 0.272), (0.066, 0.6, 0.066), "wood", bevel=0.008)
    for z in (-1, 1):  # the brace
        box(f"Brace{z}", (0, 0, z * (s + 0.004)), (0.062, 0.66, 0.02), "wood", rot=(0, 0, z * 42), bevel=0.005)
    for x in (-1, 1):  # rope handles on the other two sides
        cx = x * (s + 0.012)
        tube(f"Handle{x}", [(cx, 0.06, -0.1), (cx + x * 0.03, 0.02, -0.06), (cx + x * 0.04, -0.01, 0), (cx + x * 0.03, 0.02, 0.06), (cx, 0.06, 0.1)], 0.012, "rope", seg=6)

@prop
def cooler():
    """Body 0.55 x 0.36 x 0.36 with a white lid, a swing handle, a latch and a drain plug."""
    prism("Body", rounded_rect(0.55, 0.36, 0.05), -0.18, 0.16, "blue", bevel=0.012, segments=2, taper=1.0)
    prism("Stripe", rounded_rect(0.556, 0.366, 0.052), -0.03, 0.045, "white", bevel=0.003)
    prism("Lid", rounded_rect(0.575, 0.385, 0.06), 0.16, 0.225, "white", bevel=0.016, segments=2)
    prism("LidTop", rounded_rect(0.44, 0.25, 0.04), 0.225, 0.238, "white", bevel=0.006)
    for x in (-1, 1):  # grips moulded into the ends, and the handle's pivots
        box(f"Grip{x}", (x * 0.277, 0.07, 0), (0.012, 0.045, 0.16), "blue_dark", bevel=0.006)
        lathe(f"Pivot{x}", (x * 0.285, 0.12, 0), [(0.02, -0.008), (0.02, 0.008)], "grey", seg=10, axis='x')
    handle = [(-0.292, 0.12, 0), (-0.296, 0.2, -0.05), (-0.27, 0.285, -0.085), (-0.1, 0.3, -0.09), (0.1, 0.3, -0.09), (0.27, 0.285, -0.085), (0.296, 0.2, -0.05), (0.292, 0.12, 0)]
    tube("Handle", handle, 0.011, "grey", seg=8)
    lathe("HandleGrip", (0, 0.3, -0.09), [(0.017, -0.085), (0.02, -0.07), (0.02, 0.07), (0.017, 0.085)], "dark", seg=10, axis='x')
    box("Latch", (0, 0.15, -0.186), (0.07, 0.07, 0.014), "grey", bevel=0.005)
    box("LatchPad", (0, 0.13, -0.192), (0.05, 0.025, 0.008), "dark", bevel=0.003)
    lathe("Drain", (0.2, -0.13, -0.181), [(0.016, -0.006), (0.016, 0.006), (0.009, 0.012)], "white", seg=10, axis='z')
    for x in (-1, 1):
        for z in (-1, 1):
            box(f"Foot{x}{z}", (x * 0.2, -0.184, z * 0.12), (0.08, 0.012, 0.05), "blue_dark", bevel=0.004)

@prop
def beach_ball():
    """0.55 m ball in six coloured panels with a white cap top and bottom."""
    seg, rings, r = 24, 12, 0.275
    prof = [(math.sin(math.pi * i / rings) * r, -math.cos(math.pi * i / rings) * r) for i in range(rings + 1)]
    ob = lathe("Ball", (0, 0, 0), prof, "white", seg=seg, turn=0.0)
    gores = ["red", "white", "yellow", "white", "blue", "white"]
    def pick(c, n):
        if abs(c.y) > r * 0.93: return "white"
        ang = (math.degrees(math.atan2(c.z, c.x)) + 360) % 360
        return gores[int(ang // 60) % 6]
    paint(ob, pick)
    lathe("Valve", (0, r - 0.002, 0), [(0.012, 0), (0.012, 0.006), (0.006, 0.01)], "white", seg=8)

@prop
def life_ring():
    """Ring 0.28 m across its middle, tube 0.075: red with four white bands and a grab rope looped round the outside."""
    R, r = 0.28, 0.075
    seg, around = 32, 14
    verts, faces = [], []
    for i in range(seg):
        t = 2 * math.pi * i / seg
        for k in range(around):
            p = 2 * math.pi * k / around
            rad = R + math.cos(p) * r
            verts.append((math.cos(t) * rad, math.sin(p) * r, math.sin(t) * rad))
    for i in range(seg):
        for k in range(around):
            a0, b0 = i * around, ((i + 1) % seg) * around
            faces.append((a0 + k, a0 + (k + 1) % around, b0 + (k + 1) % around, b0 + k))
    ob = add("Ring", verts, faces, "red")
    def pick(c, n):
        ang = (math.degrees(math.atan2(c.z, c.x)) + 360) % 360
        return "white" if abs(((ang - 45) % 90 + 45) % 90 - 45) < 13 else None
    paint(ob, pick)
    # The rope: held under each white band, hanging loose in between.
    out = R + r + 0.012
    for q in range(4):
        a0 = 45 + q * 90
        path = []
        for i in range(13):
            u = i / 12
            ang = math.radians(a0 + 90 * u)
            sag = math.sin(math.pi * u)
            rad = out + 0.035 * sag
            path.append((math.cos(ang) * rad, -0.02 * sag, math.sin(ang) * rad))
        tube(f"Rope{q}", path, 0.009, "rope", seg=6, caps=False)
        ang = math.radians(a0)
        c = Vector((math.cos(ang) * R, 0, math.sin(ang) * R))
        tube(f"Tie{q}", [c + (Vector((math.cos(ang), 0, math.sin(ang))) * math.cos(p) + Vector((0, 1, 0)) * math.sin(p)) * (r + 0.006)
                         for p in [2 * math.pi * k / 12 for k in range(12)]], 0.007, "rope", seg=6, closed=True)

@prop
def coconut():
    """An opened snack coconut: fibrous brown shell, thick white rim and a shallow flesh bowl."""
    profile = [(0, -.112), (.037, -.103), (.068, -.079), (.090, -.041), (.101, .004),
               (.098, .041), (.087, .083), (.081, .088), (.074, .081), (.072, .059), (0, .030)]
    lathe("Shell", (0, 0, 0), profile, "husk", seg=48)
    flesh = lathe("FreshFlesh", (0, 0, 0), [(.082, .089), (.074, .087), (.070, .063),
                                           (.057, .061), (.034, .055), (0, .050)], "coconut_flesh", seg=48, caps=False)
    # An open concave surface has no volume to guide Blender's normal repair.
    # All visible flesh faces must face into the bowl, toward the sky.
    bm = bmesh.new(); bm.from_mesh(flesh.data); bm.normal_update()
    bmesh.ops.reverse_faces(bm, faces=[f for f in bm.faces if f.normal.z < 0])
    bm.to_mesh(flesh.data); bm.free()
    for i in range(28):
        angle = 2 * math.pi * i / 28
        pts = []
        for radius, height in profile[1:7]:
            a = angle + .028 * math.sin(height * 36 + i)
            pts.append(((radius + .0008) * math.cos(a), height, (radius + .0008) * math.sin(a)))
        tube(f"Fibre{i}", pts, .00065 if i % 3 else .0010,
             "husk_light" if i % 3 else "husk_dark", seg=4)

@prop
def beer_bottle():
    """A chunky brown longneck 0.31 m tall (cartoon-sized for the goofy lifeguards' big hands; centred, the lip at
    y 0.156): a cream label with a red band round its belly, gold foil round the neck and a gold crown cap."""
    k = 1.3
    def L(name, prof, mat, seg):
        lathe(name, (0, 0, 0), [(r * k, y * k) for r, y in prof], mat, seg=seg, caps=False)
    L("Glass", [(0.0, -0.115), (0.026, -0.115), (0.031, -0.109), (0.031, 0.02), (0.029, 0.04), (0.021, 0.063),
                (0.0125, 0.083), (0.0112, 0.106), (0.0125, 0.108), (0.0125, 0.12),
                (0.0095, 0.12), (0.0095, 0.103)], "beer_glass", 48)
    L("Label", [(0.0316, -0.078), (0.0316, -0.002)], "beer_label", 64)
    L("Band", [(0.0321, -0.054), (0.0321, -0.031)], "red", 32)
    L("Foil", [(0.0128, 0.086), (0.0122, 0.1)], "beer_foil", 32)
    L("OpenLip", [(0.0127, 0.112), (0.0134, 0.116), (0.0130, 0.12), (0.0095, 0.12)], "beer_foil", 48)
    L("BottomRim", [(0.029, -0.113), (0.0314, -0.108), (0.0314, -0.102)], "beer_glass", 48)
    L("LabelTopTrim", [(0.0319, -.006), (0.0319, -.002)], "blue_dark", 64)
    L("LabelBottomTrim", [(0.0319, -.079), (0.0319, -.075)], "blue_dark", 64)
    rng = random.Random(621)
    for i in range(24):
        angle = rng.random() * 2 * math.pi
        y = rng.uniform(-.14, .021)
        if -.106 < y < -.002: continue  # keep the lettering readable
        r = .0312 * k
        blob(f"ColdDrop{i}", (math.cos(angle) * r, y, math.sin(angle) * r),
             (.0015, .0024 + rng.random() * .0018, .0015), "condensation", subdiv=1, rough=0)
    # The label's design, raised a hair off it and wrapped round the glass: the brand in white on the red band, a
    # setting sun over a wave above it, the kind of beer underneath.
    # (Front and back, so the drinker and whoever they're toasting both see it.)
    R = 0.0321 * k
    for side, turn in (("F", 0.0), ("B", math.pi)):
        wrap_on_cylinder(text3d(f"Brand{side}", "DROWN", (0, -0.0425 * k, 0), 0.0155 * k, 0.0012, "white"), R + 0.0004, turn)
        wrap_on_cylinder(text3d(f"Kind{side}", "LAGER", (0, -0.069 * k, 0), 0.0072 * k, 0.0010, "blue_dark"), 0.0316 * k + 0.0004, turn)
        sun = lathe(f"Sun{side}", (0, -0.017 * k, 0), [(0.0, 0.0), (0.0085 * k, 0.0), (0.0085 * k, 0.0011), (0.0, 0.0011)], "yellow", seg=24, axis='z')
        wrap_on_cylinder(sun, 0.0316 * k + 0.0003, turn)
        wave = [(x * 0.001 * k, (-0.0205 + 0.0016 * math.sin(x * 0.9)) * k, 0.0) for x in range(-12, 13)]
        wrap_on_cylinder(tube(f"Wave{side}", wave, 0.0013 * k, "blue", seg=6), 0.0316 * k + 0.0011, turn)

@prop
def fries():
    """A red paper carton of chips, wider at the top, a golden bunch of fat fries sticking out at angles
    (0.22 m tall, cartoon-sized like the beer; centred)."""
    prism("Carton", rounded_rect(0.09, 0.05, 0.008), -0.105, 0.035, "red", taper=1.3, bevel=0.003)
    prism("Inside", rounded_rect(0.105, 0.058, 0.006), 0.0, 0.034, "fries_dark")
    for side, turn in (("F", "+z"), ("B", "-z")):
        z = 0.034 if side == "F" else -0.034
        text3d(f"Brand{side}", "FRIES", (0, -0.045, z), 0.018, 0.0015, "yellow", face=turn)
    rng = random.Random(77)
    for i in range(26):
        x, z = rng.uniform(-0.036, 0.036), rng.uniform(-0.014, 0.014)
        h = rng.uniform(0.09, 0.13)
        # (Leaning outward a little at most, so none pokes through the carton's walls.)
        box(f"Fry{i}", (x, h / 2 - 0.004, z), (0.012, h, 0.012), rng.choice(["fries", "fries", "fries_dark"]),
            rot=(rng.uniform(-6, 6) + z * 200, rng.uniform(0, 90), rng.uniform(-6, 6) - x * 120), bevel=0.003)

@prop
def cola_can():
    """A fat red soda can (0.2 m tall, centred): silver top and bottom, a white wave and the made-up "FIZZ" brand."""
    lathe("Can", (0, 0, 0), [(0.0, -0.1), (0.032, -0.1), (0.04, -0.092), (0.04, 0.085), (0.034, 0.097), (0.0, 0.097)], "red", seg=40)
    lathe("Bottom", (0, 0, 0), [(0.033, -0.101), (0.0401, -0.093), (0.0401, -0.084)], "can_metal", seg=40, caps=False)
    lathe("Lid", (0, 0, 0), [(0.0402, 0.078), (0.0402, 0.086), (0.0345, 0.098), (0.031, 0.1), (0.0, 0.098)], "can_metal", seg=40)
    box("Tab", (0.006, 0.101, 0), (0.024, 0.003, 0.012), "can_metal", bevel=0.001)
    R = 0.0402
    for side, turn in (("F", 0.0), ("B", math.pi)):
        wrap_on_cylinder(text3d(f"Brand{side}", "FIZZ", (0, 0.012, 0), 0.025, 0.0012, "white"), R + 0.0003, turn)
        wave = [(x * 0.004, -0.03 + 0.006 * math.sin(x * 0.55), 0.0) for x in range(-9, 10)]
        wrap_on_cylinder(tube(f"Wave{side}", wave, 0.003, "white", seg=6), R + 0.0006, turn)

@prop
def cocktail():
    """A tall hurricane glass of orange-to-yellow tropical drink with a lime wheel on the rim, a striped straw and a
    pink paper umbrella (0.27 m tall to the umbrella, centred)."""
    glass = [(0.0, -0.11), (0.03, -0.11), (0.03, -0.104), (0.008, -0.098), (0.007, -0.07), (0.026, -0.055),
             (0.036, -0.02), (0.03, 0.02), (0.034, 0.06), (0.042, 0.09)]
    lathe("Glass", (0, 0, 0), glass, "cocktail_glass", seg=40, caps=False)
    lathe("DrinkLow", (0, 0, 0), [(0.0, -0.054), (0.0245, -0.054), (0.0335, -0.02), (0.028, 0.0), (0.0, 0.0)], "cocktail", seg=40)
    lathe("DrinkHigh", (0, 0, 0), [(0.0, 0.0), (0.028, 0.0), (0.031, 0.02), (0.0345, 0.07), (0.0, 0.07)], "cocktail_top", seg=40)
    lathe("Lime", (0.04, 0.08, 0), [(0.0, -0.004), (0.022, -0.004), (0.022, 0.004), (0.0, 0.004)], "lime", seg=24, axis='z')
    tube("Straw", [(-0.012, -0.02, 0.006), (-0.02, 0.13, 0.012), (-0.03, 0.15, 0.012)], 0.004, "white", seg=8)
    tube("StrawStripe", [(-0.0121, 0.0, 0.0102), (-0.017, 0.09, 0.0142)], 0.0042, "red", seg=8)
    tube("UmbrellaStick", [(0.012, 0.0, -0.01), (0.03, 0.15, -0.02)], 0.0016, "wood_light", seg=6)
    lathe("Umbrella", (0.031, 0.152, -0.02), [(0.0, 0.016), (0.035, 0.0), (0.036, -0.002), (0.0, 0.0)], "pink", seg=8)

@prop
def defibrillator():
    """Yellow case 0.34 x 0.12 x 0.26 with a carry handle, a screen, two buttons and the paddles on their coiled leads."""
    prism("Case", rounded_rect(0.34, 0.26, 0.035), -0.06, 0.05, "yellow", bevel=0.012, segments=2)
    prism("Base", rounded_rect(0.345, 0.265, 0.037), -0.06, -0.035, "dark", bevel=0.004)
    prism("Face", rounded_rect(0.3, 0.22, 0.025), 0.05, 0.058, "yellow", bevel=0.004)
    box("Screen", (-0.06, 0.06, -0.035), (0.13, 0.006, 0.085), "black", bevel=0.003)
    trace = [(-0.115, 0.0645, -0.035), (-0.085, 0.0645, -0.035), (-0.075, 0.0645, -0.01), (-0.062, 0.0645, -0.06), (-0.05, 0.0645, -0.035), (-0.005, 0.0645, -0.035)]
    tube("Trace", trace, 0.0022, "green", seg=4)
    lathe("Shock", (0.09, 0.058, -0.05), [(0.024, 0), (0.024, 0.008), (0.018, 0.012), (0.0, 0.012)], "red", seg=14)
    lathe("Power", (0.045, 0.058, -0.065), [(0.012, 0), (0.012, 0.006), (0.0, 0.008)], "green", seg=10)
    # A white heart with a bolt of lightning through it, where the cross used to be.
    heart = []
    for i in range(24):
        t = 2 * math.pi * i / 24
        heart.append((0.002 * 16 * math.sin(t) ** 3, 0.002 * (13 * math.cos(t) - 5 * math.cos(2 * t) - 2 * math.cos(3 * t) - math.cos(4 * t))))
    prism("Heart", [(x, z) for x, z in heart], 0.058, 0.061, "white", centre=(0, 0, 0.078))
    bolt = [(0.005, 0.024), (-0.01, -0.003), (-0.001, -0.003), (-0.007, -0.024), (0.011, 0.005), (0.002, 0.005)]
    prism("Bolt", bolt, 0.061, 0.063, "red", centre=(0, 0, 0.078))
    handle = [(-0.09, 0.0, -0.13), (-0.09, 0.02, -0.165), (-0.06, 0.03, -0.18), (0.06, 0.03, -0.18), (0.09, 0.02, -0.165), (0.09, 0.0, -0.13)]
    tube("Handle", handle, 0.011, "dark", seg=8)
    for x in (-1, 1):  # the paddles, lying on top at the back
        c = Vector((x * 0.12, 0.058, 0.09))
        lathe(f"Paddle{x}", c, [(0.036, 0), (0.038, 0.006), (0.038, 0.014), (0.03, 0.02), (0.0, 0.02)], "dark", seg=14)
        lathe(f"Plate{x}", c, [(0.031, -0.002), (0.031, 0.0)], "steel", seg=14)
        tube(f"PaddleGrip{x}", [c + Vector((-0.024, 0.02, 0)), c + Vector((-0.024, 0.045, 0)), c + Vector((0.024, 0.045, 0)), c + Vector((0.024, 0.02, 0))], 0.008, "dark", seg=6)
        coil = [c + Vector((-x * 0.03 - x * 0.07 * (i / 28), 0.012 + 0.008 * math.sin(i * 1.6), -0.045 + 0.008 * math.cos(i * 1.6) - 0.03 * (i / 28))) for i in range(29)]
        tube(f"Lead{x}", coil, 0.0035, "black", seg=5)

# ======================================================================================== lost things

@prop
def wallet():
    """Closed leather bifold, 0.12 x 0.03 x 0.09: stitched edge, a strap with a brass snap, a banknote showing."""
    for y, name in ((-0.0075, "Back"), (0.0075, "Front")):
        prism(name, rounded_rect(0.12, 0.09, 0.012), y - 0.006, y + 0.006, "leather", bevel=0.003, segments=2)
    lathe("Spine", (0, 0, 0.043), [(0.0135, -0.058), (0.0135, 0.058)], "leather_dark", seg=10, axis='x')
    stitch = rounded_rect(0.106, 0.076, 0.008)
    tube("Stitch", [(x, 0.0138, z) for x, z in stitch], 0.0012, "stitch", seg=4, closed=True)
    box("Cash", (0.01, 0.0, -0.044), (0.085, 0.004, 0.012), "cash", bevel=0.001)
    box("Cash2", (-0.004, -0.004, -0.0445), (0.085, 0.003, 0.01), "cash", bevel=0.001)
    box("Strap", (0, 0.0142, -0.02), (0.026, 0.003, 0.055), "leather_dark", bevel=0.002)
    box("StrapEdge", (0, 0.004, -0.0465), (0.026, 0.024, 0.003), "leather_dark", bevel=0.001)
    lathe("Snap", (0, 0.0155, -0.03), [(0.007, 0), (0.007, 0.002), (0.004, 0.004), (0.0, 0.004)], "brass", seg=10)

@prop
def phone():
    """Slab 0.08 x 0.012 x 0.16 lying screen up: lit screen with a few app squares, camera island underneath, side buttons."""
    prism("Body", rounded_rect(0.08, 0.16, 0.012), -0.006, 0.006, "black", bevel=0.003, segments=2)
    prism("Screen", rounded_rect(0.072, 0.15, 0.009), 0.006, 0.0068, "screen")
    box("Notch", (0, 0.0069, 0.068), (0.022, 0.0006, 0.006), "black", bevel=0.0)
    rng = random.Random(3)
    icons = ["screen_light", "white", "yellow", "pink", "green", "orange"]
    for row in range(4):
        for col in range(3):
            prism(f"App{row}{col}", rounded_rect(0.013, 0.013, 0.003, n=2), 0.0068, 0.0073, rng.choice(icons), centre=(-0.021 + col * 0.021, 0, 0.042 - row * 0.024))
    box("Dock", (0, 0.007, -0.058), (0.06, 0.0006, 0.016), "screen_light", bevel=0.0)
    prism("CameraIsland", rounded_rect(0.03, 0.032, 0.007), -0.0085, -0.006, "dark", bevel=0.001, centre=(0.02, 0, 0.058))
    for dz in (-0.007, 0.007):
        lathe(f"Lens{dz}", (0.02, -0.0085, 0.058 + dz), [(0.0, -0.0012), (0.0052, -0.0012), (0.0052, 0.0)], "lens", seg=10)
    box("Power", (0.0405, 0, 0.02), (0.002, 0.004, 0.02), "dark", bevel=0.0008)
    box("Volume", (-0.0405, 0, 0.03), (0.002, 0.004, 0.03), "dark", bevel=0.0008)

@prop
def sunglasses():
    """Pink frames about 0.15 wide with dark lenses, arms folded out behind (+z)."""
    def lens_outline(side):
        pts = []
        for i in range(20):
            t = 2 * math.pi * i / 20
            x = math.cos(t) * 0.031; y = math.sin(t) * 0.023
            if y > 0: y *= 0.8  # flatter along the brow
            x += 0.004 * (y / 0.023) * side * -1 if y < 0 else 0
            pts.append((side * 0.039 + x, y - 0.012))
        return pts
    for side in (-1, 1):
        o = lens_outline(side)
        prism(f"Lens{side}", o, -0.002, 0.002, "lens", plane='xy')
        tube(f"Rim{side}", [(x, y, 0) for x, y in o], 0.0042, "pink", seg=6, closed=True)
        hinge = Vector((side * 0.073, -0.004, 0.002))
        arm = [hinge, hinge + Vector((side * 0.004, 0.001, 0.03)), hinge + Vector((side * 0.003, 0.0, 0.1)), hinge + Vector((side * 0.001, -0.012, 0.132)), hinge + Vector((0, -0.024, 0.14))]
        tube(f"Arm{side}", arm, lambda u: 0.004 - 0.0012 * u, "pink", seg=6)
    tube("Bridge", [(-0.011, -0.004, 0), (-0.005, 0.002, -0.001), (0.005, 0.002, -0.001), (0.011, -0.004, 0)], 0.0038, "pink", seg=6)
    tube("Brow", [(-0.068, 0.006, 0), (-0.03, 0.009, -0.001), (0.03, 0.009, -0.001), (0.068, 0.006, 0)], 0.0036, "pink", seg=6)

@prop
def watch():
    """Gold watch lying face up: case 0.05 across, dial with hands, a crown, black strap 0.16 long with a buckle."""
    lathe("Case", (0, 0, 0), [(0.021, -0.005), (0.025, -0.003), (0.025, 0.003), (0.022, 0.006)], "gold", seg=20)
    lathe("Bezel", (0, 0, 0), [(0.022, 0.006), (0.0225, 0.0078), (0.0195, 0.0078), (0.019, 0.0062)], "brass", seg=20)
    lathe("Dial", (0, 0.0062, 0), [(0.0, 0), (0.019, 0)], "dial", seg=20)
    for i in range(12):
        t = math.radians(i * 30)
        big = i % 3 == 0
        box(f"Mark{i}", (math.sin(t) * 0.0155, 0.0066, math.cos(t) * 0.0155), (0.0012 if not big else 0.002, 0.0006, 0.003 if not big else 0.0045), "white", rot=(0, i * 30, 0), bevel=0)
    box("Hour", euler(0, 50) @ Vector((0, 0.0069, 0.0045)), (0.0018, 0.0006, 0.01), "gold", rot=(0, 50, 0), bevel=0)
    box("Minute", euler(0, -65) @ Vector((0, 0.0073, 0.0065)), (0.0014, 0.0006, 0.014), "gold", rot=(0, -65, 0), bevel=0)
    lathe("Pin", (0, 0.007, 0), [(0.0014, 0), (0.0014, 0.0012)], "gold", seg=8)
    lathe("Crown", (0.0275, 0, 0), [(0.003, -0.0025), (0.004, -0.0015), (0.004, 0.0025)], "gold", seg=10, axis='x')
    for side in (-1, 1):  # two strap halves, dropping away from the case like a watch laid on a table
        steps = 8; pts_top = []
        for i in range(steps + 1):
            u = i / steps
            z = side * (0.022 + 0.058 * u); y = -0.001 - 0.007 * u * u
            pts_top.append((y, z))
        width0, width1 = 0.02, 0.016
        verts, faces = [], []
        for i, (y, z) in enumerate(pts_top):
            w = width0 + (width1 - width0) * (i / steps)
            verts += [(-w / 2, y, z), (w / 2, y, z), (w / 2, y - 0.003, z), (-w / 2, y - 0.003, z)]
        for i in range(steps):
            a0, b0 = i * 4, (i + 1) * 4
            for k in range(4): faces.append((a0 + k, a0 + (k + 1) % 4, b0 + (k + 1) % 4, b0 + k))
        faces += [(0, 1, 2, 3), tuple(range(steps * 4, steps * 4 + 4))]
        add(f"Strap{side}", verts, faces, "black", bevel=0.0008)
        lathe(f"Lug{side}", (0, -0.001, side * 0.0235), [(0.0028, -0.012), (0.0028, 0.012)], "gold", seg=8, axis='x')
    tube("Buckle", [(-0.011, -0.0085, 0.08), (0.011, -0.0085, 0.08), (0.011, -0.0085, 0.09), (-0.011, -0.0085, 0.09)], 0.0013, "steel", seg=5, closed=True)
    for i in range(4):
        lathe(f"Hole{i}", (0, -0.0035 - 0.0012 * i, -0.045 - i * 0.008), [(0.0, 0.0), (0.0014, 0.0)], "dark", seg=6)

@prop
def baggie():
    """A small zip bag of white powder, 0.09 x 0.025 x 0.07: clear flat edge, a soft bulge, a red zip strip."""
    prism("Seam", rounded_rect(0.092, 0.072, 0.006), -0.0012, 0.0012, "bag", bevel=0.0005)
    blob("Powder", (0, 0.0005, -0.006), (0.036, 0.0115, 0.024), "powder", subdiv=2, rough=0.12, freq=2.2, seed=5.0)
    blob("Slack", (0, 0.0, 0.004), (0.041, 0.006, 0.03), "bag", subdiv=2, rough=0.05, freq=2.0, seed=1.0)
    box("Zip", (0, 0.0, 0.031), (0.09, 0.0045, 0.005), "red", bevel=0.0015)
    box("ZipTab", (0.037, 0.0, 0.031), (0.01, 0.008, 0.008), "red", bevel=0.002)

@prop
def jet_ski_keys():
    """An orange foam float 0.05 x 0.03 x 0.1 with a white band, a split ring and the key."""
    prism("Float", rounded_rect(0.05, 0.1, 0.022), -0.015, 0.015, "orange", bevel=0.007, segments=2)
    prism("Band", rounded_rect(0.052, 0.03, 0.004), -0.0155, 0.0155, "white", bevel=0.002, centre=(0, 0, -0.01))
    lathe("Eyelet", (0, 0, 0.047), [(0.005, -0.004), (0.005, 0.004)], "steel", seg=8, axis='y')
    tube("Ring", circle((0, 0, 0.062), 0.013, n=16), 0.0014, "steel", seg=5, closed=True)
    bow = rounded_rect(0.02, 0.018, 0.005)
    prism("Bow", bow, -0.0022, 0.0022, "black", bevel=0.001, centre=(0, 0, 0.082))
    blade = [(-0.004, 0.0), (0.004, 0.0), (0.004, 0.008), (0.0025, 0.011), (0.004, 0.014), (0.002, 0.018), (0.004, 0.022), (0.0025, 0.027), (0.0, 0.031), (-0.004, 0.027)]
    prism("Blade", blade, -0.001, 0.001, "steel", centre=(0, 0, 0.091))

# ======================================================================================== the beach

@prop
def rock():
    """A weathered boulder one metre across (the scene stretches it): lighter on top, dark and mossy low down."""
    ob = blob("Rock", (0, 0, 0), (0.5, 0.5, 0.5), "rock", subdiv=3, rough=0.2, freq=1.3, seed=3.0)
    # Chip a few flat faces into it, like a stone that has split.
    bm = bmesh.new(); bm.from_mesh(ob.data)
    rng = random.Random(12)
    for _ in range(7):
        n = Vector((rng.uniform(-1, 1), rng.uniform(-0.3, 1), rng.uniform(-1, 1))).normalized()
        d = rng.uniform(0.36, 0.46)
        for v in bm.verts:
            over = v.co.dot(B(n)) - d
            if over > 0: v.co -= B(n) * over
    bm.to_mesh(ob.data); bm.free()
    def pick(c, n):
        if n.y > 0.75: return "rock_light"
        if c.y < -0.12 and n.y < 0.2: return "rock_dark"
        return None
    paint(ob, pick)

@prop
def buoy():
    """A swim-line float 0.5 m across: orange with a white band, a ring on top for the rope."""
    r = 0.25
    prof = [(math.sin(math.pi * i / 12) * r, -math.cos(math.pi * i / 12) * r * 0.9) for i in range(13)]
    ob = lathe("Float", (0, 0, 0), prof, "orange", seg=18)
    paint(ob, lambda c, n: "white" if abs(c.y) < 0.055 else None)
    lathe("Collar", (0, 0.21, 0), [(0.06, 0), (0.05, 0.03), (0.03, 0.04)], "dark", seg=10)
    tube("Eye", circle((0, 0.27, 0), 0.035, n=12, axis='z'), 0.008, "steel", seg=6, closed=True)
    tube("Tail", [(0, -0.2, 0), (0.01, -0.34, 0.01), (0.0, -0.5, 0.0)], 0.012, "rope", seg=6)

@prop
def dock():
    """The station's dock: 2.4 m wide, running from z 6 (land) out to z -19, planks across on two beams, posts in the sea."""
    rng = random.Random(21)
    tones = ["wood", "wood", "wood_light", "wood_grey"]
    z = 5.9
    i = 0
    while z > -18.95:
        w = rng.uniform(0.2, 0.26)
        box(f"Plank{i}", (rng.uniform(-0.03, 0.03), 0.275, z - w / 2), (2.4 + rng.uniform(-0.06, 0.06), 0.05, w - 0.018), rng.choice(tones),
            rot=(0, rng.uniform(-0.5, 0.5), 0), bevel=0.008)
        z -= w; i += 1
    for x in (-0.85, 0.85):
        box(f"Beam{x}", (x, 0.16, -6.5), (0.14, 0.2, 24.9), "wood_dark", bevel=0.01)
    z = 2.0
    while z >= -18.0:
        for x in (-1.1, 1.1):
            lathe(f"Post{x}{z}", (x, 0, z), [(0.1, -4.6), (0.11, -0.4), (0.11, 0.52), (0.09, 0.56), (0.0, 0.56)], "wood_dark", seg=8)
            tube(f"Wrap{x}{z}", circle((x, 0.4, z), 0.118, n=10), 0.014, "rope", seg=5, closed=True)
            tube(f"Wrap2{x}{z}", circle((x, 0.43, z), 0.118, n=10), 0.014, "rope", seg=5, closed=True)
        box(f"Tie{z}", (0, 0.05, z), (2.2, 0.1, 0.1), "wood_dark", bevel=0.01)
        z -= 4.0

@prop
def towel():
    """A beach towel 0.95 x 1.95 lying on sand: a few soft wrinkles, a turned corner, three bands (the scene tints 'cloth')."""
    nx, nz = 12, 24
    w, d = 0.95, 1.95
    verts, faces = [], []
    for iz in range(nz + 1):
        for ix in range(nx + 1):
            x = -w / 2 + w * ix / nx; z = -d / 2 + d * iz / nz
            y = 0.012 + 0.008 * noise.noise(Vector((x * 2.2, z * 2.2, 0.3))) + 0.004 * noise.noise(Vector((x * 6, z * 6, 2.0)))
            # one corner flipped up by the wind
            cx, cz = x - w / 2, z - d / 2
            lift = max(0.0, 0.22 - math.hypot(cx, cz))
            y += lift * lift * 2.2
            verts.append((x, max(0.006, y), z))
    for iz in range(nz):
        for ix in range(nx):
            a0 = iz * (nx + 1) + ix
            faces.append((a0, a0 + 1, a0 + nx + 2, a0 + nx + 1))
    ob = add("Towel", verts, faces, "cloth")
    def pick(c, n):
        if abs(c.z + 0.7) < 0.09 or abs(c.z - 0.7) < 0.09: return "white"
        if abs(c.z + 0.52) < 0.03 or abs(c.z - 0.52) < 0.03: return "white"
        return None
    paint(ob, pick)
    mod = ob.modifiers.new("solid", 'SOLIDIFY'); mod.thickness = 0.008; mod.offset = -1
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.modifier_apply(modifier=mod.name)

# ======================================================================================== the station

@prop
def bell_post():
    """The alarm bell's post: a squared timber 2.3 m tall with an iron arm reaching 0.42 m toward -x."""
    box("Post", (0, 1.15, 0), (0.12, 2.3, 0.12), "wood", bevel=0.012)
    box("Cap", (0, 2.33, 0), (0.17, 0.05, 0.17), "wood_dark", bevel=0.012)
    box("Foot", (0, 0.06, 0), (0.2, 0.12, 0.2), "wood_dark", bevel=0.012)
    arm = [(0.0, 2.05, 0.0), (-0.08, 2.2, 0.0), (-0.2, 2.26, 0.0), (-0.36, 2.25, 0.0), (-0.42, 2.22, 0.0)]
    tube("Arm", arm, 0.016, "dark", seg=8)
    tube("Curl", circle((-0.42, 2.19, 0), 0.03, n=10, axis='z', start=90, sweep=270), 0.012, "dark", seg=6)
    box("Plate", (-0.005, 2.02, 0), (0.135, 0.14, 0.05), "dark", bevel=0.006)

@prop
def bell():
    """The brass bell, hanging from y 0 (its pivot) down to the lip at y -0.32, 0.28 m across; clapper and pull rope."""
    prof = [(0.0, -0.035), (0.03, -0.035), (0.05, -0.05), (0.066, -0.085), (0.074, -0.15), (0.086, -0.215), (0.112, -0.275), (0.14, -0.305), (0.142, -0.32),
            (0.128, -0.32), (0.1, -0.27), (0.07, -0.2), (0.058, -0.1), (0.0, -0.07)]
    lathe("Bell", (0, 0, 0), prof, "brass", seg=20)
    lathe("Band", (0, 0, 0), [(0.089, -0.222), (0.096, -0.232), (0.094, -0.245)], "brass_dark", seg=20)
    tube("Hanger", circle((0, -0.018, 0), 0.02, n=10, axis='z'), 0.007, "dark", seg=6, closed=True)
    tube("Rod", [(0, -0.07, 0), (0, -0.3, 0)], 0.006, "dark", seg=6)
    lathe("Clapper", (0, -0.31, 0), arc_profile(0.032, -0.03, 0.03, 6), "dark", seg=10)
    rope = [(0, -0.34, 0), (0.004, -0.5, 0.004), (0.0, -0.66, 0.0), (0.003, -0.8, 0.002)]
    tube("Rope", rope, 0.008, "rope", seg=6)
    lathe("Knot", (0.003, -0.82, 0.002), arc_profile(0.02, -0.024, 0.024, 5), "rope", seg=8)

@prop
def sign_frame():
    """The welcome sign: two posts and a framed board 1.9 x 0.9 centred 1.75 m up (the lettering is a painted picture)."""
    for x in (-0.88, 0.88):
        box(f"Post{x}", (x, 1.15, 0.03), (0.1, 2.3, 0.1), "wood_dark", bevel=0.012)
        box(f"Cap{x}", (x, 2.32, 0.03), (0.15, 0.05, 0.15), "wood", bevel=0.012)
    box("Board", (0, 1.75, 0), (1.9, 0.9, 0.05), "cream", bevel=0.006)
    for y in (1.75 - 0.47, 1.75 + 0.47):
        box(f"Rail{y}", (0, y, -0.005), (1.98, 0.07, 0.075), "wood", bevel=0.01)
    for x in (-0.965, 0.965):
        box(f"Stile{x}", (x, 1.75, -0.005), (0.07, 1.0, 0.075), "wood", bevel=0.01)

@prop
def drill_board():
    """The rescue-drill board: one post and a framed panel 1.5 x 0.8 centred 1.6 m up (lettering is a painted picture)."""
    box("Post", (0, 1.0, 0.045), (0.12, 2.0, 0.12), "wood_dark", bevel=0.012)
    box("Foot", (0, 0.06, 0.045), (0.2, 0.12, 0.2), "wood_dark", bevel=0.012)
    box("Panel", (0, 1.6, 0), (1.5, 0.8, 0.05), "red", bevel=0.006)
    for y in (1.6 - 0.42, 1.6 + 0.42):
        box(f"Rail{y}", (0, y, -0.005), (1.58, 0.07, 0.075), "wood", bevel=0.01)
    for x in (-0.765, 0.765):
        box(f"Stile{x}", (x, 1.6, -0.005), (0.07, 0.91, 0.075), "wood", bevel=0.01)

@prop
def roof_sign():
    """A plank sign for the hut's roof, 2.4 x 0.5, on two short struts (the lettering is a painted picture on its -z face)."""
    box("Board", (0, 0.42, 0), (2.4, 0.5, 0.05), "wood_light", rot=(0, 0, -1.5), bevel=0.01)
    for x in (-0.8, 0.8):
        box(f"Strut{x}", (x, 0.12, 0.04), (0.07, 0.36, 0.07), "wood_dark", bevel=0.01)

@prop
def stool():
    """A bar stool 0.73 m tall: round red seat, four splayed legs, a footrest ring 0.28 m up."""
    lathe("Seat", (0, 0.705, 0), [(0.0, -0.01), (0.15, -0.01), (0.19, 0.0), (0.19, 0.03), (0.17, 0.045), (0.0, 0.05)], "seat", seg=16)
    lathe("Under", (0, 0.68, 0), [(0.13, 0), (0.15, 0.02)], "wood_dark", seg=12)
    for i in range(4):
        t = math.radians(45 + i * 90)
        top = Vector((math.cos(t) * 0.11, 0.69, math.sin(t) * 0.11)); foot = Vector((math.cos(t) * 0.2, 0.0, math.sin(t) * 0.2))
        tube(f"Leg{i}", [top, foot], 0.02, "wood", seg=6)
    tube("Footrest", circle((0, 0.28, 0), 0.165, n=16), 0.014, "steel", seg=6, closed=True)

@prop
def shelf():
    """A plank shelf 1.4 m wide on two board legs, top 0.98 m up, with a lower shelf and a back rail."""
    box("Top", (0, 0.95, 0), (1.4, 0.06, 0.42), "wood", bevel=0.01)
    box("Lower", (0, 0.42, 0), (1.22, 0.04, 0.34), "wood_light", bevel=0.008)
    for x in (-0.62, 0.62):
        box(f"Leg{x}", (x, 0.47, 0), (0.06, 0.95, 0.36), "wood_dark", bevel=0.01)
    box("Rail", (0, 1.03, -0.19), (1.36, 0.1, 0.03), "wood_dark", bevel=0.008)

@prop
def radio():
    """An old portable radio 0.36 x 0.2 x 0.16: speaker grille, tuning window, two knobs, a handle and an aerial."""
    prism("Case", rounded_rect(0.36, 0.2, 0.03), -0.08, 0.08, "teal", plane='xy', bevel=0.008, segments=2)
    prism("Front", rounded_rect(0.33, 0.17, 0.02), 0.08, 0.086, "cream", plane='xy', bevel=0.003)
    lathe("Speaker", (-0.075, 0, 0.086), [(0.0, 0.002), (0.062, 0.002), (0.066, 0.0)], "dark", seg=18, axis='z')
    for i in range(5):
        box(f"Slat{i}", (-0.075, -0.04 + i * 0.02, 0.0895), (0.1 - abs(i - 2) * 0.02, 0.005, 0.002), "cream", bevel=0)
    box("Window", (0.085, 0.04, 0.087), (0.12, 0.04, 0.003), "white", bevel=0.001)
    box("Needle", (0.1, 0.04, 0.089), (0.004, 0.036, 0.002), "red", bevel=0)
    for x in (0.045, 0.125):
        lathe(f"Knob{x}", (x, -0.04, 0.086), [(0.02, 0), (0.02, 0.012), (0.015, 0.016), (0.0, 0.016)], "dark", seg=12, axis='z')
    tube("Handle", [(-0.13, 0.1, 0), (-0.12, 0.135, 0), (0.12, 0.135, 0), (0.13, 0.1, 0)], 0.008, "dark", seg=6)
    tube("Aerial", [(0.15, 0.1, -0.05), (0.2, 0.24, -0.06), (0.26, 0.4, -0.07)], lambda u: 0.004 - 0.002 * u, "steel", seg=5)
    lathe("AerialTip", (0.26, 0.4, -0.07), arc_profile(0.006, -0.006, 0.006, 4), "steel", seg=6)

@prop
def first_aid_kit():
    """A green first-aid box 0.3 x 0.16 x 0.2 with a white cross on the front, a handle and two clasps."""
    prism("Box", rounded_rect(0.3, 0.16, 0.02), -0.1, 0.1, "green", plane='xy', bevel=0.008, segments=2)
    box("LidLine", (0, 0.03, 0), (0.304, 0.006, 0.204), "white", bevel=0.001)
    box("CrossH", (0, -0.015, 0.101), (0.11, 0.036, 0.004), "white", bevel=0.001)
    box("CrossV", (0, -0.015, 0.101), (0.036, 0.11, 0.004), "white", bevel=0.001)
    tube("Handle", [(-0.06, 0.08, 0), (-0.055, 0.105, 0), (0.055, 0.105, 0), (0.06, 0.08, 0)], 0.008, "white", seg=6)
    for x in (-0.09, 0.09):
        box(f"Clasp{x}", (x, 0.035, 0.102), (0.03, 0.04, 0.006), "steel", bevel=0.002)

@prop
def lost_box():
    """An open wooden box for handed-in things, 0.5 x 0.3 x 0.34, with odds and ends showing over the rim."""
    box("Bottom", (0, 0.015, 0), (0.5, 0.03, 0.34), "wood_dark", bevel=0.004)
    for z in (-0.16, 0.16):
        box(f"Side{z}", (0, 0.15, z), (0.5, 0.3, 0.025), "wood_light", bevel=0.006)
    for x in (-0.24, 0.24):
        box(f"End{x}", (x, 0.15, 0), (0.025, 0.3, 0.34), "wood", bevel=0.006)
    box("Fill", (0, 0.12, 0), (0.44, 0.2, 0.29), "wood_dark", bevel=0)
    blob("Hat", (-0.1, 0.25, 0.02), (0.1, 0.05, 0.1), "yellow", subdiv=2, rough=0.05, seed=4.0)
    box("Book", (0.1, 0.24, -0.03), (0.16, 0.03, 0.12), "blue", rot=(6, 20, 4), bevel=0.004)
    lathe("Bottle", (0.13, 0.27, 0.08), [(0.0, 0), (0.03, 0), (0.03, 0.1), (0.012, 0.13), (0.012, 0.15), (0.0, 0.15)], "teal", seg=10, rot=(55, 30, 0))
    tube("Flop", [(-0.02, 0.26, -0.1), (0.02, 0.27, -0.06), (0.03, 0.26, -0.02)], 0.022, "pink", seg=6)

# ======================================================================================== the buildings
# ======================================================================================== island 1 fun

@prop
def beach_hut():
    """Island 1's little beach hut (hut-local: floor centre at the origin, the doorway faces +z): a plank deck,
    candy-striped board walls (pink and white), a red gable roof, a heart on the front gable. The doorway is
    0.9 x 2.1 m from x -0.45; the scene builder hangs a real door in it."""
    rng = random.Random(5)
    H0, H1, W = 0.15, 2.55, 1.3
    box("Deck", (0, H0 / 2, 0), (2 * W + 0.3, H0, 2 * W + 0.3), "wood", bevel=0.015)
    for i, z in enumerate((-W, W)):
        for j, x in enumerate((-W, W)):
            box(f"Post{i}{j}", (x, (H0 + H1) / 2, z), (0.12, H1 - H0 + 0.05, 0.12), "white", bevel=0.01)
    def boards(name, axis, plane, out, door=None):
        n = 13
        for k in range(n):
            a = -W + 2 * W * k / n
            b = a + 2 * W / n
            mat = "pink" if k % 2 == 0 else "white"
            if door and b > door[0] and a < door[1]:
                _wall_box(f"{name}{k}Top", axis, plane, (a, b), (door[2], H1), 0.07, mat, bevel=0.004)
                continue
            _wall_box(f"{name}{k}", axis, plane, (a, b), (H0, H1), 0.07, mat, bevel=0.004)
    boards("Front", 'z', W, 1, door=(-0.45, 0.45, H0 + 2.1))
    boards("Back", 'z', -W, -1)
    boards("Left", 'x', -W, -1)
    boards("Right", 'x', W, 1)
    # Gable roof along x: two slabs, the ridge over the middle; triangle gables front and back.
    rise, over = 0.75, 0.28
    slope = math.degrees(math.atan2(rise, W))
    half = math.hypot(W, rise) + over
    for side in (1, -1):
        box(f"Roof{side}", (0, H1 + rise / 2 - 0.02, side * (W / 2 + over / 2 * 0.0)), (2 * W + 0.5, 0.07, half), "roof_red",
            rot=(side * slope, 0, 0), bevel=0.01)
    for side in (1, -1):
        zf = side * W
        t = 0.05
        add(f"Gable{side}", [(-W, H1, zf + t), (W, H1, zf + t), (0, H1 + rise, zf + t), (-W, H1, zf - t), (W, H1, zf - t), (0, H1 + rise, zf - t)],
            [(0, 1, 2), (5, 4, 3), (0, 3, 4, 1), (1, 4, 5, 2), (2, 5, 3, 0)], "white")
    box("Ridge", (0, H1 + rise + 0.03, 0), (2 * W + 0.55, 0.08, 0.1), "roof_red_dark", bevel=0.01)
    # A big red heart on the front gable.
    hy, hz = H1 + 0.32, W + 0.07
    lathe("HeartL", (-0.085, hy, hz), [(0.0, -0.03), (0.11, -0.03), (0.11, 0.03), (0.0, 0.03)], "red", seg=18, axis='z')
    lathe("HeartR", (0.085, hy, hz), [(0.0, -0.03), (0.11, -0.03), (0.11, 0.03), (0.0, 0.03)], "red", seg=18, axis='z')
    box("HeartTip", (0, hy - 0.085, hz), (0.19, 0.19, 0.06), "red", rot=(0, 0, 45), bevel=0.01)
    # Two steps up to the deck and a little doormat.
    box("Step", (0, 0.05, W + 0.32), (1.1, 0.1, 0.3), "wood_dark", bevel=0.01)
    box("Mat", (0, H0 + 0.005, W - 0.35), (0.8, 0.01, 0.45), "pink", bevel=0)

@prop
def beach_toilets():
    """Island 1's public toilets (local: floor centre at the origin, the doors face +z): a whitewashed block with a
    teal skirting band, two cubicles under a sloping plank roof with a deep overhang, a TOILETS board over the
    doors, little-woman and little-man plates, vent slats up high, a hand basin and mirror on the right-hand wall
    and a bin. The left doorway (x -1.6..-0.7, 2.1 m tall) is open: the scene builder hangs a real door in it. The
    right-hand cubicle's door is part of the model, shut, its tag turned to OCCUPIED."""
    W, D, H0, H1 = 2.3, 1.35, 0.15, 2.5
    box("Slab", (0, H0 / 2, 0.15), (2 * W + 0.5, H0, 2 * D + 0.6), "concrete", bevel=0.02)
    t = 0.14
    def wall_x(name, x, z0, z1, y0, y1, mat="cream"):
        box(name, (x, (y0 + y1) / 2, (z0 + z1) / 2), (t, y1 - y0, z1 - z0), mat, bevel=0.01)
    def wall_z(name, z, x0, x1, y0, y1, mat="cream"):
        box(name, ((x0 + x1) / 2, (y0 + y1) / 2, z), (x1 - x0, y1 - y0, t), mat, bevel=0.01)
    wall_z("Back", -D, -W, W, H0, H1)
    wall_x("Left", -W, -D, D, H0, H1)
    wall_x("Right", W, -D, D, H0, H1)
    wall_x("Middle", 0, -D, D, H0, H1)
    door_top = H0 + 2.1
    for name, x0, x1 in (("FrontL", -W, -1.6), ("FrontM", -0.7, 0.7), ("FrontR", 1.6, W)):
        wall_z(name, D, x0, x1, H0, H1)
    wall_z("LintelL", D, -1.6, -0.7, door_top, H1)
    wall_z("LintelR", D, 0.7, 1.6, door_top, H1)
    # A teal skirting band round the outside, and door frames.
    for name, c, s in (("BandFront", (0, 0.55, D + 0.075), (2 * W + 0.16, 0.8, 0.02)), ("BandBack", (0, 0.55, -D - 0.075), (2 * W + 0.16, 0.8, 0.02)),
                       ("BandLeft", (-W - 0.075, 0.55, 0), (0.02, 0.8, 2 * D + 0.16)), ("BandRight", (W + 0.075, 0.55, 0), (0.02, 0.8, 2 * D + 0.16))):
        if name == "BandFront":
            for k, (x0, x1) in enumerate(((-W - 0.08, -1.6), (-0.7, 0.7), (1.6, W + 0.08))):
                box(f"{name}{k}", ((x0 + x1) / 2, 0.55, D + 0.075), (x1 - x0, 0.8, 0.02), "teal", bevel=0.004)
            continue
        box(name, c, s, "teal", bevel=0.004)
    for cx in (-1.15, 1.15):
        for side in (-1, 1):
            box(f"Jamb{cx}{side}", (cx + side * 0.48, H0 + 1.05, D + 0.08), (0.07, 2.12, 0.05), "teal_dark", bevel=0.008)
        box(f"Head{cx}", (cx, door_top + 0.04, D + 0.08), (1.03, 0.08, 0.05), "teal_dark", bevel=0.008)
    # The right-hand door: shut, planked, with its OCCUPIED tag.
    for i in range(5):
        box(f"ShutPlank{i}", (0.7 + 0.09 + i * 0.18, H0 + 1.05, D + 0.02), (0.17, 2.08, 0.04), "teal", bevel=0.006)
    box("ShutKnob", (1.52, H0 + 1.0, D + 0.065), (0.05, 0.05, 0.05), "steel", bevel=0.015)
    box("TagPlate", (1.15, H0 + 1.35, D + 0.055), (0.4, 0.1, 0.015), "white", bevel=0.004)
    text3d("TagText", "OCCUPIED", (1.15, H0 + 1.35, D + 0.066), 0.05, 0.006, "red")
    # Little-woman and little-man plates on the wall beside each door.
    def figure(cx, cy, z, dress):
        lathe(f"FigHead{cx}", (cx, cy + 0.11, z), [(0.0, -0.006), (0.032, -0.006), (0.032, 0.006), (0.0, 0.006)], "dark", seg=16, axis='z')
        if dress:
            prism(f"Dress{cx}", [(-0.055, -0.05), (0.055, -0.05), (0.022, 0.07), (-0.022, 0.07)], z - 0.006, z + 0.006, "dark", plane='xy', centre=(cx, cy - 0.01, 0))
        else:
            box(f"Body{cx}", (cx, cy, z), (0.06, 0.12, 0.012), "dark", bevel=0.003)
        box(f"LegL{cx}", (cx - 0.016, cy - 0.1, z), (0.022, 0.09, 0.012), "dark", bevel=0)
        box(f"LegR{cx}", (cx + 0.016, cy - 0.1, z), (0.022, 0.09, 0.012), "dark", bevel=0)
    box("PlateL", (-1.95, 1.55, D + 0.08), (0.24, 0.32, 0.02), "white", bevel=0.01)
    figure(-1.95, 1.56, D + 0.095, True)
    box("PlateR", (1.95, 1.55, D + 0.08), (0.24, 0.32, 0.02), "white", bevel=0.01)
    figure(1.95, 1.56, D + 0.095, False)
    # Vent slats up high in the front.
    for i, x in enumerate((-0.35, 0.35)):
        box(f"VentFrame{i}", (x, H1 - 0.22, D + 0.075), (0.5, 0.24, 0.02), "teal_dark", bevel=0.004)
        for k in range(4):
            box(f"Slat{i}{k}", (x, H1 - 0.3 + k * 0.055, D + 0.09), (0.46, 0.03, 0.02), "white", rot=(-25, 0, 0), bevel=0.002)
    # The roof: planks sloping down to the back, a deep overhang over the doors, a fascia board.
    over, rise = 0.65, 0.25
    slope = math.degrees(math.atan2(rise, 2 * D + over + 0.2))
    for i in range(16):
        x = -W - 0.2 + (i + 0.5) * (2 * W + 0.4) / 16
        box(f"RoofPlank{i}", (x, H1 + rise / 2 + 0.06, over / 2), ((2 * W + 0.4) / 16 - 0.012, 0.06, 2 * D + over + 0.3),
            "wood_light" if i % 3 else "wood", rot=(-slope, 0, 0), bevel=0.006)
    for side in (-1, 1):
        box(f"Beam{side}", (side * (W - 0.1), H1 + 0.04, over / 2), (0.12, 0.1, 2 * D + over + 0.25), "wood_dark", rot=(-slope, 0, 0), bevel=0.01)
    box("Fascia", (0, H1 + rise + 0.03, D + over + 0.12), (2 * W + 0.45, 0.18, 0.05), "teal_dark", bevel=0.01)
    for side in (-1, 1):
        box(f"Bracket{side}", (side * (W - 0.15), H1 - 0.1, D + over * 0.5), (0.08, 0.5, 0.08), "wood_dark", rot=(40, 0, 0), bevel=0.008)
    # The TOILETS board on the wall between the doors (under the overhang it would hide in the shade).
    box("Board", (0, 1.85, D + 0.1), (1.25, 0.32, 0.05), "white", bevel=0.015)
    text3d("BoardText", "TOILETS", (0, 1.85, D + 0.13), 0.17, 0.02, "teal_dark")
    # Hand basin and mirror on the right-hand outside wall, a bin beside it.
    bx = W + 0.075
    lathe("Basin", (bx + 0.22, 0.85, 0.3), [(0.0, -0.08), (0.17, -0.08), (0.2, 0.05), (0.17, 0.06), (0.0, -0.04)], "white", seg=20)
    box("BasinPost", (bx + 0.2, 0.42, 0.3), (0.1, 0.78, 0.1), "white", bevel=0.01)
    box("Tap", (bx + 0.06, 1.0, 0.3), (0.12, 0.03, 0.03), "steel", bevel=0.008)
    box("TapRiser", (bx + 0.02, 0.95, 0.3), (0.03, 0.1, 0.03), "steel", bevel=0.008)
    box("MirrorFrame", (bx + 0.012, 1.55, 0.3), (0.02, 0.5, 0.4), "teal_dark", bevel=0.006)
    box("Mirror", (bx + 0.025, 1.55, 0.3), (0.012, 0.42, 0.32), "glass", bevel=0.002)
    lathe("Bin", (bx + 0.35, 0.0, -0.55), [(0.0, 0.0), (0.2, 0.0), (0.22, 0.62), (0.24, 0.66), (0.0, 0.66)], "green", seg=18)
    lathe("BinLid", (bx + 0.35, 0.66, -0.55), [(0.0, 0.0), (0.25, 0.0), (0.25, 0.04), (0.0, 0.08)], "teal_dark", seg=18)
    box("Step", (-1.15, 0.05, D + 0.5), (1.0, 0.1, 0.32), "concrete", bevel=0.01)


@prop
def beach_bar():
    """Island 1's beach bar (local: floor centre at the origin, the counter and stools face +z): a plank deck, four
    bamboo posts under a shaggy thatched roof, a bamboo-fronted counter with a dark wood top, a back shelf of
    bottles, four stools, a BEACH BAR board on the roof and a string of coloured bulbs along the front eave."""
    rng = random.Random(11)
    W, D, H0 = 2.1, 1.5, 0.15
    box("Deck", (0, H0 / 2, 0), (2 * W + 0.4, H0, 2 * D + 0.4), "wood", bevel=0.015)
    for i in range(12):
        z = -D - 0.1 + (i + 0.5) * (2 * D + 0.2) / 12
        box(f"DeckBoard{i}", (0, H0 + 0.004, z), (2 * W + 0.35, 0.008, (2 * D + 0.2) / 12 - 0.02), "wood_light" if i % 2 else "wood", bevel=0)
    top = 2.55
    def bamboo(name, x, z, y0, y1, r=0.07):
        lathe(name, (x, y0, z), [(0.0, 0.0), (r, 0.0), (r, y1 - y0), (0.0, y1 - y0)], "bamboo", seg=10)
        n = int((y1 - y0) / 0.45)
        for k in range(1, n + 1):
            yk = y0 + k * (y1 - y0) / (n + 1)
            lathe(f"{name}Ring{k}", (x, yk - 0.02, z), [(0.0, 0.0), (r + 0.012, 0.0), (r + 0.012, 0.04), (0.0, 0.04)], "bamboo_dark", seg=10)
    for i, (x, z) in enumerate(((-W, -D), (W, -D), (-W, D), (W, D))):
        bamboo(f"Post{i}", x, z, H0, top + 0.1, 0.08)
    # The counter along the front: bamboo canes on its face, a thick dark top overhanging both sides.
    cz, ch = D - 0.35, 1.1
    for i in range(30):
        x = -W + 0.15 + i * (2 * W - 0.3) / 29
        lathe(f"Cane{i}", (x, H0, cz + 0.22), [(0.0, 0.0), (0.045, 0.0), (0.045, ch - H0 - 0.06), (0.0, ch - H0 - 0.06)], "bamboo" if i % 2 else "bamboo_dark", seg=8)
    box("CounterBody", (0, (H0 + ch) / 2, cz), (2 * W - 0.2, ch - H0, 0.4), "wood_dark", bevel=0.01)
    box("CounterTop", (0, ch + 0.035, cz + 0.05), (2 * W + 0.1, 0.07, 0.7), "wood_dark", bevel=0.015)
    box("CounterEdge", (0, ch + 0.035, cz + 0.4), (2 * W + 0.1, 0.09, 0.03), "teal_dark", bevel=0.006)
    # Back shelf with bottles.
    sz = -D + 0.2
    box("ShelfBack", (0, 1.25, sz - 0.12), (2 * W - 0.4, 1.8, 0.04), "wood", bevel=0.01)
    for k, y in enumerate((0.95, 1.45, 1.95)):
        box(f"Shelf{k}", (0, y, sz), (2 * W - 0.4, 0.04, 0.26), "wood_light", bevel=0.006)
        for b in range(9):
            x = -W + 0.45 + b * (2 * W - 0.9) / 8 + rng.uniform(-0.04, 0.04)
            mat = rng.choice(["bottle_green", "bottle_amber", "bottle_blue", "glass"])
            h = rng.uniform(0.22, 0.32)
            lathe(f"Bottle{k}{b}", (x, y + 0.02, sz), [(0.0, 0.0), (0.045, 0.0), (0.045, h * 0.6), (0.018, h * 0.78), (0.016, h), (0.0, h)], mat, seg=10)
    box("BarBelowShelf", (0, 0.5, sz), (2 * W - 0.4, 0.7, 0.26), "teal", bevel=0.01)
    # Coconut cups on the counter (decoration; the real drinks are items).
    for i, x in enumerate((-1.4, 1.5)):
        blob(f"CocoCup{i}", (x, ch + 0.13, cz - 0.05), (0.075, 0.065, 0.075), "husk", subdiv=2, rough=0.04, seed=i)
        tube(f"Straw{i}", [(x, ch + 0.17, cz - 0.05), (x + 0.03, ch + 0.3, cz - 0.05)], 0.006, "red", seg=6)
    # Stools in front of the counter.
    for i in range(4):
        x = -1.5 + i * 1.0
        z = D + 0.35
        lathe(f"StoolSeat{i}", (x, 0.72, z), [(0.0, 0.0), (0.2, 0.0), (0.21, 0.04), (0.19, 0.08), (0.0, 0.08)], "teal" if i % 2 else "orange", seg=18)
        lathe(f"StoolPost{i}", (x, 0.0, z), [(0.0, 0.0), (0.035, 0.0), (0.035, 0.72), (0.0, 0.72)], "steel", seg=8)
        lathe(f"StoolFoot{i}", (x, 0.0, z), [(0.0, 0.0), (0.18, 0.0), (0.16, 0.03), (0.0, 0.03)], "steel", seg=14)
        lathe(f"StoolRing{i}", (x, 0.3, z), [(0.13, 0.0), (0.15, 0.0), (0.15, 0.025), (0.13, 0.025)], "steel", seg=14)
    # Thatched hip roof in three tiers, each overhanging the one above it with a shaggy fringe, like palm thatch.
    peak = top + 1.05
    ov = 0.55
    base = [(-W - ov, -D - ov), (W + ov, -D - ov), (W + ov, D + ov), (-W - ov, D + ov)]
    for tier, (shrink, lift) in enumerate(((1.0, 0.0), (0.7, 0.34), (0.42, 0.64))):
        y0 = top + (peak - top) * lift + tier * 0.06
        cs = [(x * shrink, z * shrink) for x, z in base]
        ra, rb = (-W * 0.35 * shrink, peak + tier * 0.06, 0), (W * 0.35 * shrink, peak + tier * 0.06, 0)
        slabs = []
        for i in range(4):
            (x0, z0), (x1, z1) = cs[i], cs[(i + 1) % 4]
            if i == 1:
                verts, faces = [(x0, y0, z0), (x1, y0, z1), rb], [(0, 1, 2)]
            elif i == 3:
                verts, faces = [(x0, y0, z0), (x1, y0, z1), ra], [(0, 1, 2)]
            elif i == 0:
                verts, faces = [(x0, y0, z0), (x1, y0, z1), rb, ra], [(0, 1, 2, 3)]
            else:
                verts, faces = [(x0, y0, z0), (x1, y0, z1), ra, rb], [(0, 1, 2, 3)]
            slabs.append(add(f"Thatch{tier}{i}", verts, faces, ["straw", "straw_light", "straw"][tier]))
        for o in slabs:  # some thickness
            mod = o.modifiers.new("solid", 'SOLIDIFY'); mod.thickness = 0.12; mod.offset = 0
            bpy.context.view_layer.objects.active = o; bpy.ops.object.modifier_apply(modifier=mod.name)
        for i in range(4):
            (x0, z0), (x1, z1) = cs[i], cs[(i + 1) % 4]
            n = max(10, int(26 * shrink))
            ang = math.degrees(math.atan2(x1 - x0, z1 - z0))
            for k in range(n):
                u = (k + 0.5) / n
                x, z = x0 + (x1 - x0) * u, z0 + (z1 - z0) * u
                length = rng.uniform(0.26, 0.4) * (1.0 if tier == 0 else 0.75)
                box(f"Fringe{tier}{i}{k}", (x, y0 - length / 2 + 0.06, z), (0.2 * max(shrink, 0.6), length, 0.05), rng.choice(["straw", "straw_dark", "straw_light"]),
                    rot=(rng.uniform(-8, 8), ang + 90, rng.uniform(-6, 6)), bevel=0)
    box("RidgeCap", (0, peak + 0.15, 0), (W * 0.35 * 2 * 0.42 + 0.3, 0.12, 0.22), "straw_dark", bevel=0.03)
    # The BEACH BAR board on the front, and a string of coloured bulbs along the front eave.
    box("SignBoard", (0, top + 0.42, D + 0.12), (2.0, 0.42, 0.06), "teal_dark", bevel=0.02)
    text3d("SignText", "BEACH BAR", (0, top + 0.42, D + 0.16), 0.24, 0.025, "yellow")
    for side in (-1, 1):
        box(f"SignPost{side}", (side * 0.8, top + 0.12, D + 0.1), (0.06, 0.5, 0.06), "bamboo_dark", bevel=0.01)
    bulbs = ["red", "yellow", "teal", "orange", "green", "blue"]
    path = []
    for k in range(13):
        u = k / 12
        x = -W - 0.3 + (2 * W + 0.6) * u
        y = top - 0.12 - math.sin(u * math.pi * 3) ** 2 * 0.18
        path.append((x, y, D + 0.42))
    tube("BulbWire", path, 0.008, "dark", seg=5)
    for k, (x, y, z) in enumerate(path[1:-1]):
        blob(f"Bulb{k}", (x, y - 0.06, z), (0.035, 0.05, 0.035), bulbs[k % len(bulbs)], subdiv=1, rough=0.0)
    # A chalkboard menu by the left post.
    box("MenuFrame", (-W - 0.34, 0.95, D - 0.1), (0.03, 0.78, 0.63), "wood_dark", bevel=0.01)
    box("MenuBoard", (-W - 0.36, 0.95, D - 0.1), (0.02, 0.7, 0.55), "chalk", bevel=0.005)
    text3d("MenuText", "DRINKS", (-W - 0.375, 1.15, D - 0.1), 0.08, 0.006, "white", face='-x')
    box("MenuLeg", (-W - 0.34, 0.3, D - 0.1), (0.05, 0.6, 0.05), "wood_dark", bevel=0.008)

@prop
def basketball_hoop():
    """A beach basketball hoop (hoop-local: the pole's foot at z -1.0, the backboard's face at z 0 facing +z, the
    rim's centre at (0, 3.05, 0.38), 0.23 m across its middle). The net is its own model (basketball_net)."""
    box("Base", (0, 0.15, -1.0), (0.7, 0.3, 0.7), "concrete", bevel=0.03)
    tube("Pole", [(0, 0.2, -1.0), (0, 3.6, -1.0)], 0.07, "blue_dark", seg=12)
    tube("Arm", [(0, 3.45, -1.0), (0, 3.45, -0.12)], 0.05, "blue_dark", seg=10)
    tube("Brace", [(0, 2.8, -1.0), (0, 3.3, -0.12)], 0.035, "blue_dark", seg=8)
    box("Board", (0, 3.43, -0.03), (1.8, 1.05, 0.05), "white", bevel=0.01)
    for name, centre, size in (("FrameT", (0, 3.94, 0.0), (1.8, 0.05, 0.02)), ("FrameB", (0, 2.92, 0.0), (1.8, 0.05, 0.02)),
                               ("FrameL", (-0.88, 3.43, 0.0), (0.05, 1.05, 0.02)), ("FrameR", (0.88, 3.43, 0.0), (0.05, 1.05, 0.02)),
                               ("BoxT", (0, 3.48, 0.0), (0.6, 0.04, 0.02)), ("BoxB", (0, 3.08, 0.0), (0.6, 0.04, 0.02)),
                               ("BoxL", (-0.28, 3.28, 0.0), (0.04, 0.44, 0.02)), ("BoxR", (0.28, 3.28, 0.0), (0.04, 0.44, 0.02))):
        box(name, centre, size, "red", bevel=0.004)
    tube("Rim", circle((0, 3.05, 0.38), 0.23, n=28), 0.012, "orange", seg=8, closed=True)
    box("RimBracket", (0, 3.03, 0.07), (0.18, 0.05, 0.14), "orange", bevel=0.005)

@prop
def basketball_net():
    """The net under the rim (net-local: the rim's centre at the origin), white cord narrowing to a smaller ring."""
    top = circle((0, 0, 0), 0.225, n=12)
    bottom = circle((0, -0.42, 0), 0.14, n=12)
    for i in range(12):
        tube(f"CordA{i}", [top[i], bottom[(i + 1) % 12]], 0.005, "white", seg=4)
        tube(f"CordB{i}", [top[(i + 1) % 12], bottom[i]], 0.005, "white", seg=4)
    tube("Hem", circle((0, -0.42, 0), 0.14, n=16), 0.006, "white", seg=4, closed=True)
    tube("Mid", circle((0, -0.21, 0), 0.185, n=16), 0.005, "white", seg=4, closed=True)

@prop
def basketball():
    """A 0.24 m basketball: orange with the black seams (two great circles and the two curved ones)."""
    seg, rings, r = 48, 28, 0.12
    prof = [(math.sin(math.pi * i / rings) * r, -math.cos(math.pi * i / rings) * r) for i in range(rings + 1)]
    ob = lathe("Ball", (0, 0, 0), prof, "orange", seg=seg, turn=0.0)
    # Continuous narrow seams instead of assigning whole faces to black; the
    # latter produced visible stair steps when holding the ball near the camera.
    for axis in (0, 1):
        points = []
        for i in range(128):
            a = 2 * math.pi * i / 128
            p = [r * math.cos(a), r * math.sin(a), 0.0]
            if axis == 0: p = [0.0, p[0], p[1]]
            else: p = [p[0], 0.0, p[1]]
            points.append(tuple(p))
        tube(f"Seam{axis}", points, 0.0015, "black", seg=6, closed=True)
    for side in (-1, 1):
        z = side * 0.62 * r
        radius = math.sqrt(r * r - z * z)
        points = [(radius * math.cos(2 * math.pi * i / 128), radius * math.sin(2 * math.pi * i / 128), z) for i in range(128)]
        tube(f"CurvedSeam{side}", points, 0.0015, "black", seg=6, closed=True)


@prop
def trampoline():
    """A round beach trampoline 2.8 m across (local: centre of the ground under it): black mat 0.75 m up, blue padded
    rim, six bent steel legs, and springs round the edge."""
    R, H = 1.4, 0.75
    lathe("Mat", (0, H, 0), [(0.0, 0.0), (R - 0.12, 0.0), (R - 0.12, -0.02), (0.0, -0.02)], "black", seg=32)
    tube("Pad", circle((0, H + 0.02, 0), R, n=40), 0.09, "blue", seg=10, closed=True)
    tube("Frame", circle((0, H - 0.06, 0), R, n=40), 0.035, "steel", seg=8, closed=True)
    for i in range(6):
        t = 2 * math.pi * i / 6
        c, s = math.cos(t), math.sin(t)
        tube(f"Leg{i}", [(c * R, H - 0.06, s * R), (c * (R + 0.05), 0.35, s * (R + 0.05)), (c * (R - 0.1), 0.02, s * (R - 0.1))], 0.03, "steel", seg=8)
    for i in range(24):
        t = 2 * math.pi * (i + 0.5) / 24
        c, s = math.cos(t), math.sin(t)
        tube(f"Spring{i}", [(c * (R - 0.12), H - 0.01, s * (R - 0.12)), (c * (R - 0.02), H - 0.04, s * (R - 0.02))], 0.012, "steel", seg=5)

@prop
def human_cannon():
    """A circus cannon's wooden carriage (local: its middle on the ground, facing +z). The wheels (cannon_wheel,
    cannon_wheel_small) and the barrel (cannon_barrel) are separate models, so the game can roll the wheels as the
    cannon turns and tip the barrel up and down: big wheels at (+-0.62, 0.48, -0.5), small ones at (+-0.62, 0.32,
    0.55), the barrel's pivot at (0, 1.0, 0)."""
    box("Carriage", (0, 0.55, -0.1), (1.0, 0.5, 1.8), "wood", bevel=0.03)
    for side in (-1, 1):
        box(f"Cheek{side}", (side * 0.42, 0.95, 0.0), (0.16, 0.5, 0.8), "wood_dark", bevel=0.03)
        tube(f"Axle{side}", [(side * 0.45, 0.48, -0.5), (side * 0.6, 0.48, -0.5)], 0.05, "steel", seg=8)
        tube(f"AxleF{side}", [(side * 0.45, 0.32, 0.55), (side * 0.6, 0.32, 0.55)], 0.04, "steel", seg=8)
    box("Trail", (0, 0.32, -1.2), (0.35, 0.18, 0.7), "wood_dark", rot=(-12, 0, 0), bevel=0.02)
    # A push bar off the end of the trail, wheelbarrow style, to roll it about the beach by (hands at x +-0.24,
    # y 0.93, z -1.9: HumanCannon.BarLeft/BarRight).
    for side in (-1, 1):
        tube(f"Strut{side}", [(side * 0.13, 0.36, -1.48), (side * 0.2, 0.62, -1.66), (side * 0.27, 0.93, -1.9)], 0.035, "wood_dark", seg=8)
    tube("PushBar", [(-0.36, 0.93, -1.9), (0.36, 0.93, -1.9)], 0.034, "wood", seg=10)
    for side in (-1, 1):
        tube(f"Grip{side}", [(side * 0.15, 0.93, -1.9), (side * 0.33, 0.93, -1.9)], 0.042, "black", seg=10)

def cannon_wheel_of(radius):
    """A spoked cart wheel round the x axis (local: its hub at the origin)."""
    rim = [(0, (radius - 0.04) * math.cos(k * math.pi / 12), (radius - 0.04) * math.sin(k * math.pi / 12)) for k in range(24)]
    tube("Rim", rim, 0.045, "wood_dark", seg=8, closed=True)
    lathe("Hub", (0, 0, 0), [(0.0, -0.08), (0.1, -0.08), (0.1, 0.08), (0.0, 0.08)], "yellow", seg=12, axis='x')
    for k in range(6):
        box(f"Spoke{k}", (0, 0, 0), (0.05, radius * 2 - 0.1, 0.05), "wood", rot=(k * 30, 0, 0), bevel=0.005)

@prop
def cannon_wheel():
    cannon_wheel_of(0.48)

@prop
def cannon_wheel_small():
    cannon_wheel_of(0.32)

@prop
def cannon_barrel():
    """The human cannon's barrel (local: its pivot at the origin, the mouth 2.0 m out along +z, level). Red with
    yellow bands, trunnions on the sides and a fuse at the back."""
    lathe("Barrel", (0, 0, 0), [(0.0, -0.9), (0.42, -0.9), (0.42, -0.75), (0.36, -0.7), (0.32, 1.9), (0.4, 1.95), (0.4, 2.05), (0.3, 2.05), (0.28, 1.0), (0.0, 1.0)],
          "red", seg=24, axis='y', rot=(90, 0, 0))
    for k, at in enumerate((-0.2, 0.6, 1.4)):
        lathe(f"Band{k}", (0, 0, 0), [(0.37, at - 0.06), (0.39, at - 0.06), (0.39, at + 0.06), (0.37, at + 0.06)], "yellow", seg=24, axis='y', rot=(90, 0, 0))
    lathe("Trunnions", (0, 0, 0), [(0.0, -0.5), (0.08, -0.5), (0.08, 0.5), (0.0, 0.5)], "steel", seg=10, axis='x')
    tube("Fuse", [(0, 0.38, -0.8), (0.05, 0.55, -1.0), (0.12, 0.6, -1.1)], 0.015, "rope_dark", seg=5)

@prop
def diving_board():
    """A springboard (local: its foot at the origin on the dock, the board reaching 2.6 m out along +z, 0.45 m up):
    a white board with a blue grip strip on a red fulcrum and a steel base."""
    box("Base", (0, 0.12, 0.2), (0.6, 0.24, 0.6), "steel", bevel=0.02)
    box("Fulcrum", (0, 0.3, 0.9), (0.55, 0.3, 0.2), "red", bevel=0.03)
    box("Board", (0, 0.45, 1.3), (0.55, 0.07, 2.6), "white", bevel=0.02)
    box("Grip", (0, 0.488, 1.6), (0.45, 0.01, 1.8), "blue", bevel=0)
    for side in (-1, 1):
        tube(f"Rail{side}", [(side * 0.32, 0.24, 0.0), (side * 0.32, 1.0, 0.0), (side * 0.32, 1.0, 0.5), (side * 0.32, 0.5, 0.75)], 0.025, "steel", seg=8)

@prop
def zipline_post():
    """The zipline's far end: a tall post standing in the sea with a crossbar and a red-and-white float ring
    (local: foot on the seabed at the origin; the cable's eye 6.5 m up)."""
    tube("Post", [(0, 0, 0), (0, 7.0, 0)], 0.16, "wood_dark", seg=12)
    box("Cross", (0, 6.5, 0), (1.2, 0.18, 0.18), "wood", bevel=0.02)
    lathe("Eye", (0, 6.5, 0.2), [(0.08, -0.03), (0.13, -0.03), (0.13, 0.03), (0.08, 0.03)], "steel", seg=12, axis='z')
    for k in range(6):
        lathe(f"Stripe{k}", (0, 4.6 + k * 0.25, 0), [(0.17, 0), (0.19, 0), (0.19, 0.25), (0.17, 0.25)], "red" if k % 2 == 0 else "white", seg=12)

@prop
def zipline_handle():
    """The trolley you hang from (local: the wheels on the cable at the origin, the bar 0.55 m below)."""
    box("Trolley", (0, 0, 0), (0.12, 0.16, 0.3), "steel", bevel=0.02)
    for z in (-0.09, 0.09):
        lathe(f"Wheel{z}", (0, 0.05, z), [(0.0, -0.025), (0.06, -0.025), (0.06, 0.025), (0.0, 0.025)], "dark", seg=12, axis='x')
    tube("Straps", [(-0.25, -0.55, 0), (0, -0.05, 0), (0.25, -0.55, 0)], 0.015, "yellow", seg=6)
    tube("Bar", [(-0.3, -0.55, 0), (0.3, -0.55, 0)], 0.025, "black", seg=8)

@prop
def banana_boat():
    """A yellow inflatable banana boat 4.2 m long (local: nose toward +z, the bottom at y 0): a curved main tube
    with green-brown tips, two side tubes to rest the feet on, red grab handles and a little tow ring at the nose."""
    def banana_path(n=14, length=4.2, rise=0.35, y=0.33, x=0.0):
        pts = []
        for i in range(n + 1):
            t = i / n
            z = (t - 0.5) * length
            pts.append((x, y + rise * (2 * t - 1) ** 2, z))
        return pts
    ob = tube("Banana", banana_path(), 0.33, "yellow", seg=16)
    lathe("TipFront", (0, 0.68, 2.1), [(0.33, 0.0), (0.22, 0.25), (0.0, 0.42)], "green", seg=16, axis='z')
    lathe("TipBack", (0, 0.68, -2.1), [(0.33, 0.0), (0.22, -0.25), (0.0, -0.42)], "wood_dark", seg=16, axis='z')
    for side in (-1, 1):
        tube(f"Side{side}", banana_path(n=10, length=3.0, rise=0.15, y=0.18, x=side * 0.55), 0.17, "yellow", seg=12)
        lathe(f"SideCapF{side}", (side * 0.55, 0.33, 1.5), [(0.17, 0.0), (0.0, 0.17)], "yellow", seg=12, axis='z')
        lathe(f"SideCapB{side}", (side * 0.55, 0.33, -1.5), [(0.17, 0.0), (0.0, -0.17)], "yellow", seg=12, axis='z')
    for k, z in enumerate((1.2, 0.2, -0.8)):
        tube(f"Handle{k}", [(-0.18, 0.66, z), (-0.12, 0.8, z), (0.12, 0.8, z), (0.18, 0.66, z)], 0.025, "red", seg=6)
    lathe("TowRing", (0, 0.75, 2.45), [(0.05, -0.012), (0.08, -0.012), (0.08, 0.012), (0.05, 0.012)], "steel", seg=12, axis='x')
    # Brown sugar spots, because it's a banana.
    rng = random.Random(3)
    def pick(c, n):
        if c.y > 0.45 and abs(c.z) < 1.9 and rng.random() < 0.02: return "wood_dark"
        return None
    paint(ob, pick)

@prop
def parrot():
    """A cartoon macaw perched upright (local: its feet at the origin, beak toward +z, 0.6 m tall): a red body and
    head, white cheeks, big googly eyes, a hooked yellow beak and a long blue-and-red tail hanging behind. The wings
    are a separate model (parrot_wing) so the game can flap them: shoulders at (+-0.1, 0.3, -0.02)."""
    blob("Body", (0, 0.24, -0.02), (0.11, 0.16, 0.12), "red", subdiv=3, rough=0.03, rot=(-15, 0, 0))
    blob("Head", (0, 0.43, 0.05), (0.095, 0.095, 0.1), "red", subdiv=3, rough=0.02)
    blob("Belly", (0, 0.2, 0.06), (0.08, 0.1, 0.06), "orange", subdiv=2, rough=0.03)
    for side in (-1, 1):
        blob(f"Cheek{side}", (side * 0.065, 0.42, 0.1), (0.03, 0.045, 0.04), "white", subdiv=2, rough=0.0)
        blob(f"Eye{side}", (side * 0.07, 0.47, 0.11), (0.04, 0.04, 0.025), "white", subdiv=2, rough=0.0)
        blob(f"Pupil{side}", (side * 0.078, 0.465, 0.132), (0.018, 0.018, 0.01), "black", subdiv=1, rough=0.0)
        box(f"Foot{side}", (side * 0.045, 0.015, 0.03), (0.035, 0.03, 0.07), "dark", bevel=0.01)
        box(f"Leg{side}", (side * 0.045, 0.06, 0.0), (0.03, 0.08, 0.03), "dark", bevel=0.01)
    lathe("Beak", (0, 0.43, 0.13), [(0.045, 0.0), (0.04, 0.03), (0.025, 0.06), (0.0, 0.09)], "yellow", seg=12, axis='z', rot=(35, 0, 0))
    lathe("BeakLow", (0, 0.4, 0.13), [(0.028, 0.0), (0.018, 0.03), (0.0, 0.045)], "dark", seg=10, axis='z', rot=(10, 0, 0))
    box("TailBlue", (0, 0.04, -0.2), (0.07, 0.02, 0.38), "blue", rot=(-58, 0, 0), bevel=0.008)
    box("TailRed", (0, 0.06, -0.17), (0.05, 0.022, 0.32), "red", rot=(-55, 0, 0), bevel=0.008)

@prop
def parrot_wing():
    """One parrot wing (local: the shoulder at the origin, folded down the side and back along -z; the left wing,
    mirror it for the right): blue on top with a yellow band and a green edge."""
    blob("Wing", (0.02, -0.08, -0.07), (0.03, 0.1, 0.15), "blue", subdiv=2, rough=0.04, rot=(-25, 0, 0))
    blob("Band", (0.035, -0.04, -0.02), (0.02, 0.06, 0.09), "yellow", subdiv=2, rough=0.02, rot=(-25, 0, 0))
    blob("Tip", (0.015, -0.15, -0.19), (0.025, 0.05, 0.08), "green", subdiv=2, rough=0.03, rot=(-35, 0, 0))

# The watch tower and the shack, modelled clean (boards, trims, glass, roofs) to the very sizes the scene builder's
# colliders use (MeshyArt.Tower / MeshyArt.Shack at TowerScale 1.2 x widen 1.5, ShackScale 1.45), so they drop in
# where the Meshy scans were: the door gaps, decks, floors, walls and stairs all line up with what you walk on.

def _segments(span, cuts):
    """span (a, b) minus the cut ranges: the pieces left, in order."""
    pieces = [span]
    for c0, c1 in cuts:
        out = []
        for a, b in pieces:
            if c1 <= a or c0 >= b: out.append((a, b)); continue
            if c0 > a: out.append((a, c0))
            if c1 < b: out.append((c1, b))
        pieces = out
    return [(a, b) for a, b in pieces if b - a > 0.01]

def _wall_box(name, axis, plane, along, y, thick, mat, out=0.0, bevel=0.004, tilt=0.0):
    """A board in a wall: axis 'z' = the wall is a plane of constant z running along x, 'x' = constant x along z."""
    (a, b), (y0, y1) = along, y
    mid, length = (a + b) / 2, b - a
    if axis == 'z':
        return box(name, (mid, (y0 + y1) / 2, plane + out), (length, y1 - y0, thick), mat, rot=(0, 0, tilt), bevel=bevel)
    return box(name, (plane + out, (y0 + y1) / 2, mid), (thick, y1 - y0, length), mat, rot=(tilt, 0, 0), bevel=bevel)

def sided_wall(name, axis, plane, outward, span, y_range, openings, board_h, tones, core, rng, core_t=0.1, board_t=0.022,
               gap=0.014, tilt=0.0, inner=None):
    """A wall: a core slab with holes for the openings (a, b, y0, y1), lapped boards on the outside and, with inner,
    boards of that colour on the inside too. outward = +1 / -1: which side of the plane is outside."""
    y0, y1 = y_range
    # Core: horizontal bands between the openings' tops and bottoms; in each band the span minus what's open there.
    edges = sorted({y0, y1} | {min(max(o[2], y0), y1) for o in openings} | {min(max(o[3], y0), y1) for o in openings})
    for i in range(len(edges) - 1):
        ya, yb = edges[i], edges[i + 1]
        if yb - ya < 0.005: continue
        cuts = [(o[0], o[1]) for o in openings if o[2] <= ya + 1e-4 and o[3] >= yb - 1e-4]
        for j, seg in enumerate(_segments(span, cuts)):
            _wall_box(f"{name}Core{i}_{j}", axis, plane, seg, (ya, yb), core_t, core, bevel=0)  # (shows in the gaps between boards)
    # Boards: rows up the wall, cut round the openings.
    row, y = 0, y0
    while y < y1 - 0.02:
        top = min(y1, y + board_h)
        cuts = [(o[0], o[1]) for o in openings if o[2] < top - 0.01 and o[3] > y + 0.01]
        for j, seg in enumerate(_segments(span, cuts)):
            _wall_box(f"{name}Board{row}_{j}", axis, plane, seg, (y + gap * 0.5, top - gap * 0.5), board_t, rng.choice(tones),
                      out=outward * (core_t / 2 + board_t / 2), bevel=0.006, tilt=rng.uniform(-tilt, tilt))
            if inner:  # boards on the inside too (a room you walk into has board walls, not a flat lining)
                _wall_box(f"{name}InBoard{row}_{j}", axis, plane, seg, (y + gap * 0.5, top - gap * 0.5), 0.012, inner,
                          out=-outward * (core_t / 2 + 0.016), bevel=0.004)
        y = top; row += 1

def window(name, axis, plane, outward, a, b, y0, y1, frame, glass, depth, sill=None, cross=True):
    """Frame round an opening (both faces), a pane in the middle, glazing bars, a sill under it outside."""
    f = 0.075
    for side, (sa, sb, ya, yb) in {"L": (a - f, a, y0, y1), "R": (b, b + f, y0, y1), "B": (a - f, b + f, y0 - f, y0), "T": (a - f, b + f, y1, y1 + f)}.items():
        _wall_box(f"{name}Frame{side}", axis, plane, (sa, sb), (ya, yb), depth, frame, bevel=0.008)
    if glass:  # None = an open hatch
        _wall_box(f"{name}Glass", axis, plane, (a, b), (y0, y1), 0.012, glass, bevel=0)
    if cross and glass:
        m = (a + b) / 2; my = (y0 + y1) / 2
        _wall_box(f"{name}BarV", axis, plane, (m - 0.02, m + 0.02), (y0, y1), 0.04, frame, bevel=0.004)
        _wall_box(f"{name}BarH", axis, plane, (a, b), (my - 0.02, my + 0.02), 0.04, frame, bevel=0.004)
    if sill:
        _wall_box(f"{name}Sill", axis, plane, (a - f - 0.04, b + f + 0.04), (y0 - f - 0.05, y0 - f + 0.005), depth + 0.12, sill,
                  out=outward * 0.04, bevel=0.008)

def life_ring_at(name, centre, axis, radius=0.3, tube_r=0.075):
    pts = circle(centre, radius, n=28, axis=axis)
    ob = tube(name, pts, tube_r, "red", seg=10, closed=True)
    c = Vector(centre)
    def pick(p, n):
        d = p - c
        if axis == 'x': ang = math.degrees(math.atan2(d.y, d.z))
        elif axis == 'z': ang = math.degrees(math.atan2(d.y, d.x))
        else: ang = math.degrees(math.atan2(d.z, d.x))
        return "white" if (ang + 360 + 22.5) % 90 < 45 else None
    paint(ob, pick)
    tube(name + "Rope", circle(centre, radius, n=28, axis=axis), tube_r * 0.18, "rope", seg=5, closed=True)

def railing(name, a, b, fixed, along_x, y_floor, height, rng, post_every=1.0, colour="white"):
    """Posts, a top rail and a middle rail from a to b along x (at z = fixed) or along z (at x = fixed)."""
    n = max(1, round(abs(b - a) / post_every))
    for i in range(n + 1):
        t = a + (b - a) * i / n
        p = (t, y_floor + height / 2, fixed) if along_x else (fixed, y_floor + height / 2, t)
        box(f"{name}Post{i}", p, (0.08, height, 0.08), colour, bevel=0.01)
    mid = (a + b) / 2; length = abs(b - a) + 0.08
    for k, (yy, w, h) in enumerate(((y_floor + height, 0.1, 0.05), (y_floor + height * 0.5, 0.05, 0.05))):
        c = (mid, yy, fixed) if along_x else (fixed, yy, mid)
        s = (length, h, w) if along_x else (w, h, length)
        box(f"{name}Rail{k}", c, s, colour, bevel=0.01)

def stairs(name, x_mid, width, low, high, steps, tread, riser, stringer, rail=None, rail_h=0.9):
    """A flight from low (y, z) up to high (y, z) along -z: stringers, a tread per step on the slope line, closed risers."""
    (yl, zl), (yh, zh) = low, high
    run = zl - zh
    for i in range(1, steps + 1):
        t = i / steps
        y = yl + (yh - yl) * t
        z = zl - run * t
        depth = run / steps + 0.05
        box(f"{name}Tread{i}", (x_mid, y - 0.025, z + depth / 2 - 0.02), (width - 0.08, 0.05, depth), tread, bevel=0.008)
        rise = (yh - yl) / steps
        box(f"{name}Riser{i}", (x_mid, y - 0.05 - rise / 2 + 0.01, z + depth - 0.04), (width - 0.12, rise - 0.03, 0.025), riser, bevel=0)
    slope = math.atan2(yh - yl, run)
    length = math.hypot(yh - yl, run)
    for side in (-1, 1):
        x = x_mid + side * (width / 2 - 0.03)
        cy, cz = (yl + yh) / 2 - 0.12, (zl + zh) / 2 + 0.08
        box(f"{name}Stringer{side}", (x, cy, cz), (0.06, 0.26, length + 0.2), stringer, rot=(math.degrees(slope), 0, 0), bevel=0.01)
        if rail:
            xr = x + side * 0.02
            box(f"{name}PostLow{side}", (xr, yl + rail_h / 2 + 0.05, zl - 0.1), (0.08, rail_h + 0.1, 0.08), rail, bevel=0.01)
            box(f"{name}PostHigh{side}", (xr, yh + rail_h / 2, zh + 0.05), (0.08, rail_h, 0.08), rail, bevel=0.01)
            tube(f"{name}Hand{side}", [(xr, yl + rail_h + 0.08, zl - 0.1), (xr, yh + rail_h, zh + 0.05)], 0.035, rail, seg=8)

@prop
def watch_tower():
    """The lifeguard watch tower (tower-local: the stairs come down toward +z, the sea): white stilts, a railed plank
    deck at 2.74 m with life rings, a white board cabin with windows on every side and a red gable roof."""
    rng = random.Random(7)
    D = 2.742                          # deck top
    xw, zf, zb = 2.718, 0.18, -2.88    # deck half width, front and back edges
    cx0, cx1, cz0, cz1, E = -1.764, 1.728, -2.34, -0.24, 5.112   # cabin walls and eaves
    door = (-1.26, -0.036, D, 5.01)
    # Stilts with footings, beams under the deck, knee braces (all above head height).
    for x in (-2.25, 2.25):
        for z in (-2.64, -0.18):
            box(f"Stilt{x}{z}", (x, (D - 0.2 - 0.3) / 2, z), (0.22, D - 0.2 + 0.3, 0.22), "white", bevel=0.02)
            box(f"Foot{x}{z}", (x, 0.02, z), (0.38, 0.14, 0.38), "concrete", bevel=0.02)
    for z in (-2.64, -0.18):
        box(f"BeamX{z}", (0, D - 0.29, z), (4.94, 0.18, 0.16), "white", bevel=0.012)
    for x in (-2.25, 2.25):
        box(f"BeamZ{x}", (x, D - 0.29, -1.41), (0.16, 0.18, 2.86), "white", bevel=0.012)
    for x in (-2.25, 2.25):
        for z, dz in ((-2.64, 1), (-0.18, -1)):
            box(f"KneeZ{x}{z}", (x, D - 0.62, z + dz * 0.3), (0.1, 0.1, 0.78), "white", rot=(dz * -45, 0, 0), bevel=0.01)
        for z in (-2.64, -0.18):
            dx = 1 if x < 0 else -1
            box(f"KneeX{x}{z}", (x + dx * 0.3, D - 0.62, z), (0.78, 0.1, 0.1), "white", rot=(0, 0, dx * 45), bevel=0.01)
    # Deck: joists, planks across, a white rim.
    for x in (-1.6, -0.55, 0.55, 1.6):
        box(f"Joist{x}", (x, D - 0.13, (zf + zb) / 2), (0.1, 0.16, zf - zb - 0.1), "wood_dark", bevel=0.01)
    z = zf
    i = 0
    while z > zb + 0.01:
        w = min(0.2, z - zb)
        box(f"DeckPlank{i}", (0, D - 0.03, z - w / 2), (2 * xw - 0.04, 0.06, w - 0.014), rng.choice(["wood_light", "wood_light", "wood"]), bevel=0.008)
        z -= w; i += 1
    for zz in (zf, zb):
        box(f"RimX{zz}", (0, D - 0.12, zz), (2 * xw + 0.06, 0.2, 0.06), "white", bevel=0.01)
    for xx in (-xw, xw):
        box(f"RimZ{xx}", (xx, D - 0.12, (zf + zb) / 2), (0.06, 0.2, zf - zb), "white", bevel=0.01)
    # Railings round the deck (open at the top of the stairs) and a life ring on each side.
    railing("RailFrontL", -xw + 0.04, -0.98, zf - 0.04, True, D, 1.0, rng)
    railing("RailFrontR", 0.98, xw - 0.04, zf - 0.04, True, D, 1.0, rng)
    railing("RailBack", -xw + 0.04, xw - 0.04, zb + 0.04, True, D, 1.0, rng)
    railing("RailL", zb + 0.04, zf - 0.04, -xw + 0.04, False, D, 1.0, rng)
    railing("RailR", zb + 0.04, zf - 0.04, xw - 0.04, False, D, 1.0, rng)
    life_ring_at("RingL", (-xw - 0.05, D + 0.62, -1.35), 'x')
    life_ring_at("RingR", (xw + 0.05, D + 0.62, -1.35), 'x')
    # The cabin: board walls with a door gap and a window on each side.
    win_y = (D + 0.95, D + 1.95)
    tones = ["siding", "siding", "siding_warm"]
    sided_wall("Front", 'z', cz1, 1, (cx0, cx1), (D, E), [door, (0.3, 1.35, *win_y)], 0.17, tones, "siding_shadow", rng, inner="siding")
    sided_wall("Back", 'z', cz0, -1, (cx0, cx1), (D, E), [(-0.6, 0.6, *win_y)], 0.17, tones, "siding_shadow", rng, inner="siding")
    sided_wall("Left", 'x', cx0, -1, (cz0, cz1), (D, E), [(-1.85, -0.75, *win_y)], 0.17, tones, "siding_shadow", rng, inner="siding")
    sided_wall("Right", 'x', cx1, 1, (cz0, cz1), (D, E), [(-1.85, -0.75, *win_y)], 0.17, tones, "siding_shadow", rng, inner="siding")
    window("WinFront", 'z', cz1, 1, 0.3, 1.35, *win_y, "trim_blue", "glass", 0.2, sill="white")
    window("WinBack", 'z', cz0, -1, -0.6, 0.6, *win_y, "trim_blue", "glass", 0.2, sill="white")
    window("WinLeft", 'x', cx0, -1, -1.85, -0.75, *win_y, "trim_blue", "glass", 0.2, sill="white")
    window("WinRight", 'x', cx1, 1, -1.85, -0.75, *win_y, "trim_blue", "glass", 0.2, sill="white")
    for x in (cx0, cx1):
        for z in (cz0, cz1):
            box(f"Corner{x}{z}", (x, (D + E) / 2, z), (0.16, E - D, 0.16), "trim_blue", bevel=0.012)
    box("Ceiling", ((cx0 + cx1) / 2, E - 0.02, (cz0 + cz1) / 2), (cx1 - cx0 - 0.1, 0.04, cz1 - cz0 - 0.1), "siding", bevel=0)
    # Gable roof, ridge front to back.
    xm, half, H, ov = (cx0 + cx1) / 2, (cx1 - cx0) / 2, 1.05, 0.34
    ridge_y = E + H
    for z in (cz0, cz1):  # gable ends
        verts = [(cx0 - 0.08, E, z - 0.05), (cx1 + 0.08, E, z - 0.05), (xm, ridge_y, z - 0.05),
                 (cx0 - 0.08, E, z + 0.05), (cx1 + 0.08, E, z + 0.05), (xm, ridge_y, z + 0.05)]
        add(f"Gable{z}", verts, [(0, 1, 2), (3, 5, 4), (0, 3, 4, 1), (1, 4, 5, 2), (2, 5, 3, 0)], "siding")
    for side in (-1, 1):
        eave = Vector((xm + side * (half + ov), E - ov * H / half, 0))
        top = Vector((xm, ridge_y, 0))
        d = eave - top
        ang = math.degrees(math.atan2(d.y, d.x))
        normal = Vector((-d.y, d.x, 0)).normalized()
        if normal.y < 0: normal = -normal
        mid = (eave + top) / 2 + normal * 0.05
        zlen = (cz1 - cz0) + 2 * ov
        box(f"Roof{side}", (mid.x, mid.y, (cz0 + cz1) / 2), (d.length + 0.06, 0.1, zlen), "roof_red", rot=(0, 0, ang), bevel=0.02)
        for zz in (cz0 - ov, cz1 + ov):  # rake boards
            box(f"Rake{side}{zz}", (mid.x - normal.x * 0.06, mid.y - normal.y * 0.06, zz), (d.length + 0.06, 0.16, 0.06), "white", rot=(0, 0, ang), bevel=0.01)
        box(f"Fascia{side}", (eave.x, eave.y - 0.06, (cz0 + cz1) / 2), (0.06, 0.18, zlen), "white", bevel=0.01)
        # Courses: lines of overlapping sheets down the slope.
        for k in range(1, 5):
            p = top + d * (k / 5) + normal * 0.105
            box(f"Course{side}{k}", (p.x, p.y, (cz0 + cz1) / 2), (0.05, 0.025, zlen - 0.02), "roof_red_dark", rot=(0, 0, ang), bevel=0)
    box("RidgeCap", (xm, ridge_y + 0.08, (cz0 + cz1) / 2), (0.24, 0.1, (cz1 - cz0) + 2 * ov + 0.04), "roof_red_dark", bevel=0.03)
    # A flag on the front gable.
    tube("FlagPole", [(xm, ridge_y + 0.05, cz1 + ov - 0.1), (xm, ridge_y + 1.25, cz1 + ov - 0.1)], 0.025, "white", seg=8)
    flag = []
    for i in range(9):
        u = i / 8
        flag.append((xm + 0.04 + u * 0.75, ridge_y + 1.18, cz1 + ov - 0.1 + math.sin(u * 5) * 0.06))
        flag.append((xm + 0.04 + u * 0.75, ridge_y + 0.78 + u * 0.04, cz1 + ov - 0.1 + math.sin(u * 5) * 0.06))
    faces = [(2 * i, 2 * i + 1, 2 * i + 3, 2 * i + 2) for i in range(8)]
    ob = add("Flag", flag, faces, "red")
    paint(ob, lambda c, n: "yellow" if c.y < ridge_y + 0.98 else None)
    mod = ob.modifiers.new("solid", 'SOLIDIFY'); mod.thickness = 0.015
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.modifier_apply(modifier=mod.name)
    # Stairs down to the sand: solid treads and risers between stringers, handrails on both sides.
    stairs("Stairs", 0.0, 1.89, (0.04, 2.82), (D, 0.15), 14, "wood_light", "wood", "white", rail="white")
    box("StairPad", (0, 0.03, 3.05), (2.1, 0.08, 0.5), "concrete", bevel=0.02)

@prop
def shack():
    """Sandy's Lost & Found / the old lifeguard shack (shack-local; the doorway faces +z): a plank floor on blocks
    0.83 m up, weathered board walls, a small blue window each side, an orange hip roof, front steps and a red flag."""
    rng = random.Random(11)
    F = 0.834
    x0, x1, z0, z1, E = -1.0875, 1.885, -1.711, 0.9425, 3.6975
    door = (-0.232, 0.58, F, 3.2625)
    cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    # Base: corner blocks and middle blocks, a skirt of boards round it, the floor planks.
    for x in (x0 + 0.1, cx, x1 - 0.1):
        for z in (z0 + 0.1, cz, z1 - 0.1):
            box(f"Block{x}{z}", (x, (F - 0.06) / 2 - 0.05, z), (0.24, F - 0.06 + 0.1, 0.24), "wood_dark", bevel=0.015)
    for side, (axis, plane, out, span) in {"F": ('z', z1, 1, (x0, x1)), "B": ('z', z0, -1, (x0, x1)),
                                         "L": ('x', x0, -1, (z0, z1)), "R": ('x', x1, 1, (z0, z1))}.items():
        sided_wall(f"Skirt{side}", axis, plane - out * 0.06, out, span, (0.02, F - 0.06), [], 0.2, ["wood_dark", "wood_grey"], "wood_dark", rng,
                   core_t=0.04, tilt=0.6)
    z = z1
    i = 0
    while z > z0 + 0.01:
        w = min(0.19, z - z0)
        box(f"Floor{i}", (cx, F - 0.03, z - w / 2), (x1 - x0 + 0.06, 0.06, w - 0.012), rng.choice(["wood", "wood_light", "wood"]), bevel=0.008)
        z -= w; i += 1
    # Walls of weathered boards, a window each side of the hut (none at the back: the shelf stands there).
    tones = ["wood_grey", "wood_grey", "wood", "wood_old"]
    win_y = (F + 1.15, F + 1.8)
    sided_wall("Front", 'z', z1, 1, (x0, x1), (F, E), [door, (-0.88, -0.5, *win_y)], 0.24, tones, "wood_dark", rng, tilt=0.5, inner="wood_old")
    sided_wall("Back", 'z', z0, -1, (x0, x1), (F, E), [], 0.24, tones, "wood_dark", rng, tilt=0.5, inner="wood_old")
    sided_wall("Left", 'x', x0, -1, (z0, z1), (F, E), [(-0.75, -0.1, *win_y)], 0.24, tones, "wood_dark", rng, tilt=0.5, inner="wood_old")
    # East side: Sandy's Lost & Found hatch, open (no glass), wide and tall enough to see her on her stool behind
    # the counter (the scene builder lays the counter plank on its sill at 1.9 m).
    hatch = (-1.05, 0.2, F + 1.07, F + 2.3)
    sided_wall("Right", 'x', x1, 1, (z0, z1), (F, E), [hatch], 0.24, tones, "wood_dark", rng, tilt=0.5, inner="wood_old")
    window("WinFront", 'z', z1, 1, -0.88, -0.5, *win_y, "wood_dark", "glass", 0.18, sill="wood_dark")
    window("WinLeft", 'x', x0, -1, -0.75, -0.1, *win_y, "wood_dark", "glass", 0.18, sill="wood_dark")
    window("WinRight", 'x', x1, 1, hatch[0], hatch[1], hatch[2], hatch[3], "wood_dark", None, 0.18)
    # Its shutter, hinged at the top and propped open outward on two sticks: a little awning over the counter.
    sh_len = hatch[3] - hatch[2] + 0.1
    tilt = math.radians(62)
    hy = hatch[3] + 0.075
    cxs = x1 + 0.1 + math.sin(tilt) * sh_len / 2
    cys = hy - math.cos(tilt) * sh_len / 2
    box("HatchShutter", (cxs, cys, (hatch[0] + hatch[1]) / 2), (0.05, sh_len, hatch[1] - hatch[0] + 0.16), "wood_grey", rot=(0, 0, 62), bevel=0.01)
    for zz in (hatch[0] + 0.06, hatch[1] - 0.06):
        tube(f"HatchProp{zz}", [(x1 + 0.12, hatch[2] - 0.02, zz), (x1 + 0.1 + math.sin(tilt) * sh_len * 0.95, hy - math.cos(tilt) * sh_len * 0.95, zz)], 0.018, "wood_dark", seg=6)
    for x in (x0, x1):
        for z in (z0, z1):
            box(f"Corner{x}{z}", (x, (F + E) / 2 + 0.02, z), (0.18, E - F + 0.04, 0.18), "wood_dark", bevel=0.02)
    for side, (axis, plane, out, span) in {"F": ('z', z1, 1, (x0, x1)), "B": ('z', z0, -1, (x0, x1)),
                                         "L": ('x', x0, -1, (z0, z1)), "R": ('x', x1, 1, (z0, z1))}.items():
        _wall_box(f"TopPlate{side}", axis, plane, (span[0] - 0.09, span[1] + 0.09), (E - 0.12, E + 0.02), 0.2, "wood_dark", out=0.0, bevel=0.012)
    box("Ceiling", (cx, E - 0.02, cz), (x1 - x0 - 0.1, 0.04, z1 - z0 - 0.1), "wood_light", bevel=0)
    tube("LampCord", [(0.406, E - 0.02, -0.377), (0.406, 3.48, -0.377)], 0.008, "dark", seg=5)
    lathe("LampShade", (0.406, 3.47, -0.377), [(0.0, 0.03), (0.05, 0.03), (0.13, -0.05), (0.12, -0.06), (0.0, -0.0)], "dark", seg=14)
    # Hip roof: four sloping faces over an overhang, the ridge along x; courses of shingles, hip and ridge caps.
    ov, slope = 0.42, 0.72
    ex0, ex1, ez0, ez1 = x0 - ov, x1 + ov, z0 - ov, z1 + ov
    ey = E - ov * slope + 0.02
    half_d = (ez1 - ez0) / 2
    half_ridge = max(0.05, ((ex1 - ex0) - (ez1 - ez0)) / 2)
    ry = ey + half_d * slope
    A, Bc, C, Dc = Vector((ex0, ey, ez0)), Vector((ex1, ey, ez0)), Vector((ex1, ey, ez1)), Vector((ex0, ey, ez1))
    R1, R2 = Vector((cx - half_ridge, ry, cz)), Vector((cx + half_ridge, ry, cz))
    roof = add("Roof", [A, Bc, C, Dc, R1, R2], [(3, 2, 5, 4), (1, 0, 4, 5), (0, 3, 4), (2, 1, 5)], "roof_orange")
    mod = roof.modifiers.new("solid", 'SOLIDIFY'); mod.thickness = 0.09; mod.offset = -1
    bpy.context.view_layer.objects.active = roof
    bpy.ops.object.modifier_apply(modifier=mod.name)
    lift = 0.03
    def course(p, q, k):
        tube(f"Course{k}", [p, q], 0.018, "roof_orange_dark", seg=5)
    faces_lines = [((Dc, R1), (C, R2), Vector((0, 1, 1))), ((A, R1), (Bc, R2), Vector((0, 1, -1))),
                   ((A, R1), (Dc, R1), Vector((-1, 1, 0))), ((Bc, R2), (C, R2), Vector((1, 1, 0)))]
    k = 0
    for (p0, p1), (q0, q1), n in faces_lines:
        n = n.normalized()
        for s in range(1, 6):
            t = s / 6
            p = p0 + (p1 - p0) * t + n * lift
            q = q0 + (q1 - q0) * t + n * lift
            if (p - q).length > 0.05: course(p, q, k)
            k += 1
    for e0, e1 in ((A, R1), (Dc, R1), (Bc, R2), (C, R2), (R1, R2)):
        tube(f"Cap{k}", [e0 + Vector((0, 0.06, 0)), e1 + Vector((0, 0.06, 0))], 0.05, "wood_dark", seg=6); k += 1
    for e0, e1 in ((A, Bc), (Bc, C), (C, Dc), (Dc, A)):
        m = (e0 + e1) / 2
        size = ((e1 - e0).length + 0.06, 0.14, 0.05) if abs(e1.x - e0.x) > 0.01 else (0.05, 0.14, (e1 - e0).length + 0.06)
        box(f"Fascia{k}", (m.x, ey - 0.08, m.z), size, "wood_dark", bevel=0.01); k += 1
    # Front steps up to the doorway.
    stairs("Steps", 0.1015, 1.32, (0.0, 2.494), (F, z1 + 0.03), 4, "wood", "wood_dark", "wood_dark")
    # The red flag on its pole, front left.
    px, pz = x0 - 0.6, z1 + 0.55
    tube("Pole", [(px, -0.2, pz), (px, 5.3, pz)], 0.045, "wood_dark", seg=8)
    lathe("PoleTop", (px, 5.32, pz), arc_profile(0.06, -0.05, 0.05, 5), "wood_dark", seg=8)
    flag = []
    for i in range(10):
        u = i / 9
        wave = math.sin(u * 6.0) * 0.08 * u
        flag.append((px + 0.05 + u * 1.0, 5.2 - u * 0.05, pz + wave))
        flag.append((px + 0.05 + u * 1.0, 4.55 + u * 0.05, pz + wave))
    ob = add("Flag", flag, [(2 * i, 2 * i + 1, 2 * i + 3, 2 * i + 2) for i in range(9)], "red")
    mod = ob.modifiers.new("solid", 'SOLIDIFY'); mod.thickness = 0.015
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.modifier_apply(modifier=mod.name)

# ---------------------------------------------------------------------------------------- run

bpy.ops.wm.read_factory_settings(use_empty=True)
os.makedirs(a.out, exist_ok=True)
only = [n for n in a.only.split(",") if n]
FLAT = {"rock", "crate", "dock"}
VIEWS = {"watch_tower": (0.9, 0.55, 1.0), "shack": (0.9, 0.55, 1.0), "dock": (1.0, 0.9, 1.0), "drill_board": (0.5, 0.25, -1.0), "roof_sign": (0.5, 0.25, -1.0), "lost_box": (0.6, 1.0, -1.0), "towel": (0.3, 1.0, -0.6), "sunglasses": (0.7, 0.7, -1.0), "bell": (0.9, 0.2, -1.0), "sign_frame": (0.5, 0.25, -1.0), "radio": (-0.6, 0.5, 1.0),
         "first_aid_kit": (-0.6, 0.6, 1.0), "defibrillator": (0.6, 1.0, -1.0), "phone": (0.5, 1.2, -0.8), "watch": (0.6, 1.2, -0.7), "wallet": (0.6, 1.0, -1.0),
         "beach_hut": (0.8, 0.5, 1.0), "basketball_hoop": (0.9, 0.4, 1.0), "basketball": (0.6, 0.6, 1.0),
         "trampoline": (0.9, 0.6, 1.0), "human_cannon": (1.0, 0.5, 0.6), "cannon_barrel": (1.0, 0.5, 0.6), "cannon_wheel": (1.0, 0.2, 0.2), "diving_board": (1.0, 0.5, 0.8),
         "zipline_post": (1.0, 0.3, 1.0), "zipline_handle": (1.0, 0.3, 1.0),
         "banana_boat": (1.0, 0.5, 0.7), "parrot": (0.8, 0.35, 1.0), "parrot_wing": (1.0, 0.3, 0.3),
         "beach_toilets": (0.8, 0.45, 1.0), "beach_bar": (0.8, 0.45, 1.0)}
for name, build in PROPS.items():
    if only and name not in only: continue
    for ob in [o for o in bpy.data.objects if o.type == 'MESH']: bpy.data.objects.remove(ob)
    parts = []
    build()
    ob = finish(name, flat=name in FLAT)
    export(ob, os.path.join(a.out, name + ".glb"))
    if a.preview: preview(ob, f"{a.preview}_{name}.png", VIEWS.get(name, (0.9, 0.75, -1.0)))
