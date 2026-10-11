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
THIN = (.35, .35, 1.0)   # solid thinner than this along x, y, z is a slab, ledge or parapet: rebuilt elsewhere
BALCONY_DEPTH = .75      # a thin slab standing out at least this far from its wall is a balcony (else a ledge)
# Made solid (x0, x1, y0, y1, z0, z1; Blender coordinates, -y is the front): the recess right of the canopy, first floor.
FILL_WALL = [(6.0, 8.6, -3.6, -1.6, 4.4, 8.1)]
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


def log_none(*a):
    pass


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


def find_slabs(slab, kept, xs, ys, zs):
    """Balcony slabs: pieces of the thin-in-height solid (6-connected cells) that stand out of the building's wall.
    Each: (axis, sign, top z, wall depth, front depth, u0, u1), depths signed outward along the axis."""
    lines = (xs, ys, zs)
    seen = np.zeros_like(slab)
    found = []
    for start in map(tuple, np.argwhere(slab)):
        if seen[start]: continue
        stack, cells = [start], []
        seen[start] = True
        while stack:
            c = stack.pop(); cells.append(c)
            for ax in range(3):
                for s in (-1, 1):
                    n = list(c); n[ax] += s; n = tuple(n)
                    if 0 <= n[ax] < slab.shape[ax] and slab[n] and not seen[n]:
                        seen[n] = True; stack.append(n)
        # Which side the building is on: the most contact with what's left of it.
        contact = {}
        for c in cells:
            for ax in (0, 1):
                for s in (-1, 1):
                    n = list(c); n[ax] += s; n = tuple(n)
                    if 0 <= n[ax] < kept.shape[ax] and kept[n]:
                        o = 1 - ax
                        area = (lines[o][c[o] + 1] - lines[o][c[o]]) * (zs[c[2] + 1] - zs[c[2]])
                        contact[(ax, s)] = contact.get((ax, s), 0.0) + area
        if not contact: continue
        (a, s) = max(contact, key=contact.get)
        cells = np.array(cells)
        lo = [lines[ax][cells[:, ax].min()] for ax in range(3)]
        hi = [lines[ax][cells[:, ax].max() + 1] for ax in range(3)]
        sg = -s                                                  # the building is on side s: the balcony looks the other way
        wall, front = (hi[a], lo[a]) if s > 0 else (lo[a], hi[a])
        u = 1 - a
        found.append(((a, sg, hi[2], wall * sg, front * sg, lo[u], hi[u]), cells))
    return found


def terrace_edges(solid, cells, lines):
    """The open edges of the base storey's roof (where it drops away, not where a wall rises): railing runs
    (fixed axis, its value, inward sign, along from, along to, floor z), merged along each edge."""
    cellset = set(cells)
    pieces = {}
    for i, j, k in cellset:
        if k + 1 < solid.shape[2] and solid[i, j, k + 1]: continue        # covered: not walked on
        for ax, s in ((0, -1), (0, 1), (1, -1), (1, 1)):
            n = [i, j]; n[ax] += s
            if 0 <= n[0] < solid.shape[0] and 0 <= n[1] < solid.shape[1] and solid[n[0], n[1], k]: continue
            c = (i, j)[ax]
            value = lines[ax][c + 1] if s > 0 else lines[ax][c]
            o = 1 - ax; oc = (i, j)[o]
            pieces.setdefault((ax, round(value, 3), -s, round(lines[2][k + 1], 3)), []).append((lines[o][oc], lines[o][oc + 1]))
    runs = []
    for (ax, value, inward, z), spans in pieces.items():
        spans.sort()
        r0, r1 = spans[0]
        for s0, s1 in spans[1:] + [(1e9, 1e9)]:
            if s0 <= r1 + 1e-3: r1 = max(r1, s1); continue
            if r1 - r0 > .6: runs.append((ax, value, inward, r0, r1, z))
            r0, r1 = s0, s1
    return runs


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
    # Right of the canopy the AI model's first-floor recess ran on to the canopy (a balcony railing past its balcony,
    # a slit of window in the corner); left of it that stretch is wall. Wall on the right too, like the left.
    for x0, x1, y0, y1, z0, z1 in FILL_WALL:
        solid |= ((cx[:, None, None] > x0) & (cx[:, None, None] < x1) & (cy[None, :, None] > y0) &
                  (cy[None, :, None] < y1) & (cz[None, None, :] > z0) & (cz[None, None, :] < z1))
    kept = drop_thin(solid, xs, ys, zs, THIN, log)
    # What was thin in height and stood out of the walls: the AI model's balcony slabs (rebuilt by rebuild_balconies).
    flat = solid & ~drop_thin(solid, xs, ys, zs, (0.0, 0.0, THIN[2]), log_none)
    # A balcony: 0.75-3.2 m out, 1.2-10 m wide, from the first floor up. Deeper, wider or at the ground: the terrace
    # round the base, the base storey's roof along the sides, roof terraces (their balustrades are built elsewhere).
    slabs, terrace_cells = [], []
    for s, cells in find_slabs(flat, kept, xs, ys, zs):
        if BALCONY_DEPTH <= s[4] - s[3] <= 3.2 and 1.2 <= s[6] - s[5] <= 10.0 and s[2] > 3.0: slabs.append(s)
        elif s[4] - s[3] > 3.2 or s[2] <= 3.0:
            kept[tuple(cells.T)] = True                          # part of the building: stays solid
            if 3.0 < s[2] < 12.0: terrace_cells.extend(map(tuple, cells))   # the base storey's roof: a terrace
    log('balcony slabs in the AI model', len(slabs))
    solid = kept
    terraces = terrace_edges(solid, terrace_cells, coarse)
    log('base storey terrace railing runs', len(terraces))

    # Windows with a wall standing in front of them (the AI model had them in a corner or a bay's jamb): on the clean
    # walls only a slit of them showed. Those aren't cut in, and their frames and glass go (remove_buried_windows).
    def solid_at(p):
        i = [np.searchsorted(l, v) - 1 for l, v in zip(coarse, p)]
        return all(0 <= i[k] < solid.shape[k] for k in range(3)) and solid[i[0], i[1], i[2]]
    buried = []                 # window numbers (1.., the 'win' face attribute of the detail mesh)
    for wi, w in enumerate(windows):
        a, sg, sglass, swall, u0, u1, z0, z1, teal = w
        a = int(a)
        hits = 0
        for fu in (.1, .3, .5, .7, .9):
            for fz in (.1, .3, .5, .7, .9):
                p = [0.0, 0.0, z0 + (z1 - z0) * fz]; p[a] = (swall + .2) * sg; p[1 - a] = u0 + (u1 - u0) * fu
                hits += solid_at(p)
        if hits >= 2: buried.append(wi + 1)
    windows = [w for wi, w in enumerate(windows) if wi + 1 not in buried]
    log('windows behind a wall (not cut in, frames removed)', len(buried))

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
    return slabs, buried, terraces


# ---------------------------------------------------------------------------------------------------- balconies

def remove_buried_windows(details, buried, log):
    """The frames, glass and sills of the windows clean_massing found behind a wall (their 'win' face attribute)."""
    me = details.data
    attr = me.attributes.get('win')
    if attr is None: return
    win = np.empty(len(me.polygons), dtype=np.int32); attr.data.foreach_get('value', win)
    doomed = np.nonzero(np.isin(win, np.array(buried, dtype=np.int32)))[0]
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.faces[i] for i in doomed], context='FACES')
    bm.to_mesh(me); bm.free()
    me.attributes.remove(me.attributes['win'])
    me.update()
    log('buried windows removed', len(buried), 'faces', len(doomed))


def _wall_at(bvh, a, sg, u, z, d_from):
    """Signed depth (outward +) of the clean wall met looking in from d_from at (u, z); None if open, or if the ray
    meets a wall from inside."""
    o = [0.0, 0.0, z]; o[a] = d_from * sg; o[1 - a] = u
    d = [0.0, 0.0, 0.0]; d[a] = -sg
    loc, n, i, dist = bvh.ray_cast(Vector(o), Vector(d), 40.0)
    if loc is None or n[a] * sg < .9: return None
    return loc[a] * sg


def _side(bvh, a, sg, wall, front, zs, edge, toward):
    """How far out a side wall (a bay's jamb) beside the balcony's end reaches (capped at its front), or None for an
    open side."""
    w = _wall_at(bvh, a, sg, edge + .08 * toward, zs + 1.2, front + 3.0)
    return min(w, front) if w is not None and w > wall + .2 else None


def _balcony(b, a, sg, wall, front, ul, ur, zs, rail_top, cap, closed_l, closed_r, idx):
    """Slab, drip and gold lines, the front balustrade with corner posts, side balustrades where no wall closes the
    side. Facade coordinates: depth d (outward +), along the wall u, height z."""
    W, WH, G = idx['wall'], idx['white'], idx['gold']
    u_ax = 1 - a

    def box(d0, d1, u0, u1, z0, z1, m):
        lo = [0.0, 0.0, z0]; hi = [0.0, 0.0, z1]
        lo[a], hi[a] = sorted((d0 * sg, d1 * sg)); lo[u_ax], hi[u_ax] = u0, u1
        b.box(tuple(lo), tuple(hi), m)

    box(wall - .12, front + .06, ul - .05, ur + .05, zs - .42, zs, W)                 # one clean slab, into the wall
    box(front + .06, front + .1, ul - .05, ur + .05, zs - .16, zs - .06, WH)           # drip moulding
    box(front + .06, front + .075, ul - .03, ur + .03, zs - .055, zs - .025, G)        # gold slab line
    box(front - .14, front - .03, ul + .02, ur - .02, rail_top - .08, rail_top, cap)   # front handrail
    box(front - .15, front - .02, ul + .02, ur - .02, rail_top, rail_top + .03, G)     # gold cap
    box(front - .13, front - .05, ul + .02, ur - .02, zs, zs + .1, WH)                 # bottom rail
    n = max(2, int((ur - ul - .4) / .18))
    for i in range(n + 1):
        x = ul + .22 + (ur - ul - .44) * i / n
        box(front - .12, front - .06, x - .025, x + .025, zs + .1, rail_top - .08, WH)   # balusters
    for side, closed in ((ul + .02, closed_l), (ur - .18, closed_r)):
        box(front - .2, front - .02, side, side + .16, zs, rail_top + .04, WH)        # corner post
        box(front - .17, front - .05, side + .02, side + .14, rail_top + .04, rail_top + .13, G)  # finial
        start = wall if closed is None else closed
        if front - .2 - start < .15: continue                                          # the bay's wall closes this side
        box(start - .02, front - .2, side + .04, side + .12, rail_top - .08, rail_top, cap)   # side handrail
        box(start - .02, front - .2, side + .05, side + .11, zs, zs + .1, WH)          # side bottom rail
        k = max(1, int((front - start - .3) / .18))
        for i in range(k):
            d = start + .1 + (front - start - .35) * i / k
            box(d, d + .05, side + .055, side + .105, zs + .1, rail_top - .08, WH)    # side balusters




def rebuild_balconies(hotel, details, slabs, terraces, names, log):
    """The balconies, where the AI model has a slab standing out of a wall (found on the massing grid, so against
    the real wall: no rails short of it or across a door, none on a balcony that isn't there). Each gets a clean slab,
    a balustrade along its front and side balustrades where no wall (a bay's jamb) closes the side. The rail caps keep
    the colour the window step saw on the AI model's (teal on some), white otherwise."""
    from hotel_exterior import Builder, TERRACES
    idx = {n: i for i, n in enumerate(names)}
    found = json.loads(WINDOWS.read_text(encoding='utf-8'))['balconies']
    me = hotel.data
    me.calc_loop_triangles()
    bvh = BVHTree.FromPolygons([v.co.copy() for v in me.vertices], [tuple(t.vertices) for t in me.loop_triangles])

    def on_terrace(zs, x, y):
        return any(abs(zs - zr) < .35 and xa < x < xb and ya < y < yb for zr, xa, xb, ya, yb in TERRACES)

    def cap_of(a, sg, zs, u0, u1):
        for fa, fsg, fzs, fwall, ffront, ful, fur, capmat, captop in found:
            if int(fa) == a and fsg == sg and abs(fzs - zs) < .5 and ful < u1 and fur > u0 and capmat is not None:
                return int(capmat)
        return idx['white']

    b = Builder()
    built = skipped = 0
    for a, sg, zs, wall, front, ul, ur in slabs:
        centre = [0.0, 0.0]; centre[a] = (wall + front) / 2 * sg; centre[1 - a] = (ul + ur) / 2
        if on_terrace(zs, *centre):
            skipped += 1                       # a roof terrace's edge: it has its own balustrade
            continue
        closed_l = _side(bvh, a, sg, wall, front, zs, ul, -1)
        closed_r = _side(bvh, a, sg, wall, front, zs, ur, 1)
        _balcony(b, a, sg, wall, front, ul, ur, zs, zs + 1.03, cap_of(a, sg, zs, ul, ur), closed_l, closed_r, idx)
        built += 1
    b.append_to(details)
    log('balconies built', built, 'terrace edges skipped', skipped)
    t = Builder()
    for run in terraces: _terrace_railing(t, idx, *run)
    t.append_to(details)


def _terrace_railing(b, idx, ax, value, inward, r0, r1, z):
    """A balustrade along one open edge of the base storey's roof, set in from the edge: posts at the ends and about
    every 3 m with gold finials, a white handrail with a gold cap, a bottom rail and balusters (like the roofs')."""
    W, G = idx['white'], idx['gold']
    al = 1 - ax
    at = value + inward * .15

    def rb(a0, a1, f0, f1, z0, z1, m):
        p, q = [0.0, 0.0, z0], [0.0, 0.0, z1]
        p[al], q[al] = a0, a1; p[ax], q[ax] = sorted((at + f0, at + f1))
        b.box(tuple(p), tuple(q), m)

    r0, r1 = r0 + .05, r1 - .05
    rb(r0, r1, -.07, .07, z + 1.0, z + 1.08, W)             # handrail
    rb(r0, r1, -.08, .08, z + 1.08, z + 1.11, G)            # gold cap
    rb(r0, r1, -.06, .06, z + .04, z + .14, W)              # bottom rail
    n = max(2, int((r1 - r0) / .2))
    for i in range(n + 1):
        x = r0 + .05 + (r1 - r0 - .1) * i / n
        rb(x - .025, x + .025, -.025, .025, z + .14, z + 1.0, W)   # balusters
    m = max(1, int(round((r1 - r0) / 3)))
    for i in range(m + 1):
        x = r0 + (r1 - r0) * i / m
        rb(x - .11, x + .11, -.11, .11, z + .04, z + 1.12, W)      # posts
        rb(x - .07, x + .07, -.07, .07, z + 1.12, z + 1.24, G)     # gold finial
