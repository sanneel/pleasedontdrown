"""Depth maps of the polished hotel from its four sides and the top (Logs/hotelfix/depth_*.png + .npy).
Hotel-local Unity metres: x = blender x, y = blender z, z = -blender y - 4.
blender -b ArtSource/Resort/hotel_realistic.blend -P ArtSource/Tools/hotel_depth_maps.py
"""
from pathlib import Path
import bpy, numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(bpy.data.filepath).resolve().parents[2]
OUT = ROOT / 'Logs/hotelfix'; OUT.mkdir(parents=True, exist_ok=True)
dg = bpy.context.evaluated_depsgraph_get()
objs = [o for o in bpy.data.objects if o.type == 'MESH' and o.name in ('HotelLOD0', 'HotelWindows0')]
print('objects', [o.name for o in bpy.data.objects])
verts, polys = [], []
for o in objs:
    m = o.evaluated_get(dg).to_mesh(); base = len(verts)
    verts += [o.matrix_world @ v.co for v in m.vertices]
    polys += [[base + i for i in p.vertices] for p in m.polygons]
bvh = BVHTree.FromPolygons(verts, polys)
lo = Vector((min(v.x for v in verts), min(v.y for v in verts), min(v.z for v in verts)))
hi = Vector((max(v.x for v in verts), max(v.y for v in verts), max(v.z for v in verts)))
print('bounds', lo, hi)
step = .25
def scan(name, origin_fn, direction, us, vs):
    d = np.full((len(vs), len(us)), np.nan, np.float32)
    for j, v in enumerate(vs):
        for i, u in enumerate(us):
            hit = bvh.ray_cast(origin_fn(u, v), direction, 200)
            if hit[0] is not None: d[j, i] = hit[3]
    np.save(OUT / f'depth_{name}.npy', d)
    img = bpy.data.images.new(name, len(us), len(vs))
    finite = d[np.isfinite(d)]; a, b = finite.min(), np.percentile(finite, 99)
    n = np.where(np.isfinite(d), 1 - np.clip((d - a) / max(b - a, 1e-3), 0, 1), 0)
    px = np.stack([n, n, np.where(np.isfinite(d), n, 1) * 0 + n, np.ones_like(n)], -1)
    px[~np.isfinite(d)] = (1, 0, 0, 1)
    img.pixels = px.ravel().tolist(); img.filepath_raw = str(OUT / f'depth_{name}.png'); img.file_format = 'PNG'; img.save()
    print(name, 'min', a, 'p99', b)
xs = np.arange(lo.x - 1, hi.x + 1, step); ys = np.arange(lo.y - 1, hi.y + 1, step); zs = np.arange(0.1, hi.z + 1, step)
# Blender -y is Unity +z (front); back of the building is +y in Blender.
scan('back', lambda u, v: Vector((u, hi.y + 5, v)), Vector((0, -1, 0)), xs, zs[::-1])
scan('front', lambda u, v: Vector((u, lo.y - 5, v)), Vector((0, 1, 0)), xs[::-1], zs[::-1])
scan('west', lambda u, v: Vector((lo.x - 5, u, v)), Vector((1, 0, 0)), ys, zs[::-1])
scan('east', lambda u, v: Vector((hi.x + 5, u, v)), Vector((-1, 0, 0)), ys[::-1], zs[::-1])
scan('top', lambda u, v: Vector((u, v, hi.z + 5)), Vector((0, 0, -1)), xs, ys)
(OUT / 'bounds.txt').write_text(f'{tuple(lo)} {tuple(hi)} step {step}\n')
