"""Clean massing for the polished Tripo hotel (used by build_realistic_hotel.py, after hotel_exterior).

The AI facade kept jagged slivers, torn corners and holes wherever two walls meet (the wing corners, the balcony bays'
side walls, the base storey's band, the porte-cochere's back): straightening could not fix those. This replaces the
whole AI facade with clean axis-aligned blocks of the same shape:

- Grid lines go through every real wall plane, roof and soffit of the AI model (and every window's wall plane).
- Each grid cell is solid when most of it is inside the AI model (rays up through the closed source mesh), so thin
  things (balcony slabs, ledges, parapets, rails: all rebuilt cleanly by hotel_windows / hotel_exterior) drop out.
- The surface between solid and empty cells, merged into flat panels, is the new facade; each window is a clean
  recess in it behind the rebuilt frame and glass. Panels keep the AI model's colour (cream render, sandstone, white,
  teal, roof) by majority.
Blender coordinates: x across the front, -y out of the front facade, z up (metres, ground = 0).
"""
import json
import math
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

from hotel_exterior import CANOPY_TOP, CANOPY_X, CANOPY_Y

ROOT = Path(__file__).resolve().parents[2]
MERGE = .12              # wall planes closer than this are one plane
MIN_PLANE = 6.0          # m^2 of facing faces for a plane to count
SAMPLES = 3              # occupancy samples per cell along each axis
THIN = (.35, .35, .6)    # solid thinner than this along x, y, z is a slab, ledge or parapet: rebuilt elsewhere
CARVE_IN = .14           # window recess depth behind the wall face (the glass is 6 cm in)
WINDOWS = ROOT / 'Logs/hotel-window-repairs.json'


def source_bvh(path, height):
    """The closed AI source mesh, sized and centred like build_realistic_hotel.load_source, as a BVH. Everything the
    import brought in is removed again."""
    before = {k: set(getattr(bpy.data, k)) for k in ('objects', 'meshes', 'materials', 'images')}
    bpy.ops.import_scene.gltf(filepath=str(path))
    src = next(o for o in bpy.data.objects if o not in before['objects'] and o.type == 'MESH')
    me = src.data
    co = np.array([src.matrix_world @ v.co for v in me.vertices])
    lo, hi = co.min(0), co.max(0)
    co = (co - np.array([(lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, lo[2]])) * (height / (hi[2] - lo[2]))
    me.calc_loop_triangles()
    tris = np.empty(len(me.loop_triangles) * 3, dtype=np.int64); me.loop_triangles.foreach_get('vertices', tris)
    bvh = BVHTree.FromPolygons(co.tolist(), tris.reshape(-1, 3).tolist())
    for k, old in before.items():
        coll = getattr(bpy.data, k)
        for item in [i for i in coll if i not in old]: coll.remove(item)
    return bvh


def face_arrays(me):
    n = len(me.polygons)
    c = np.empty(n * 3); me.polygons.foreach_get('center', c)
    nm = np.empty(n * 3); me.polygons.foreach_get('normal', nm)
    ar = np.empty(n); me.polygons.foreach_get('area', ar)
    mt = np.empty(n, dtype=np.int64); me.polygons.foreach_get('material_index', mt)
    return c.reshape(-1, 3), nm.reshape(-1, 3), ar, mt


def plane_lines(c, nm, ar, axis, extra=()):
    """Positions along axis of every big plane facing +-axis (area-weighted, merged within MERGE)."""
    facing = np.abs(nm[:, axis]) > .97
    v = c[facing, axis]; w = ar[facing]
    order = np.argsort(v); v = v[order]; w = w[order]
    grp = np.concatenate([[0], np.cumsum(np.diff(v) > .03)])
    sums = np.bincount(grp, w * v); ws = np.bincount(grp, w)
    pos = sums / np.maximum(ws, 1e-9)
    lines = [(p, a) for p, a in zip(pos, ws) if a >= MIN_PLANE]
    lines += [(p, 1e9) for p in extra]                 # window planes win
    lines.sort()
    merged = []
    for p, a in lines:
        if merged and p - merged[-1][0] < MERGE:
            if a > merged[-1][1]: merged[-1] = (p, a)
        else: merged.append((p, a))
    return np.array([p for p, _ in merged])


def column_intervals(bvh, x, y, z0, z1):
    """Inside intervals along z of a vertical line through the closed mesh (parity of the hits)."""
    hits = []
    o = Vector((x, y, z0)); d = Vector((0, 0, 1))
    while True:
        loc, normal, i, dist = bvh.ray_cast(o, d, z1 - o.z)
        if loc is None: break
        if not hits or loc.z - hits[-1] > 1e-4: hits.append(loc.z)
        o = Vector((x, y, loc.z + 1e-4))
    return [(hits[k], hits[k + 1]) for k in range(0, len(hits) - 1, 2)]


def occupancy(bvh, xs, ys, zs, log):
    """Solid share of every grid cell, sampled on SAMPLES^2 columns per (x, y) cell."""
    nx, ny, nz = len(xs) - 1, len(ys) - 1, len(zs) - 1
    share = np.zeros((nx, ny, nz))
    f = (np.arange(SAMPLES) + .5) / SAMPLES
    zlo, zhi = zs[0] - 1, zs[-1] + 1
    for i in range(nx):
        for j in range(ny):
            acc = np.zeros(nz)
            for fx in f:
                for fy in f:
                    x = xs[i] + (xs[i + 1] - xs[i]) * fx; y = ys[j] + (ys[j + 1] - ys[j]) * fy
                    for a, b in column_intervals(bvh, x, y, zlo, zhi):
                        lo = np.clip(a, zs[:-1], zs[1:]); hi = np.clip(b, zs[:-1], zs[1:])
                        acc += (hi - lo) / (zs[1:] - zs[:-1])
            share[i, j] = acc / (SAMPLES * SAMPLES)
    log('massing grid', nx, ny, nz, 'solid cells', int((share > .5).sum()))
    return share


def drop_thin(solid, xs, ys, zs, limits, log):
    """Solid runs thinner than limits[axis] along an axis go (balcony slabs, ledges, parapets: rebuilt elsewhere)."""
    sizes = (np.diff(xs), np.diff(ys), np.diff(zs))
    solid = solid.copy()
    for _ in range(3):
        removed = 0
        for axis in range(3):
            moved = np.moveaxis(solid, axis, -1)          # view: runs along the last axis
            size = sizes[axis]
            for idx in np.ndindex(moved.shape[:-1]):
                line = moved[idx]
                k = 0; n = len(line)
                while k < n:
                    if not line[k]: k += 1; continue
                    e = k
                    while e < n and line[e]: e += 1
                    if size[k:e].sum() < limits[axis]:
                        line[k:e] = False; removed += e - k
                    k = e
        log('thin cells removed', removed)
        if not removed: break
    return solid


def merge_lines(values, tol):
    values = np.sort(np.asarray(values, dtype=float))
    out = [values[0]]
    for v in values[1:]:
        if v - out[-1] >= tol: out.append(v)
    return np.array(out)


def refine(lines_c, lines_f):
    """Index of the coarse cell holding each fine cell's centre, per axis."""
    return [np.clip(np.searchsorted(c, (f[:-1] + f[1:]) / 2) - 1, 0, len(c) - 2) for c, f in zip(lines_c, lines_f)]


def build_surface(xs, ys, zs, solid, cls_at):
    """Quads between solid and empty cells, outward-facing, with a material per quad."""
    verts, faces, mats = [], [], []
    vid = {}

    def v(i, j, k):
        key = (i, j, k)
        if key not in vid:
            vid[key] = len(verts); verts.append((xs[i], ys[j], zs[k]))
        return vid[key]

    nx, ny, nz = solid.shape
    pad = np.zeros((nx + 2, ny + 2, nz + 2), dtype=np.int8); pad[1:-1, 1:-1, 1:-1] = solid
    for axis in range(3):
        o1, o2 = [ax for ax in range(3) if ax != axis]
        step = np.diff(pad, axis=axis)                   # index t: padded cells t -> t+1, i.e. grid line t
        for sgn in (1, -1):                              # +1: solid on the + side (the face looks to -axis)
            normal = np.zeros(3); normal[axis] = -sgn
            for p in np.argwhere(step == sgn):
                if p[o1] in (0, pad.shape[o1] - 1) or p[o2] in (0, pad.shape[o2] - 1): continue
                q = []
                for d1, d2 in ((0, 0), (1, 0), (1, 1), (0, 1)):
                    g = [0, 0, 0]; g[axis] = p[axis]; g[o1] = p[o1] - 1 + d1; g[o2] = p[o2] - 1 + d2
                    q.append(v(*g))
                cross = np.cross(np.subtract(verts[q[1]], verts[q[0]]), np.subtract(verts[q[3]], verts[q[0]]))
                if np.dot(cross, normal) < 0: q.reverse()
                faces.append(q)
                mats.append(cls_at(np.mean([verts[k] for k in q], axis=0), normal))
    return verts, faces, mats


def wall_faces(hotel):
    """The straightened AI model's faces (centre, normal, area), taken before the window and exterior rebuilds delete
    most of them: the massing's wall planes come from these."""
    c, nm, ar, mt = face_arrays(hotel.data)
    return c, nm, ar


def clean_massing(hotel, faces, source, height, names, log):
    """Replaces the hotel's AI facade mesh with the clean massing (same materials, same object)."""
    idx = {n: i for i, n in enumerate(names)}
    c, nm, ar = faces
    windows = json.loads(WINDOWS.read_text(encoding='utf-8'))['windows']
    wall_planes = {0: [], 1: []}
    for a, sg, sglass, swall, u0, u1, z0, z1, teal in windows: wall_planes[int(a)].append(swall * sg)
    coarse = (plane_lines(c, nm, ar, 0, wall_planes[0]), plane_lines(c, nm, ar, 1, wall_planes[1]), plane_lines(c, nm, ar, 2))
    xs, ys, zs = coarse
    solid = occupancy(source_bvh(source, height), xs, ys, zs, log) > .5
    # The porte-cochere is rebuilt whole (hotel_exterior.rebuild_canopy): nothing of the AI one stays.
    cx, cy, cz = ((l[:-1] + l[1:]) / 2 for l in coarse)
    solid &= ~((cx[:, None, None] > CANOPY_X[0] - .3) & (cx[:, None, None] < CANOPY_X[1] + .3) &
               (cy[None, :, None] < CANOPY_Y[1]) & (cz[None, None, :] < CANOPY_TOP + .4))
    solid = drop_thin(solid, xs, ys, zs, THIN, log)

    # Fine grid: every window's opening and recess back get their own lines, so it is cut exactly.
    extra = ([xs], [ys], [zs])
    for a, sg, sglass, swall, u0, u1, z0, z1, teal in windows:
        a = int(a); u = 1 - a
        extra[a].append([(swall - CARVE_IN) * sg]); extra[u].append([u0, u1]); extra[2].append([z0, z1])
    fine = tuple(merge_lines(np.concatenate(e), .005) for e in extra)
    ix, iy, iz = refine(coarse, fine)
    grid = solid[np.ix_(ix, iy, iz)]
    centre = [(l[:-1] + l[1:]) / 2 for l in fine]
    carved = 0
    for a, sg, sglass, swall, u0, u1, z0, z1, teal in windows:
        a = int(a); u = 1 - a
        d0, d1 = sorted(((swall - CARVE_IN) * sg, (swall + .05) * sg))
        sel = [None, None, None]
        sel[a] = (centre[a] > d0) & (centre[a] < d1)
        sel[u] = (centre[u] > u0) & (centre[u] < u1)
        sel[2] = (centre[2] > z0) & (centre[2] < z1)
        box = np.ix_(*(np.nonzero(s)[0] for s in sel))
        carved += int(grid[box].sum()); grid[box] = False
    log('window recesses', len(windows), 'cells carved', carved)

    def cls_at(p, n):
        return idx['wall'] if abs(n[2]) < .5 else (idx['roof'] if n[2] > 0 else idx['white'])
    verts, faces, mats = build_surface(*fine, grid, cls_at)
    mesh = bpy.data.meshes.new('HotelMassing')
    mesh.from_pydata(verts, [], faces)
    for m in hotel.data.materials: mesh.materials.append(m)
    mesh.polygons.foreach_set('material_index', np.array(mats, dtype=np.int32))
    # One flat panel per wall stretch (the grid cut it into cells), then triangles.
    bm = bmesh.new(); bm.from_mesh(mesh)
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(1), use_dissolve_boundaries=False,
                             verts=bm.verts, edges=bm.edges, delimit={'MATERIAL'})
    bmesh.ops.triangulate(bm, faces=bm.faces, quad_method='BEAUTY', ngon_method='BEAUTY')
    bm.to_mesh(mesh); bm.free(); mesh.update()
    old = hotel.data
    hotel.data = mesh
    bpy.data.meshes.remove(old)
    log('clean massing: grid', [len(l) - 1 for l in fine], 'triangles', len(mesh.polygons))
