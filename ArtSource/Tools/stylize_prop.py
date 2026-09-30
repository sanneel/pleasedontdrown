# Blender batch script: give a prepared Meshy prop the game's look (chunky low poly, flat faceted shading, a few
# flat paint colours instead of a photo-real texture). Run it on the output of prepare_prop.py.
#   blender -b --factory-startup -P stylize_prop.py -- <in.glb> <out.glb> --palette lifeguard [--tris 5000]
#       [--smooth 2] [--preview <prefix>]
# Each face is painted with the palette colour nearest to its texture colour (sampled at the corners and the
# centre, median), then speckles are voted away by the neighbours (--smooth passes). The texture is replaced by a
# tiny palette atlas (one 8x8 cell per colour, nearest filtering) and every face's UVs point at its cell, so the
# model stays one mesh with one plain material that any lit shader shows the same way.
import sys, os, math, argparse, colorsys
import bpy, bmesh, mathutils, mathutils.bvhtree, mathutils.geometry

argv = sys.argv[sys.argv.index("--") + 1:]
ap = argparse.ArgumentParser()
ap.add_argument("src"); ap.add_argument("dst")
ap.add_argument("--palette", default="lifeguard")
ap.add_argument("--tris", type=int, default=5000)
ap.add_argument("--smooth", type=int, default=3)
ap.add_argument("--min-area", type=float, default=0.004)
ap.add_argument("--voxel", type=float, default=0.012, help="voxel remesh size in metres (0 = keep the Meshy topology)")
ap.add_argument("--decal", default="", help="PNG lettering (make_decals.py) projected onto both flanks")
ap.add_argument("--decal-on", default="red", help="palette colour the lettering sits on (only those faces get it)")
ap.add_argument("--decal-band", default="0.05,0.29,-1.05,0.65", help="z0,z1,y0,y1 in Blender axes (nose -Y)")
ap.add_argument("--iron",type=float, default=0.8, help="Laplacian smoothing strength after the remesh")
ap.add_argument("--tones", default="", help="paint by lightness rank instead of by colour: comma fractions of the "
                "faces (darkest first) for each palette colour, e.g. 0.6,0.3,0.1 (for near-monochrome models like guns)")
ap.add_argument("--preview", default="")
a = ap.parse_args(argv)

# name, paint colour (sRGB), and the "source look" that maps onto it (sRGB): several sources may share a paint.
PALETTES = {
    "lifeguard": [
        ("red",    (0.86, 0.16, 0.13), [(0.85, 0.35, 0.30), (0.95, 0.45, 0.40), (0.70, 0.25, 0.22), (0.90, 0.55, 0.50)]),
        ("white",  (0.94, 0.92, 0.86), [(0.93, 0.93, 0.93), (0.80, 0.80, 0.82), (0.98, 0.98, 0.98)]),
        ("grey",   (0.46, 0.48, 0.52), [(0.55, 0.56, 0.58), (0.45, 0.45, 0.47)]),
        ("rubber", (0.15, 0.16, 0.18), [(0.20, 0.20, 0.22), (0.30, 0.30, 0.32)]),
        ("black",  (0.06, 0.06, 0.07), [(0.05, 0.05, 0.06), (0.11, 0.11, 0.12)]),
    ],
    # Guns (used with --tones, darkest first): polymer furniture, blued metal, worn steel edges.
    "gun": [
        ("polymer", (0.21, 0.22, 0.24), []),
        ("blued",   (0.38, 0.40, 0.44), []),
        ("steel",   (0.58, 0.60, 0.63), []),
    ],
}
palette = PALETTES[a.palette]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=a.src)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in meshes: o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1: bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active

# The base colour texture.
image = None
for m in obj.data.materials:
    if m is None or not m.use_nodes: continue
    for n in m.node_tree.nodes:
        if n.type == 'BSDF_PRINCIPLED':
            links = n.inputs["Base Color"].links
            if links and links[0].from_node.type == 'TEX_IMAGE':
                image = links[0].from_node.image
if image is None: raise SystemExit("no base colour texture")
W, H = image.size
pixels = list(image.pixels[:])  # linear floats RGBA
print(f"[stylize] texture {image.name} {W}x{H}")

def to_srgb(c):
    return c * 12.92 if c <= 0.0031308 else 1.055 * (c ** (1 / 2.4)) - 0.055

def sample(uv):
    x = min(W - 1, max(0, int((uv.x % 1.0) * W)))
    y = min(H - 1, max(0, int((uv.y % 1.0) * H)))
    i = (y * W + x) * 4
    # Blender image pixels hold what the file stored; colour data textures are sRGB.
    if image.colorspace_settings.name == 'sRGB':
        return mathutils.Vector(pixels[i:i + 3])
    return mathutils.Vector([to_srgb(c) for c in pixels[i:i + 3]])

def classify(c):
    # Hue matters for the red; lightness sorts the neutrals.
    h, l, s = colorsys.rgb_to_hls(*c)
    best, bestd = 0, 1e9
    for k, (_, _, sources) in enumerate(palette):
        for src in sources:
            hs, ls, ss = colorsys.rgb_to_hls(*src)
            dh = min(abs(h - hs), 1 - abs(h - hs)) * (min(s, ss) * 2.0)
            d = dh * dh * 4 + (l - ls) ** 2 * 2 + (s - ss) ** 2
            if d < bestd: best, bestd = k, d
    return best

def tris(o): return sum(len(p.vertices) - 2 for p in o.data.polygons)

# The textured source stays as a colour lookup (triangulated, in a BVH).
src = bmesh.new(); src.from_mesh(obj.data)
bmesh.ops.triangulate(src, faces=src.faces[:])
src.faces.ensure_lookup_table()
src_uv = src.loops.layers.uv.active
bvh = mathutils.bvhtree.BVHTree.FromBMesh(src)

def colour_at(p):
    loc, _, idx, _ = bvh.find_nearest(p)
    if idx is None: return None
    f = src.faces[idx]
    a_, b_, c_ = (l.vert.co for l in f.loops)
    w = mathutils.geometry.barycentric_transform(loc, a_, b_, c_, mathutils.Vector((1, 0, 0)), mathutils.Vector((0, 1, 0)), mathutils.Vector((0, 0, 1)))
    uvs = [l[src_uv].uv for l in f.loops]
    return sample(uvs[0] * w.x + uvs[1] * w.y + uvs[2] * w.z)

# The shape: Meshy surfaces are lumpy, so rebuild them (voxel remesh), iron the lumps out, then bring the
# count down; the low-poly facets come out as big clean planes instead of crumpled foil.
if a.voxel > 0:
    obj.data.remesh_voxel_size = a.voxel
    obj.data.use_remesh_fix_poles = True
    bpy.ops.object.voxel_remesh()
    sm = obj.modifiers.new("Smooth", 'LAPLACIANSMOOTH')
    sm.lambda_factor = a.iron; sm.iterations = 4; sm.use_volume_preserve = True
    bpy.ops.object.modifier_apply(modifier=sm.name)
    mod = obj.modifiers.new("Pre", 'DECIMATE')
    mod.decimate_type = 'COLLAPSE'; mod.ratio = min(1.0, a.tris * 5 / max(1, tris(obj))); mod.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=mod.name)
    print(f"[stylize] remeshed at {a.voxel} m -> {tris(obj)} triangles")
    if not obj.data.uv_layers: obj.data.uv_layers.new(name="UV")

bm = bmesh.new(); bm.from_mesh(obj.data)
bm.faces.ensure_lookup_table()
uv = bm.loops.layers.uv.active
labels = []
lights = []
for f in bm.faces:
    centre = f.calc_center_median()
    pts = [centre] + [l.vert.co.lerp(centre, 0.5) for l in f.loops]
    cols = sorted((c for c in (colour_at(p) for p in pts) if c is not None), key=lambda c: c.x + c.y + c.z)
    labels.append(classify(cols[len(cols) // 2]) if cols else 0)
    lights.append(sum(cols[len(cols) // 2]) if cols else 0.0)
if a.tones:
    # Lightness rank (area weighted): the darkest share of the surface gets the first colour, and so on.
    shares = [float(v) for v in a.tones.split(",")]
    order = sorted(range(len(lights)), key=lambda i: lights[i])
    total = sum(f.calc_area() for f in bm.faces)
    k, acc, edge = 0, 0.0, shares[0]
    for i in order:
        while k < len(shares) - 1 and acc >= edge * total:
            k += 1; edge += shares[k]
        labels[i] = k
        acc += bm.faces[i].calc_area()

# Neighbour vote: a face whose neighbours mostly agree takes their colour (area weighted).
areas = [f.calc_area() for f in bm.faces]
for _ in range(a.smooth):
    new = labels[:]
    for f in bm.faces:
        votes = {labels[f.index]: areas[f.index]}
        for e in f.edges:
            for g in e.link_faces:
                if g.index != f.index: votes[labels[g.index]] = votes.get(labels[g.index], 0) + areas[g.index]
        new[f.index] = max(votes, key=votes.get)
    labels = new

# Paint islands smaller than --min-area (mÂ²) melt into the colour they border most.
def islands():
    seen = [False] * len(labels); out = []
    for f in bm.faces:
        if seen[f.index]: continue
        stack, members = [f], []
        seen[f.index] = True
        while stack:
            g = stack.pop(); members.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if not seen[h.index] and labels[h.index] == labels[g.index]:
                        seen[h.index] = True; stack.append(h)
        out.append(members)
    return out
for _ in range(4):
    changed = 0
    for members in sorted(islands(), key=lambda m: sum(areas[g.index] for g in m)):
        if sum(areas[g.index] for g in members) >= a.min_area: continue
        own = labels[members[0].index]
        border = {}
        for g in members:
            for e in g.edges:
                for h in e.link_faces:
                    if labels[h.index] != own: border[labels[h.index]] = border.get(labels[h.index], 0) + e.calc_length()
        if border:
            top = max(border, key=border.get)
            for g in members: labels[g.index] = top
            changed += 1
    if changed == 0: break
    print(f"[stylize] melted {changed} small paint islands")

# Palette atlas: one cell per colour along the bottom row; an optional decal strip (lettering painted over the
# --decal-on colour) above it. Cells are big enough that mipmaps don't bleed between colours up close.
cell = 32
names = [p[0] for p in palette]
decal = None
if a.decal:
    decal = bpy.data.images.load(os.path.abspath(a.decal))
    DW, DH = decal.size
    AW, AH = max(DW, cell * len(palette)), DH + cell
else:
    AW, AH = cell * len(palette), cell
atlas = bpy.data.images.new("Palette", width=AW, height=AH, alpha=False)
atlas.colorspace_settings.name = 'sRGB'
px = [0.0] * (AW * AH * 4)
for k, (_, paint_rgb, _) in enumerate(palette):
    for y in range(cell):
        for x in range(k * cell, (k + 1) * cell):
            i = (y * AW + x) * 4
            px[i:i + 4] = [*paint_rgb, 1.0]
if decal is not None:
    under = palette[names.index(a.decal_on)][1]
    dpx = list(decal.pixels[:])
    for y in range(DH):
        for x in range(AW):
            j = (y * DW + min(x, DW - 1)) * 4
            t = dpx[j + 3] if x < DW else 0.0
            i = ((y + cell) * AW + x) * 4
            px[i:i + 4] = [under[c] * (1 - t) + dpx[j + c] * t for c in range(3)] + [1.0]
atlas.pixels = px
atlas.pack()
cell_v = (cell * 0.5) / AH
def cell_uv(k): return (((k + 0.5) * cell) / AW, cell_v)
paint = bm.faces.layers.int.new("paint")
for f in bm.faces:
    f[paint] = labels[f.index]
    for l in f.loops: l[uv].uv = cell_uv(labels[f.index])
bm.to_mesh(obj.data); bm.free()

# Decimate to the house triangle budget now: the paint borders are UV seams, which the collapse keeps.
before = tris(obj)
if before > a.tris:
    mod = obj.modifiers.new("Decimate", 'DECIMATE')
    mod.decimate_type = 'COLLAPSE'; mod.ratio = a.tris / before; mod.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=mod.name)
print(f"[stylize] {before} -> {tris(obj)} triangles")

bm = bmesh.new(); bm.from_mesh(obj.data)
uv = bm.loops.layers.uv.active
paint = bm.faces.layers.int.get("paint")
counts = {}
band = [float(v) for v in a.decal_band.split(",")] if decal is not None else None
decal_faces = 0
for f in bm.faces:
    k = f[paint]
    counts[palette[k][0]] = counts.get(palette[k][0], 0) + 1
    f.smooth = False
    c = f.calc_center_median()
    painted = names[k] not in ("black", "rubber")  # stray light patches inside the strip become --decal-on
    if band and painted and abs(f.normal.x) > 0.3 and band[0] <= c.z <= band[1] and band[2] <= c.y <= band[3]:
        # Side lettering: projected flat from the side, left to right (never mirrored) on both flanks.
        side = 1.0 if f.normal.x > 0 else -1.0
        for l in f.loops:
            p = l.vert.co
            s = (p.y - band[2]) / (band[3] - band[2])
            u = s if side > 0 else 1.0 - s
            t = (p.z - band[0]) / (band[1] - band[0])
            l[uv].uv = (min(1.0, max(0.0, u)) * (DW / AW), (cell + min(1.0, max(0.0, t)) * (DH - 1)) / AH)
        decal_faces += 1
        continue
    for l in f.loops: l[uv].uv = cell_uv(k)
if band: print(f"[stylize] decal on {decal_faces} faces")
bm.faces.layers.int.remove(paint)
print(f"[stylize] faces per colour {counts}")
bm.to_mesh(obj.data); bm.free()
if hasattr(obj.data, "shade_flat"): obj.data.shade_flat()

mat = bpy.data.materials.new("Paint"); mat.use_nodes = True
nodes = mat.node_tree.nodes
bsdf = nodes["Principled BSDF"]
bsdf.inputs["Roughness"].default_value = 0.85
bsdf.inputs["Metallic"].default_value = 0.0
tex = nodes.new("ShaderNodeTexImage"); tex.image = atlas; tex.interpolation = 'Linear' if decal is not None else 'Closest'
mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
obj.data.materials.clear(); obj.data.materials.append(mat)
for im in list(bpy.data.images):
    if im != atlas and im.users == 0: bpy.data.images.remove(im)

bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True)
bpy.ops.export_scene.gltf(filepath=a.dst, export_format='GLB', export_image_format='AUTO', use_selection=True, export_apply=True)

allv = [obj.matrix_world @ v.co for v in obj.data.vertices]
size = [max(v[i] for v in allv) - min(v[i] for v in allv) for i in range(3)]
print(f"[stylize] size width {size[0]:.3f} length {size[1]:.3f} height {size[2]:.3f}")

if a.preview:
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 800; scene.render.resolution_y = 520
    world = bpy.data.worlds.new("w"); scene.world = world; world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.75, 0.9, 1)
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); scene.collection.objects.link(sun)
    sun.data.energy = 3.5; sun.rotation_euler = (0.8, 0.2, 0.6)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scene.collection.objects.link(cam)
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = max(size) * 1.15
    scene.camera = cam
    centre = mathutils.Vector((0, 0, size[2] / 2))
    for name, d in [("side", (1, 0, 0)), ("front", (0, -1, 0.05)), ("top", (0.001, 0, 1)), ("persp", (1, -1, 0.6)), ("back", (-1, 1, 0.5))]:
        v = mathutils.Vector(d).normalized()
        cam.location = centre + v * 20
        cam.rotation_euler = (-v).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = f"{a.preview}_{name}.png"
        bpy.ops.render.render(write_still=True)
