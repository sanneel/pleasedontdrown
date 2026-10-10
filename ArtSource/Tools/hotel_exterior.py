"""Exterior finishing for the polished Tripo hotel (used by build_realistic_hotel.py), after hotel_windows.

- The porte-cochere is rebuilt as clean architecture in the model's own place and proportions: stone plinths, round
  teal shafts with gold rings, a cream entablature, teal fascia with a gold band, a crisp cornice and a flat soffit.
- Roof edges get a clean cornice and coping over the AI model's ragged ones.
- Window glass varies (dark, lighter tint, drawn curtains), like an occupied hotel.
- Box-projected UVs in metres so Unity can lay a subtle plaster texture over the render.
Blender coordinates: x across the front, -y out of the front facade, z up (metres, ground = 0).
"""
import math

import bmesh
import bpy
import numpy as np
from mathutils import Vector

# Porte-cochere, measured on the source model (see Logs/hotel-realistic.json).
CANOPY_X = (-6.45, 6.35)
CANOPY_Y = (-16.3, -5.3)        # front edge .. facade
CANOPY_TOP = 8.15
SOFFIT = 5.95
# Entrance wall (front face) and doorway, matching the lobby moved inside (Unity: GameSceneBuilder.HotelIsland).
ENTRANCE_WALL_Y = -3.75
DOOR_HALF = 1.3
COLUMNS = [(-4.95, -14.8), (4.75, -14.8), (-5.25, -6.0), (4.95, -6.0)]


def ensure_materials(mesh, names, classes):
    """The mesh gets exactly the class materials, in class order (existing slots keep their index)."""
    have = [m.name for m in mesh.materials]
    for n in names:
        name = 'tripohotel_' + n
        if name in have: continue
        m = bpy.data.materials.get(name)
        if m is None:
            colour, rough, metal = classes[n]
            m = bpy.data.materials.new(name); m.use_nodes = True
            p = m.node_tree.nodes.get('Principled BSDF')
            p.inputs['Base Color'].default_value = (*colour, 1); p.inputs['Roughness'].default_value = rough
            p.inputs['Metallic'].default_value = metal; m.diffuse_color = (*colour, 1)
        mesh.materials.append(m)
    order = [m.name for m in mesh.materials]
    assert order[:len(names)] == ['tripohotel_' + n for n in names], order


class Builder:
    """Collects boxes and cylinders, then appends them to a mesh with outward normals."""

    def __init__(self):
        self.verts, self.faces, self.mats = [], [], []

    def box(self, lo, hi, m):
        base = len(self.verts)
        for x in (lo[0], hi[0]):
            for y in (lo[1], hi[1]):
                for z in (lo[2], hi[2]): self.verts.append((x, y, z))
        for q in ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)):
            self.faces.append([base + i for i in q]); self.mats.append(m)

    def cylinder(self, cx, cy, z0, z1, r, m, sides=20):
        base = len(self.verts)
        for z in (z0, z1):
            for i in range(sides):
                t = 2 * math.pi * i / sides
                self.verts.append((cx + r * math.cos(t), cy + r * math.sin(t), z))
        for i in range(sides):
            j = (i + 1) % sides
            self.faces.append([base + i, base + j, base + sides + j, base + sides + i]); self.mats.append(m)
        self.faces.append([base + sides + i for i in range(sides)]); self.mats.append(m)
        self.faces.append([base + i for i in reversed(range(sides))]); self.mats.append(m)

    def mesh(self, verts, faces, m):
        base = len(self.verts)
        self.verts.extend(map(tuple, verts))
        for f in faces: self.faces.append([base + i for i in f]); self.mats.append(m)

    def append_to(self, obj):
        bm = bmesh.new(); bm.from_mesh(obj.data)
        start = len(bm.faces)
        vs = [bm.verts.new(v) for v in self.verts]
        for f, m in zip(self.faces, self.mats):
            face = bm.faces.new([vs[i] for i in f]); face.material_index = m
        bm.faces.ensure_lookup_table()
        new = bm.faces[start:]
        bmesh.ops.recalc_face_normals(bm, faces=new)
        bmesh.ops.triangulate(bm, faces=new)
        bm.to_mesh(obj.data); bm.free(); obj.data.update()


def delete_in_box(obj, lo, hi):
    me = obj.data
    n = len(me.polygons)
    c = np.empty(n * 3); me.polygons.foreach_get('center', c); c = c.reshape(-1, 3)
    inside = np.all((c > np.array(lo)) & (c < np.array(hi)), axis=1)
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.faces[i] for i in np.nonzero(inside)[0]], context='FACES')
    bm.to_mesh(me); bm.free(); me.update()
    return int(inside.sum())


def rebuild_canopy(hotel, details, idx, log):
    x0, x1 = CANOPY_X; y0, y1 = CANOPY_Y
    lo, hi = (x0 - .3, y0 - .3, .28), (x1 + .3, y1, CANOPY_TOP + .4)
    removed = delete_in_box(hotel, lo, hi) + delete_in_box(details, lo, hi)
    b = Builder()
    W, T, G, S, WH = idx['wall'], idx['teal'], idx['gold'], idx['stone'], idx['white']
    for cx, cy in COLUMNS:
        b.box((cx - .48, cy - .48, .28), (cx + .48, cy + .48, 1.05), S)              # plinth
        b.box((cx - .4, cy - .4, 1.05), (cx + .4, cy + .4, 1.2), WH)                 # base moulding
        b.cylinder(cx, cy, 1.2, 1.32, .36, G)                                        # gold ring
        b.cylinder(cx, cy, 1.32, 5.45, .31, T)                                       # shaft
        b.cylinder(cx, cy, 5.45, 5.55, .36, G)                                       # gold necking
        b.box((cx - .42, cy - .42, 5.55), (cx + .42, cy + .42, 5.75), WH)            # capital
        b.box((cx - .5, cy - .5, 5.75), (cx + .5, cy + .5, SOFFIT), WH)              # abacus
    b.box((x0, y0, SOFFIT), (x1, y1, 6.55), W)                                       # entablature + soffit
    b.box((x0 - .02, y0 - .02, 6.55), (x1 + .02, y1, 6.62), G)                       # gold band
    b.box((x0, y0, 6.62), (x1, y1, 7.45), T)                                         # teal fascia
    b.box((x0 - .18, y0 - .18, 7.45), (x1 + .18, y1, 7.62), WH)                      # cornice
    b.box((x0 - .1, y0 - .1, 7.62), (x1 + .1, y1, CANOPY_TOP), W)                    # roof slab / parapet
    b.box((x0 - .14, y0 - .14, CANOPY_TOP), (x1 + .14, y1, CANOPY_TOP + .06), WH)    # coping
    # Recessed soffit coffers with gold trim (the lights in Unity hang here).
    for cx in (-2.6, 2.4):
        for cy in (-13.4, -8.4):
            b.box((cx - 1.6, cy - 1.9, SOFFIT - .02), (cx + 1.6, cy + 1.9, SOFFIT + .01), G)
            b.box((cx - 1.5, cy - 1.8, SOFFIT - .03), (cx + 1.5, cy + 1.8, SOFFIT + .0), WH)
    # The entrance wall under the canopy: the AI facade there (shards, a broken balcony over the door) is replaced by
    # a clean wall with a 2.6 m doorway into the lobby, a stone and gold door surround and gold-framed shopfronts.
    removed += delete_in_box(hotel, (x0 - .05, -5.45, .28), (x1 + .05, -2.9, SOFFIT)) +         delete_in_box(details, (x0 - .05, -5.45, .28), (x1 + .05, -2.9, SOFFIT))
    wy0, wy1 = ENTRANCE_WALL_Y, ENTRANCE_WALL_Y + .4
    D = DOOR_HALF
    b.box((x0, wy0, .28), (-D, wy1, SOFFIT), W); b.box((D, wy0, .28), (x1, wy1, SOFFIT), W)
    b.box((-D, wy0, 3.3), (D, wy1, SOFFIT), W)                                        # over the door
    b.box((-D - .5, wy0 - .22, .28), (-D, wy0, 3.85), S)                               # stone jambs
    b.box((D, wy0 - .22, .28), (D + .5, wy0, 3.85), S)
    b.box((-D - .5, wy0 - .26, 3.3), (D + .5, wy0, 3.85), S)                           # stone head
    b.box((-D - .55, wy0 - .3, 3.85), (D + .55, wy0, 3.95), G)                         # gold cap
    for sx in (-1, 1):                                                                 # gold door reveals
        b.box((min(sx * D, sx * (D - .05)), wy0 - .24, .3), (max(sx * D, sx * (D - .05)), wy1, 3.3), G)
    b.box((-D, wy0 - .2, 4.05), (D, wy0 - .02, 4.9), idx['glass'])                    # transom light
    b.box((-D, wy0 - .24, 4.0), (D, wy0, 4.05), G); b.box((-D, wy0 - .24, 4.9), (D, wy0, 4.95), G)
    for sx in (-1, 1):                                                                 # shopfront windows
        xa, xb = sorted((sx * 2.3, sx * 5.7))
        b.box((xa, wy0 - .12, .75), (xb, wy0 - .02, 4.6), idx['glass'])
        b.box((xa - .08, wy0 - .2, .65), (xb + .08, wy0, .75), G)
        b.box((xa - .08, wy0 - .2, 4.6), (xb + .08, wy0, 4.7), G)
        for xx in np.linspace(xa, xb, 4):
            b.box((xx - .04, wy0 - .18, .75), (xx + .04, wy0, 4.6), G)
        b.box((xa - .2, wy0 - .3, .28), (xb + .2, wy0, .65), S)                        # stone stall riser
    # The narrow wall strips either side of the canopy (tower recess): shards and a crumpled teal strip in the AI model.
    for sx in (-1, 1):
        xa, xb = sorted((sx * 6.0, sx * 8.4))
        removed += delete_in_box(hotel, (xa, -7.6, .28), (xb, -2.9, 8.5)) + delete_in_box(details, (xa, -7.6, .28), (xb, -2.9, 8.5))
        xa2, xb2 = sorted((sx * (x1 if sx > 0 else -x0), sx * 8.4))
        b.box((xa2, ENTRANCE_WALL_Y, .28), (xb2, ENTRANCE_WALL_Y + .4, 8.5), W)
    # Front terrace: one clean stone podium along the whole front instead of the AI slab's ragged edge.
    removed += delete_in_box(hotel, (-25.4, -16.0, -.05), (25.4, -4.3, .4))
    b.box((-25.3, -15.85, 0), (25.3, -4.2, .3), S)
    b.box((-25.38, -15.93, .22), (25.38, -15.75, .31), WH)                              # nosing (front edge only)
    b.append_to(details)
    log('porte-cochere rebuilt; old faces removed', removed)


ROOF_RAIL_ZONES = []
POSTS = []              # (x, y, floor z) of every balustrade post
TERRACES = []           # (floor z, x0, x1, y0, y1) of every railed roof terrace, for the gold dressing


def roof_balustrade(c, nm, ar, kill, b, idx, zr, xa, xb, ya, yb):
    """Clean balustrade round a roof terrace's open edges (1 m segments; none where a taller wall rises). The AI
    balustrade in that strip is marked for removal. Returns the number of runs built."""
    W, G = idx['white'], idx['gold']
    wallish = (np.abs(nm[:, 2]) < .3) & (c[:, 2] > zr + 1.6) & (c[:, 2] < zr + 4.5)
    runs = 0
    # (fixed axis, fixed value, inward sign, along range)
    for fa, fv, inward, (la0, la1) in ((1, ya, 1, (xa, xb)), (1, yb, -1, (xa, xb)), (0, xa, 1, (ya, yb)), (0, xb, -1, (ya, yb))):
        al = 1 - fa
        rail_at = fv + inward * .15
        dist = (c[:, fa] - fv) * inward                                  # inward distance from the edge
        segs = np.arange(np.floor(la0), np.ceil(la1))
        open_ = []
        for s0 in segs:
            near = wallish & (dist > -.3) & (dist < 1.2) & (c[:, al] > s0) & (c[:, al] < s0 + 1)
            open_.append(ar[near].sum() < .15)
        k = 0
        while k < len(segs):                                             # contiguous open runs
            if not open_[k]:
                k += 1; continue
            j = k
            while j + 1 < len(segs) and open_[j + 1]: j += 1
            r0, r1 = max(segs[k], la0), min(segs[j] + 1, la1)
            k = j + 1
            if r1 - r0 < 1.2: continue
            strip = (dist > -.45) & (dist < .9) & (c[:, al] > r0 - .05) & (c[:, al] < r1 + .05) & \
                (c[:, 2] > zr + .06) & (c[:, 2] < zr + 1.5) & (ar < 1.5)
            kill |= strip
            lo = [0.0, 0.0, zr + .06]; hi = [0.0, 0.0, zr + 1.5]
            lo[al], hi[al] = r0 - .05, r1 + .05; lo[fa], hi[fa] = sorted((fv - inward * .45, fv + inward * .9))
            ROOF_RAIL_ZONES.append((tuple(lo), tuple(hi)))

            def rb(a0, a1, f0, f1, z0, z1, m):
                p, q = [0.0, 0.0, z0], [0.0, 0.0, z1]
                p[al], q[al] = a0, a1; p[fa], q[fa] = sorted((rail_at + f0, rail_at + f1))
                b.box(tuple(p), tuple(q), m)
            rb(r0, r1, -.07, .07, zr + 1.0, zr + 1.08, W)                    # handrail
            rb(r0, r1, -.08, .08, zr + 1.08, zr + 1.11, G)                   # gold cap
            rb(r0, r1, -.06, .06, zr + .04, zr + .14, W)                     # bottom rail
            n = max(2, int((r1 - r0) / .2))
            for i in range(n + 1):
                x = r0 + .05 + (r1 - r0 - .1) * i / n
                rb(x - .025, x + .025, -.025, .025, zr + .14, zr + 1.0, W)   # balusters
            m = max(1, int(round((r1 - r0) / 3)))
            for i in range(m + 1):
                x = r0 + (r1 - r0) * i / m
                rb(x - .11, x + .11, -.11, .11, zr + .04, zr + 1.12, W)      # posts
                rb(x - .07, x + .07, -.07, .07, zr + 1.12, zr + 1.24, G)     # gold finial
                p = [0.0, 0.0]; p[al] = x; p[fa] = rail_at; POSTS.append((p[0], p[1], zr))
            runs += 1
    return runs


def face_vertex_arrays(me):
    """(face vertex indices padded with -1, vertex z) for connectivity checks."""
    n = len(me.polygons)
    starts = np.empty(n, dtype=np.int64); me.polygons.foreach_get('loop_start', starts)
    totals = np.empty(n, dtype=np.int64); me.polygons.foreach_get('loop_total', totals)
    vidx = np.empty(len(me.loops), dtype=np.int64); me.loops.foreach_get('vertex_index', vidx)
    k = int(totals.max())
    pos = starts[:, None] + np.arange(k)
    fv = np.where(np.arange(k) < totals[:, None], vidx[np.minimum(pos, len(vidx) - 1)], -1)
    co = np.empty(len(me.vertices) * 3); me.vertices.foreach_get('co', co)
    return fv, co.reshape(-1, 3)[:, 2]


def roof_junk(c, fv, vz, kill, zr, xa, xb, ya, yb):
    """AI clutter standing on a roof terrace (crumpled pergolas, loose rail pieces along notched corners): every
    connected piece in the 2.6 m above the floor that doesn't rise on into a taller block's wall is removed."""
    sel = np.nonzero((c[:, 0] > xa - .7) & (c[:, 0] < xb + .7) & (c[:, 1] > ya - .7) & (c[:, 1] < yb + .7) &
                     (c[:, 2] > zr + .05) & (c[:, 2] < zr + 2.6))[0]
    if not len(sel): return 0
    f = fv[sel]
    label = np.arange(len(vz))
    while True:                                                          # min-label flood through shared vertices
        lab = np.where(f >= 0, label[np.maximum(f, 0)], np.iinfo(np.int64).max).min(1)
        before = label.copy()
        for j in range(f.shape[1]):
            ok = f[:, j] >= 0
            np.minimum.at(label, f[ok, j], lab[ok])
        if np.array_equal(before, label): break
    comp = label[f[:, 0]]
    top = np.full(len(vz), -1e9)
    for j in range(f.shape[1]):
        ok = f[:, j] >= 0
        np.maximum.at(top, comp[ok], vz[f[ok, j]])
    junk = top[comp] < zr + 2.2
    kill[sel[junk]] = True
    return int(junk.sum())


def terrace_decks(details, b, idx):
    """One flat paved deck per terrace over its whole railed rectangle. The AI roofs step down into balcony notches
    and a gutter along the front, which showed through the new balustrade; the deck closes them. Rebuilt balcony
    rails left standing in those notches (now inside the terrace) are removed, but not the walls and windows of a
    higher block standing on the terrace."""
    me = details.data
    n = len(me.polygons)
    c = np.empty(n * 3); me.polygons.foreach_get('center', c); c = c.reshape(-1, 3)
    kill = np.zeros(n, dtype=bool)
    for zr, xa, xb, ya, yb in TERRACES:
        on = (c[:, 0] > xa + .45) & (c[:, 0] < xb - .45) & (c[:, 1] > ya + .45) & (c[:, 1] < yb - .45) & \
            (c[:, 2] > zr - .4) & (c[:, 2] < zr + 1.4)
        for z2, xa2, xb2, ya2, yb2 in TERRACES:                          # blocks standing on this terrace
            if z2 > zr + 1:
                on &= ~((c[:, 0] > xa2 - .5) & (c[:, 0] < xb2 + .5) & (c[:, 1] > ya2 - .5) & (c[:, 1] < yb2 + .5))
        kill |= on
        b.box((xa, ya, zr - .12), (xb, yb, zr + .03), idx['stone'])
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.faces[i] for i in np.nonzero(kill)[0]], context='FACES')
    bm.to_mesh(me); bm.free(); me.update()
    return int(kill.sum())


def roof_edges(hotel, details, idx, log):
    """Flat roofs: find each roof rectangle (up-facing faces above 12 m, gridded) and run a clean cornice + coping
    round its edge, hiding the ragged AI edge."""
    me = hotel.data
    n = len(me.polygons)
    c = np.empty(n * 3); me.polygons.foreach_get('center', c); c = c.reshape(-1, 3)
    nm = np.empty(n * 3); me.polygons.foreach_get('normal', nm); nm = nm.reshape(-1, 3)
    ar = np.empty(n); me.polygons.foreach_get('area', ar)
    fv, vz = face_vertex_arrays(me)
    junk = 0
    up = (nm[:, 2] > .95) & (c[:, 2] > 12)
    order = np.argsort(c[up, 2]); z = c[up, 2][order]
    group = np.concatenate([[0], np.cumsum(np.diff(z) > .35)])
    ids = np.nonzero(up)[0][order]
    b = Builder(); count = 0; rails = 0
    kill = np.zeros(n, dtype=bool)
    for g in range(group.max() + 1):
        f = ids[group == g]
        if ar[f].sum() < 25: continue
        zr = float(np.median(c[f, 2]))
        # Grid the faces (1 m) and split into connected roofs.
        cells = {}
        for i in f: cells.setdefault((int(math.floor(c[i, 0])), int(math.floor(c[i, 1]))), []).append(i)
        seen = set()
        for start in cells:
            if start in seen: continue
            stack, comp = [start], []
            seen.add(start)
            while stack:
                cell = stack.pop(); comp.append(cell)
                for dx in (-1, 0, 1):
                    for dy in (-1, 0, 1):
                        nb = (cell[0] + dx, cell[1] + dy)
                        if nb in cells and nb not in seen: seen.add(nb); stack.append(nb)
            fs = [i for cell in comp for i in cells[cell]]
            if ar[fs].sum() < 20: continue
            xs = np.percentile(c[fs, 0], [1, 99]); ys = np.percentile(c[fs, 1], [1, 99])
            if xs[1] - xs[0] < 4 or ys[1] - ys[0] < 4: continue
            (xa, xb), (ya, yb) = xs, ys
            if ar[fs].sum() / ((xb - xa) * (yb - ya)) > .45:        # a roof floor, not a parapet top
                rails += roof_balustrade(c, nm, ar, kill, b, idx, zr, xa, xb, ya, yb)
                junk += roof_junk(c, fv, vz, kill, zr, xa, xb, ya, yb)
                TERRACES.append((zr, xa, xb, ya, yb))
            o = .32
            for lo, hi in (((xa - o, ya - o), (xb + o, ya + .25)), ((xa - o, yb - .25), (xb + o, yb + o)),
                           ((xa - o, ya - o), (xa + .25, yb + o)), ((xb - .25, ya - o), (xb + o, yb + o))):
                b.box((lo[0], lo[1], zr - .62), (hi[0], hi[1], zr - .38), idx['trim'])      # cornice band
                b.box((lo[0] + .06, lo[1] + .06, zr - .38), (hi[0] - .06, hi[1] - .06, zr - .3), idx['gold'])
                b.box((lo[0] + .1, lo[1] + .1, zr - .3), (hi[0] - .1, hi[1] - .1, zr + .04), idx['wall'])
            count += 1
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.faces[i] for i in np.nonzero(kill)[0]], context='FACES')
    bm.to_mesh(me); bm.free(); me.update()
    for lo, hi in ROOF_RAIL_ZONES: delete_in_box(details, lo, hi)        # old rail caps from hotel_windows
    ROOF_RAIL_ZONES.clear()
    decks = terrace_decks(details, b, idx)
    b.append_to(details)
    log('terrace decks', len(TERRACES), 'balcony faces under them removed', decks)
    log('roof cornices', count, 'roof balustrade runs', rails, 'AI faces removed', int(kill.sum()), 'of them roof clutter', junk)
    for t in TERRACES: log('terrace z %.2f x %.1f..%.1f y %.1f..%.1f' % t)


def ledges(hotel, details, idx, log):
    """Floor bands and cornices running along a facade: replaced by straight boxes (the AI ones are jagged)."""
    me = hotel.data
    n = len(me.polygons)
    c = np.empty(n * 3); me.polygons.foreach_get('center', c); c = c.reshape(-1, 3)
    nm = np.empty(n * 3); me.polygons.foreach_get('normal', nm); nm = nm.reshape(-1, 3)
    ar = np.empty(n); me.polygons.foreach_get('area', ar)
    mt = np.empty(n, dtype=np.int64); me.polygons.foreach_get('material_index', mt)
    solid = np.isin(mt, [idx['wall'], idx['trim'], idx['teal'], idx['white']])
    up = nm[:, 2] > .9
    b = Builder(); kill = np.zeros(n, dtype=bool); count = 0
    for a in (0, 1):
        u = 1 - a
        for sg in (-1, 1):
            facing = solid & (nm[:, a] * sg > .9)
            d = c[:, a] * sg
            order = np.argsort(d[facing]); dv = d[facing][order]; fa = np.nonzero(facing)[0][order]
            grp = np.concatenate([[0], np.cumsum(np.diff(dv) > .15)])
            for g in range(grp.max() + 1):
                f = fa[grp == g]
                if ar[f].sum() < 15: continue
                plane = float(np.median(d[f]))
                ua, ub = np.percentile(c[f, u], [1, 99])
                cand = up & (d > plane - .05) & (d < plane + .8) & (c[:, u] > ua) & (c[:, u] < ub) & (c[:, 2] > 1.5)
                if cand.sum() < 4: continue
                zo = np.argsort(c[cand, 2]); zv = c[cand, 2][zo]; ci = np.nonzero(cand)[0][zo]
                zg = np.concatenate([[0], np.cumsum(np.diff(zv) > .12)])
                for h in range(zg.max() + 1):
                    lf = ci[zg == h]
                    bins = np.unique(np.floor(c[lf, u] / .5))
                    if len(bins) * .5 < .55 * (ub - ua) or len(bins) * .5 < 4: continue
                    ztop = float(np.median(c[lf, 2]))
                    front = float(np.percentile(d[lf], 85)) + .05
                    lo_u, hi_u = bins.min() * .5, bins.max() * .5 + .5
                    band = (d > plane + .01) & (d < front + .08) & (c[:, 2] > ztop - .4) & (c[:, 2] < ztop + .06) &                         (c[:, u] > lo_u) & (c[:, u] < hi_u) & solid
                    kill |= band
                    m = idx['trim'] if (mt[lf] == idx['trim']).mean() > .4 else idx['wall']
                    lo = [0.0, 0.0, ztop - .32]; hi = [0.0, 0.0, ztop]
                    lo[a], hi[a] = sorted((sg * (plane - .04), sg * front)); lo[u], hi[u] = lo_u, hi_u
                    b.box(lo, hi, m)
                    lo2 = list(lo); hi2 = list(hi); lo2[2], hi2[2] = ztop - .4, ztop - .32
                    lo2[2], hi2[2] = ztop - .355, ztop - .32
                    lo2[a], hi2[a] = sorted((sg * (plane - .04), sg * (front + .005))); b.box(lo2, hi2, idx['gold'])
                    count += 1
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.faces[i] for i in np.nonzero(kill)[0]], context='FACES')
    bm.to_mesh(me); bm.free(); me.update()
    b.append_to(details)
    log('ledges rebuilt', count, 'faces replaced', int(kill.sum()))


def facade_shards(hotel, details, idx, log):
    """Thin slanted slivers of the AI mesh standing out of flat walls (the 'cracks' on the tower's side and the
    ragged band under the cornices). Any slanted wall-coloured face within 45 cm in front of a big flat wall goes.
    The slivers were folded into the wall surface, so each gap is backed by a plaster patch just inside the wall
    (25 cm cells), never over a window opening."""
    me = hotel.data
    n = len(me.polygons)
    c = np.empty(n * 3); me.polygons.foreach_get('center', c); c = c.reshape(-1, 3)
    nm = np.empty(n * 3); me.polygons.foreach_get('normal', nm); nm = nm.reshape(-1, 3)
    ar = np.empty(n); me.polygons.foreach_get('area', ar)
    mt = np.empty(n, dtype=np.int64); me.polygons.foreach_get('material_index', mt)
    starts = np.empty(n, dtype=np.int64); me.polygons.foreach_get('loop_start', starts)
    totals = np.empty(n, dtype=np.int64); me.polygons.foreach_get('loop_total', totals)
    vidx = np.empty(len(me.loops), dtype=np.int64); me.loops.foreach_get('vertex_index', vidx)
    co = np.empty(len(me.vertices) * 3); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
    solid = np.isin(mt, [idx['wall'], idx['trim'], idx['white'], idx['teal']])
    oblique = np.abs(nm).max(1) < .93
    # Window glass on the details mesh: (normal axis, centre, min corner, max corner) per pane.
    dm = details.data
    dn = len(dm.polygons)
    dc = np.empty(dn * 3); dm.polygons.foreach_get('center', dc); dc = dc.reshape(-1, 3)
    dnm = np.empty(dn * 3); dm.polygons.foreach_get('normal', dnm); dnm = dnm.reshape(-1, 3)
    dmt = np.empty(dn, dtype=np.int64); dm.polygons.foreach_get('material_index', dmt)
    dco = np.empty(len(dm.vertices) * 3); dm.vertices.foreach_get('co', dco); dco = dco.reshape(-1, 3)
    glass = [(i, dco[list(dm.polygons[i].vertices)]) for i in np.nonzero(dmt == idx['glass'])[0]]
    CELL = .25
    kill = np.zeros(n, dtype=bool)
    b = Builder(); patches = 0
    for a in (0, 1):
        u = 1 - a
        for sg in (-1, 1):
            facing = solid & (nm[:, a] * sg > .97)
            d = c[:, a] * sg
            order = np.argsort(d[facing]); dv = d[facing][order]; fa = np.nonzero(facing)[0][order]
            grp = np.concatenate([[0], np.cumsum(np.diff(dv) > .1)])
            for g in range(grp.max() + 1):
                f = fa[grp == g]
                if ar[f].sum() < 30: continue
                plane = float(np.median(d[f]))
                ua, ub = np.percentile(c[f, u], [.5, 99.5]); za, zb = np.percentile(c[f, 2], [.5, 99.5])
                here = (d > plane - .02) & (d < plane + .45) & (c[:, u] > ua) & (c[:, u] < ub) & \
                    (c[:, 2] > max(za, 1.5)) & (c[:, 2] < zb) & oblique & solid & ~kill
                if not here.any(): continue
                kill |= here
                # Cells (u, z) the removed slivers covered...
                cells = set()
                for i in np.nonzero(here)[0]:
                    p = co[vidx[starts[i]:starts[i] + totals[i]]]
                    for cu in range(int(p[:, u].min() // CELL), int(p[:, u].max() // CELL) + 1):
                        for cz in range(int(p[:, 2].min() // CELL), int(p[:, 2].max() // CELL) + 1):
                            cells.add((cu, cz))
                # ...minus window openings in this wall (glass up to 60 cm behind it, and its frame).
                for i, p in glass:
                    if abs(dnm[i, a]) < .9 or not plane - .6 < dc[i, a] * sg < plane + .1: continue
                    for cu in range(int((p[:, u].min() - .15) // CELL), int((p[:, u].max() + .15) // CELL) + 1):
                        for cz in range(int((p[:, 2].min() - .15) // CELL), int((p[:, 2].max() + .15) // CELL) + 1):
                            cells.discard((cu, cz))
                for cu, cz in cells:
                    lo = [0.0, 0.0, cz * CELL - .01]; hi = [0.0, 0.0, (cz + 1) * CELL + .01]
                    lo[u], hi[u] = cu * CELL - .01, (cu + 1) * CELL + .01
                    lo[a], hi[a] = sorted((sg * (plane - .06), sg * (plane - .012)))
                    b.box(tuple(lo), tuple(hi), idx['wall'])
                    patches += 1
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.faces[i] for i in np.nonzero(kill)[0]], context='FACES')
    bm.to_mesh(me); bm.free(); me.update()
    b.append_to(hotel)
    log('facade slivers removed', int(kill.sum()), 'plaster patches', patches)


# Tower front balcony stack (x, out of the facade in -y): the window rebuild missed the 18 m one, which is still the
# AI's lumpy balcony. It gets a copy of the clean one below it, one storey (3.16 m) up.
TOWER_BAY_X = 3.6
TOWER_BAY_FRONT_Y = -3.95       # faces further out than this belong to the balcony, not the wall or window sills
MISSING_BALCONY_Z, BALCONY_BELOW_Z = 18.04, 14.88
# The recessed bays behind those balconies: side walls at |x| = 2.6 between the facade and the glazed back wall.
BAY_JAMB_X = (2.4, 2.85)
BAY_Y = (-3.93, -1.9)
BAY_Z = (7.6, 35.9)


def tower_balcony(hotel, details, idx, log):
    dz = MISSING_BALCONY_Z - BALCONY_BELOW_Z
    x, y = TOWER_BAY_X, TOWER_BAY_FRONT_Y
    removed = delete_in_box(hotel, (-x, -9, MISSING_BALCONY_Z - 1.1), (x, y, MISSING_BALCONY_Z + 1.5)) + \
        delete_in_box(details, (-x, -9, MISSING_BALCONY_Z - 1.1), (x, y, MISSING_BALCONY_Z + 1.5)) + \
        delete_in_box(hotel, (-BAY_JAMB_X[0], y, MISSING_BALCONY_Z - 1.1),
                      (BAY_JAMB_X[0], BAY_Y[1], MISSING_BALCONY_Z + 1.5))      # AI balcony left inside the bay
    me = details.data
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    src = [f for f in bm.faces if -x < f.calc_center_median().x < x and f.calc_center_median().y < BAY_Y[1] and
           BALCONY_BELOW_Z - .5 < f.calc_center_median().z < BALCONY_BELOW_Z + 1.35]
    dup = bmesh.ops.duplicate(bm, geom=src)
    bmesh.ops.translate(bm, verts=[g for g in dup['geom'] if isinstance(g, bmesh.types.BMVert)], vec=(0, 0, dz))
    bm.to_mesh(me); bm.free(); me.update()
    b = Builder()                                                       # the bays' side walls are jagged: clean jambs
    for sx in (-1, 1):
        xa, xb = sorted((sx * BAY_JAMB_X[0], sx * BAY_JAMB_X[1]))
        b.box((xa, BAY_Y[0], BAY_Z[0]), (xb, BAY_Y[1], BAY_Z[1]), idx['wall'])
    b.append_to(details)
    log('tower balcony at', MISSING_BALCONY_Z, 'rebuilt from', len(src), 'faces; AI faces removed', removed)


def vary_glass(details, idx, log):
    """Occupied-hotel look: per pane, dark glass, a lighter tint or drawn curtains (stable per pane)."""
    me = details.data
    n = len(me.polygons)
    m = np.empty(n, dtype=np.int64); me.polygons.foreach_get('material_index', m)
    c = np.empty(n * 3); me.polygons.foreach_get('center', c); c = c.reshape(-1, 3)
    glass = np.nonzero(m == idx['glass'])[0]
    cell = np.floor(c[glass] / np.array([1.3, 1.3, 3.0])).astype(np.int64)
    h = (cell[:, 0] * 73856093 ^ cell[:, 1] * 19349663 ^ cell[:, 2] * 83492791) % 100
    m[glass[h < 22]] = idx['curtain']
    m[glass[(h >= 22) & (h < 45)]] = idx['glass_b']
    me.polygons.foreach_set('material_index', m.astype(np.int32)); me.update()
    log('glass panes varied: curtains', int((h < 22).sum()), 'tint', int(((h >= 22) & (h < 45)).sum()))


def box_uvs(obj):
    """World-aligned box projection, 1 UV unit = 1 m, for tiling plaster/stone textures in Unity."""
    me = obj.data
    if not me.uv_layers: me.uv_layers.new(name='UVMap')
    bm = bmesh.new(); bm.from_mesh(me)
    uv = bm.loops.layers.uv.active
    for f in bm.faces:
        n = f.normal; a = max(range(3), key=lambda i: abs(n[i]))
        for loop in f.loops:
            p = loop.vert.co
            loop[uv].uv = (p.x, p.y) if a == 2 else ((p.y, p.z) if a == 0 else (p.x, p.z))
    bm.to_mesh(me); bm.free(); me.update()


def text_mesh(body, size, depth, width):
    """Block lettering standing upright, reading along +x and facing -y: (verts, faces). Centred on x = 0, the
    baseline at z = 0, front face at y = 0. Scaled down to fit width."""
    cu = bpy.data.curves.new('lettering', 'FONT')
    cu.body = body; cu.size = size; cu.extrude = depth / 2; cu.align_x = 'CENTER'; cu.resolution_u = 6
    ob = bpy.data.objects.new('lettering', cu); bpy.context.scene.collection.objects.link(ob)
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    v = np.array([vt.co[:] for vt in me.vertices])
    faces = [list(p.vertices) for p in me.polygons]
    bpy.data.objects.remove(ob); bpy.data.curves.remove(cu); bpy.data.meshes.remove(me)
    v = np.stack([v[:, 0], -(v[:, 2] + depth / 2), v[:, 1]], 1)        # stand up, front face at y = 0
    k = min(1.0, width / (v[:, 0].max() - v[:, 0].min()))
    v[:, 0] -= (v[:, 0].max() + v[:, 0].min()) / 2
    v[:, [0, 2]] *= k; v[:, 2] -= v[:, 2].min()
    return v, faces


def gold_dressing(details, idx, log):
    """The things that make it read as an expensive hotel from the beach: a gold rooftop sign, gold lettering on
    the porte-cochere and gold urns on the terrace corners."""
    b = Builder(); G, M = idx['gold'], idx['metal']
    # Rooftop sign on the front of the highest terrace, standing behind its balustrade on a slim steel frame.
    zr, xa, xb, ya, yb = max(TERRACES)
    sy = ya + .55
    v, f = text_mesh('GRAND CORAL', 1.0, .14, xb - xa - 1.2)
    v[:, 1] += sy; v[:, 2] *= 1.25 / v[:, 2].max(); v[:, 2] += zr + 1.45
    b.mesh(v, f, G)
    w = v[:, 0].max()
    b.box((-w - .1, sy + .14, zr + 1.6), (w + .1, sy + .24, zr + 1.72), M)            # rail behind the letters
    b.box((-w - .1, sy + .14, zr + 2.35), (w + .1, sy + .24, zr + 2.47), M)
    for x in np.linspace(-w + .2, w - .2, 5):
        b.box((x - .06, sy + .14, zr + .03), (x + .06, sy + .26, zr + 2.5), M)        # uprights
    # Porte-cochere: lettering on the teal fascia.
    x0, x1 = CANOPY_X; y0 = CANOPY_Y[0]
    v, f = text_mesh('GRAND CORAL RESORT', 1.0, .07, (x1 - x0) - 2.4)
    v[:, 2] *= .5 / v[:, 2].max()
    v[:, 0] += (x0 + x1) / 2; v[:, 1] += y0 - .005; v[:, 2] += 6.78
    b.mesh(v, f, G)
    # Gold urns on the balustrade corners.
    urns = 0
    for zr, xa, xb, ya, yb in TERRACES:
        for cx, cy in ((xa, ya), (xa, yb), (xb, ya), (xb, yb)):
            if not any(abs(px - cx) < .4 and abs(py - cy) < .4 and pz == zr for px, py, pz in POSTS): continue
            ux, uy = cx + (.15 if cx == xa else -.15), cy + (.15 if cy == ya else -.15)
            b.box((ux - .17, uy - .17, zr + 1.12), (ux + .17, uy + .17, zr + 1.2), G)  # plinth
            for z0, z1, r in ((1.2, 1.26, .1), (1.26, 1.32, .15), (1.32, 1.5, .19), (1.5, 1.56, .15),
                              (1.56, 1.62, .08), (1.62, 1.67, .13), (1.67, 1.75, .06), (1.75, 1.83, .04)):
                b.cylinder(ux, uy, zr + z0, zr + z1, r, G, sides=14)
            urns += 1
    b.append_to(details)
    log('gold dressing: rooftop sign, canopy lettering, corner urns', urns)


def finish_exterior(hotel, details, names, classes, log):
    for o in (hotel, details): ensure_materials(o.data, names, classes)
    idx = {n: i for i, n in enumerate(names)}
    rebuild_canopy(hotel, details, idx, log)
    roof_edges(hotel, details, idx, log)
    ledges(hotel, details, idx, log)
    facade_shards(hotel, details, idx, log)
    tower_balcony(hotel, details, idx, log)
    vary_glass(details, idx, log)
    gold_dressing(details, idx, log)
