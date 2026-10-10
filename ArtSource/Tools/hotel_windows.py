"""Clean windows for the polished Tripo hotel (used by build_realistic_hotel.py).

Finds every window (glass on a vertical wall), regularises it to its facade's grid, deletes the crumpled AI pane and
its jagged reveal, and builds a clean window instead: flat glass, frame, mullions, sill, a clean reveal lining and,
where the facade has one, a teal surround.
"""
import bmesh
import bpy
import json
from pathlib import Path
import numpy as np
from mathutils import Vector


def face_table(me):
    nf = len(me.polygons)

    def get(attr, n, dt=float):
        a = np.empty(nf * n, dtype=dt); me.polygons.foreach_get(attr, a); return a
    centre = get('center', 3).reshape(-1, 3); normal = get('normal', 3).reshape(-1, 3)
    area = get('area', 1); mats = get('material_index', 1, np.int64); starts = get('loop_start', 1, np.int64)
    vidx = np.empty(len(me.loops), dtype=np.int64); me.loops.foreach_get('vertex_index', vidx)
    eidx = np.empty(len(me.loops), dtype=np.int64); me.loops.foreach_get('edge_index', eidx)
    co = np.empty(len(me.vertices) * 3); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
    return centre, normal, area, mats, vidx[starts[:, None] + np.arange(3)], eidx[starts[:, None] + np.arange(3)], co


def components(candidate, key, fedges):
    """Connected components (shared edges) among candidate faces with equal key; label per face (-1 = none)."""
    ids = np.nonzero(candidate)[0]
    e = fedges[ids].ravel(); f = np.repeat(ids, 3)
    order = np.argsort(e, kind='stable'); e, f = e[order], f[order]
    same = e[1:] == e[:-1]
    a, b = f[:-1][same], f[1:][same]
    keep = key[a] == key[b]; a, b = a[keep], b[keep]
    label = np.full(len(candidate), -1); label[ids] = ids
    for _ in range(1000):
        m = np.minimum(label[a], label[b])
        if not ((label[a] != m) | (label[b] != m)).any(): break
        np.minimum.at(label, a, m); np.minimum.at(label, b, m)
        label[ids] = label[label[ids]]
    return label


def cluster_values(values, tol):
    """Each value becomes the median of its cluster (sorted; gaps > tol split clusters)."""
    if len(values) == 0: return values
    order = np.argsort(values); v = values[order]
    group = np.concatenate([[0], np.cumsum(np.diff(v) > tol)])
    med = np.array([np.median(v[group == g]) for g in range(group.max() + 1)])
    out = np.empty_like(values); out[order] = med[group]
    return out


def find_windows(centre, normal, area, mats, fverts, fedges, co, GLASS, log):
    rows = np.arange(len(normal))
    horiz = np.abs(normal[:, :2]).argmax(1)                         # 0: faces +-x, 1: faces +-y
    aligned = np.abs(normal[rows, horiz]) > .85
    sign = np.sign(normal[rows, horiz])
    key = horiz * 2 + (sign > 0)
    label = components((mats == GLASS) & aligned, key, fedges)
    wins = []
    for lab in np.unique(label[label >= 0]):
        f = np.nonzero(label == lab)[0]
        if area[f].sum() < .05: continue
        a = horiz[f[0]]; u = 1 - a; sg = sign[f[0]]
        vs = co[np.unique(fverts[f])]
        order = np.argsort(centre[f, a]); cum = np.cumsum(area[f][order])
        plane = centre[f, a][order][np.searchsorted(cum, cum[-1] / 2)]
        wins.append([a, sg, plane, *np.percentile(vs[:, u], [1, 99]), *np.percentile(vs[:, 2], [1, 99])])
    wins = np.array(wins)
    log('glass components', len(wins))
    # Panes split by an AI mullion are one window: merge touching rectangles on the same plane.
    changed = True
    while changed:
        changed = False
        alive = np.ones(len(wins), dtype=bool)
        for i in range(len(wins)):
            if not alive[i]: continue
            w = wins[i]
            hit = alive & (wins[:, 0] == w[0]) & (wins[:, 1] == w[1]) & (np.abs(wins[:, 2] - w[2]) < .35) & \
                (wins[:, 3] < w[4] + .3) & (wins[:, 4] > w[3] - .3) & (wins[:, 5] < w[6] + .3) & (wins[:, 6] > w[5] - .3)
            if hit.sum() > 1:
                o = wins[hit]
                wins[i] = [w[0], w[1], np.median(o[:, 2]), o[:, 3].min(), o[:, 4].max(), o[:, 5].min(), o[:, 6].max()]
                hit[i] = False; alive &= ~hit; changed = True
        wins = wins[alive]
    w, h = wins[:, 4] - wins[:, 3], wins[:, 6] - wins[:, 5]
    # Thin dark seams below roof caps were being mistaken for glazing.
    wins = wins[(w > .7) & (w < 9) & (h > 1.3) & (h < 4.6)]
    # One grid per facade: shared sill/head heights per floor and shared jambs per column.
    fk = wins[:, 0] * 2 + (wins[:, 1] > 0)
    for k in np.unique(fk):
        sel = np.nonzero(fk == k)[0]
        for col, tol in ((2, .5), (3, .16), (4, .16), (5, .16), (6, .16)):
            wins[sel, col] = cluster_values(wins[sel, col], tol)
    log('windows', len(wins))
    return wins, horiz, aligned, sign, key


def find_rails(centre, normal, area, mats, fverts, fedges, co, rail_mats, remove, log):
    """Balcony and roof railings, found by their caps: long thin horizontal teal or grey-metal pieces. The lumpy AI
    cap and balusters under it are marked for removal; returns clean replacements as (cap box, cap material, slab z)."""
    rails, balconies = [], []
    up = normal[:, 2] > .9
    for m in rail_mats:
        label = components((mats == m) & ~remove, np.zeros(len(mats), dtype=np.int64), fedges)
        for lab in np.unique(label[label >= 0]):
            f = np.nonzero(label == lab)[0]
            vs = co[np.unique(fverts[f])]
            lo, hi = np.percentile(vs, 2, axis=0), np.percentile(vs, 98, axis=0)
            size = hi - lo
            la = int(np.argmax(size[:2])); sa = 1 - la
            if size[la] < 1.2 or size[2] > .35 or size[sa] > .45: continue
            top, mid = hi[2], (lo[sa] + hi[sa]) / 2
            a = [0.0, 0.0, top - .07]; b = [0.0, 0.0, top]
            a[la], b[la] = lo[la] - .02, hi[la] + .02
            a[sa], b[sa] = mid - .045, mid + .045
            remove[f] = True
            # The floor it stands on: the highest up-facing surface under the cap, on the building side.
            c = centre
            floor = up & (c[:, la] > lo[la]) & (c[:, la] < hi[la]) & (np.abs(c[:, sa] - mid) < .7) &                 (c[:, 2] > top - 1.4) & (c[:, 2] < top - .45)
            if floor.sum() <= 2:
                rails.append((a, b, m, None, la)); continue                          # roof rail: cap only
            slab = float(np.percentile(c[floor, 2], 90))
            inward = np.sign(np.mean(c[floor, sa]) - mid) or 1.0
            sg = -inward                                                             # outward
            front = mid * sg + .05
            d = c[:, sa] * sg
            deck = up & (c[:, la] > lo[la] - 3.5) & (c[:, la] < hi[la] + 3.5) & (d < front + .1) & (d > front - 2.6) &                 (np.abs(c[:, 2] - slab) < .12)
            if deck.sum() > 2:                    # keep the floor that is continuous with the cap's own stretch
                bins = np.floor(c[deck, la] / .25).astype(int); have = set(bins.tolist())
                k0 = int(np.floor((lo[la] + hi[la]) / 2 / .25)); lo_k = hi_k = k0
                while lo_k - 1 in have or lo_k - 2 in have: lo_k -= 1
                while hi_k + 1 in have or hi_k + 2 in have: hi_k += 1
                deck &= (c[:, la] >= lo_k * .25 - .01) & (c[:, la] <= hi_k * .25 + .26)
            swall = float(np.percentile(d[deck], 3)) - .05 if deck.sum() > 2 else front - 1.5
            swall = min(max(swall, front - 2.3), front - .7)
            ul, ur = lo[la], hi[la]
            if deck.sum() > 2:                                                       # full width of its floor
                ul = min(ul, float(np.percentile(c[deck, la], 2))); ur = max(ur, float(np.percentile(c[deck, la], 98)))
            balconies.append((sa, sg, slab, swall, front, ul, ur, m, top))
    log('roof rails', len(rails), 'railed balconies', len(balconies))
    return rails, balconies


def clear_balcony(centre, normal, remove, a, sg, zs, swall, front, ul, ur):
    """Everything the AI model had for this balcony: balusters, side rails, posts and the slab itself."""
    u = 1 - a
    d = centre[:, a] * sg; cu = centre[:, u]; cz = centre[:, 2]
    rail_z = (cz > zs + .03) & (cz < zs + 1.3)
    front_zone = rail_z & (d > front - .45) & (d < front + .15) & (cu > ul - .2) & (cu < ur + .2)
    side_zone = rail_z & (d > swall + .1) & (d < front + .15) & ((np.abs(cu - ul) < .35) | (np.abs(cu - ur) < .35))
    slab = (d > swall + .05) & (d < front + .3) & (cu > ul - .15) & (cu < ur + .15) & (cz > zs - 1.0) & (cz < zs + .04)
    remove |= front_zone | side_zone | slab


def find_balconies(built, rails, centre, normal, area_, fmin, fmax, remove, log):
    """Balconies in front of windows: an up-facing slab ~0.5-2.5 m out at sill height. Their lumpy AI balusters are
    marked for removal; returns (axis, sign, slab z, wall depth, front depth, u0, u1) for a clean balustrade."""
    up = normal[:, 2] > .9
    found = []
    for a, sg, sglass, swall, u0, u1, z0, z1, teal in built:
        u = 1 - a
        if z0 < 2.5: continue                                       # no balconies on the ground floor
        depth_c = centre[:, a] * sg
        slab = up & (centre[:, u] > u0 - 1.0) & (centre[:, u] < u1 + 1.0) & (centre[:, 2] > z0 - .45) &             (centre[:, 2] < z0 + .2) & (depth_c > swall + .25) & (depth_c < swall + 3.0)
        dbg = __import__('os').environ.get('HOTEL_DEBUG') and a == 1 and sg < 0 and -7 < u0 < 0
        if area_[slab].sum() < 1.0:
            if dbg: print('[BalDbg] no slab', round(u0, 2), round(u1, 2), round(z0, 2), round(float(area_[slab].sum()), 2))
            continue
        zs = np.median(centre[slab, 2])
        dmax = np.where(sg > 0, fmax[:, a], -fmin[:, a])
        front = np.percentile(dmax[slab], 95)
        if not .8 < front - swall < 2.9:
            if dbg: print('[BalDbg] depth', round(u0, 2), round(z0, 2), round(front - swall, 2))
            continue
        lo_u = max(np.percentile(fmin[slab, u], 3), u0 - 1.0); hi_u = min(np.percentile(fmax[slab, u], 97), u1 + 1.0)
        # A real balcony has balusters on its front edge in the AI model; a cornice ledge doesn't.
        posts = ~up & (np.abs(normal[:, 2]) < .5) & (centre[:, 2] > zs + .2) & (centre[:, 2] < zs + .9) &             (depth_c > front - .45) & (depth_c < front + .1) & (centre[:, u] > lo_u) & (centre[:, u] < hi_u)
        if area_[posts].sum() < .25 * (hi_u - lo_u):
            if dbg: print('[BalDbg] posts', round(u0, 2), round(z0, 2), round(float(area_[posts].sum()), 2), round(hi_u - lo_u, 2))
            continue
        if dbg: print('[BalDbg] OK', round(u0, 2), round(z0, 2), round(lo_u, 2), round(hi_u, 2))
        found.append([a, sg, zs, swall, front, lo_u, hi_u])
    # One balcony may front several windows: merge overlapping ones.
    found.sort(key=lambda b: (b[0], b[1], round(b[2], 0), b[5]))
    merged = []
    for b in found:
        m = merged[-1] if merged else None
        if m and m[0] == b[0] and m[1] == b[1] and abs(m[2] - b[2]) < .3 and b[5] < m[6] - .2 and abs(m[4] - b[4]) < .3:
            m[5], m[6], m[4] = min(m[5], b[5]), max(m[6], b[6]), max(m[4], b[4])
        else: merged.append(list(b))
    out = []
    for a, sg, zs, swall, front, ul, ur in merged:
        u = 1 - a
        # Already found by its coloured rail?
        if any(r[0] == a and r[1] == sg and abs(r[2] - zs) < .4 and r[5] < ur and r[6] > ul for r in rails): continue
        out.append((a, sg, zs, swall, front, ul, ur, None, zs + 1.03))
    log('balconies from windows', len(out))
    return out


def rebuild_windows(obj, names, log):
    GLASS, WALL, TEAL, WHITE, TRIM, ROOF, GOLD = (names.index(n) for n in ('glass', 'wall', 'teal', 'white', 'trim', 'roof', 'gold'))
    me = obj.data
    centre, normal, area, mats, fverts, fedges, co = face_table(me)
    wins, horiz, aligned, sign, key = find_windows(centre, normal, area, mats, fverts, fedges, co, GLASS, log)
    signed = centre[np.arange(len(centre)), horiz] * sign
    fco = co[fverts]                                  # (faces, 3 verts, xyz): face extents, for slivers
    fmin, fmax = fco.min(1), fco.max(1)
    solid = aligned & np.isin(mats, [WALL, TEAL, TRIM, WHITE])
    remove = np.zeros(len(mats), dtype=bool)
    built = []
    for a, sg, plane, u0, u1, z0, z1 in wins:
        a = int(a); u = 1 - a; k = a * 2 + (sg > 0)
        sglass = plane * sg
        cu, cz = centre[:, u], centre[:, 2]
        near = solid & (key == k) & (signed > sglass - .05) & (signed < sglass + .9)
        band = near & (cu > u0 - .45) & (cu < u1 + .45) & (cz > z0 - .45) & (cz < z1 + .45) & \
            ~((cu > u0) & (cu < u1) & (cz > z0) & (cz < z1))
        swall = np.percentile(signed[band], 80) if band.sum() > 3 else sglass + .15
        swall = max(swall, sglass + .06)
        anyband = (key == k) & (signed > sglass - .1) & (signed < swall + .2) & (cu > u0 - .45) & (cu < u1 + .45) & \
            (cz > z0 - .45) & (cz < z1 + .45) & ~((cu > u0 + .05) & (cu < u1 - .05) & (cz > z0 + .05) & (cz < z1 - .05))
        teal_share = area[anyband & (mats == TEAL)].sum() / max(area[anyband & (mats != GLASS)].sum(), 1e-6)
        # Anything reaching into the opening between the glass and the wall face (long AI slivers included).
        # Only faces centred near the opening: a big wall triangle whose box merely overlaps it stays (else: a hole).
        near_c = (cu > u0 - .2) & (cu < u1 + .2) & (cz > z0 - .2) & (cz < z1 + .2)
        inside = near_c & (fmax[:, u] > u0 + .04) & (fmin[:, u] < u1 - .04) & (fmax[:, 2] > z0 + .04) & \
            (fmin[:, 2] < z1 - .04) & (signed > sglass - .6) & (signed < swall - .02) & ~(np.abs(normal[:, 2]) > .9)
        # Head/sill trim slivers hanging into the opening at the wall face.
        inside |= (cu > u0 + .03) & (cu < u1 - .03) & (cz > z0 + .03) & (cz < z1 - .03) & \
            (signed > sglass - .6) & (signed < swall + .3) & (area < .5)
        # All of the old pane, tilted shards included (whatever way they face).
        depth = centre[:, a] * sg
        pane = (mats == GLASS) & (fmax[:, u] > u0 - .06) & (fmin[:, u] < u1 + .06) & (fmax[:, 2] > z0 - .06) & \
            (fmin[:, 2] < z1 + .06) & (np.abs(depth - sglass) < .45)
        remove |= inside | pane
        if teal_share > .18:
            remove |= anyband & (mats == TEAL) & (cu > u0 - .4) & (cu < u1 + .4) & (cz > z0 - .4) & (cz < z1 + .4)
        built.append((a, sg, sglass, swall, u0, u1, z0, z1, teal_share > .18))
    log('teal surrounds', sum(b[-1] for b in built), 'faces replaced', int(remove.sum()))
    # All windows in a plane share one wall depth. Individual noisy percentiles had
    # produced overlapping panels which looked like torn seams at close range.
    for axis in (0, 1):
        for sg in (-1, 1):
            ids = [i for i, b in enumerate(built) if b[0] == axis and b[1] == sg]
            planes = cluster_values(np.array([built[i][2] for i in ids]), .35)
            for plane in np.unique(planes):
                group = [ids[j] for j in np.nonzero(planes == plane)[0]]
                depth = float(np.median([built[i][3] for i in group]))
                for i in group:
                    b = list(built[i]); b[3] = depth; built[i] = tuple(b)
    # The old panes sit inside a noisy recess. Put the replacement glass just behind a
    # regular wall face, and repair the surrounding reveal instead of leaving its AI shards.
    # The source footprint, tower heights, balconies and canopy remain the original model.
    for a, sg, sglass, swall, u0, u1, z0, z1, teal in built:
        u = 1 - a
        d = centre[:, a] * sg
        patch = (centre[:, u] > u0 - .72) & (centre[:, u] < u1 + .72) & \
                (centre[:, 2] > z0 - .7) & (centre[:, 2] < z1 + .7) & \
                (d > sglass - .55) & (d < swall + .35)
        # Leave a cornice/slab which continues beyond the repaired bay intact.
        local = (fmin[:, u] > u0 - .75) & (fmax[:, u] < u1 + .75) & \
                (fmin[:, 2] > z0 - .73) & (fmax[:, 2] < z1 + .73)
        remove |= patch & local
    # Straighten the surviving render beside the repaired bays onto the same plane.
    # This edits the existing wall vertices rather than replacing the building's shape.
    targets = np.zeros_like(co); weights = np.zeros_like(co)
    for a, sg, sglass, swall, u0, u1, z0, z1, teal in built:
        u = 1 - a
        d = centre[:, a] * sg
        wall = ~remove & (np.abs(normal[:, a]) > .65) & (mats == WALL) & \
               (centre[:, u] > u0 - .95) & (centre[:, u] < u1 + .95) & \
               (centre[:, 2] > z0 - .95) & (centre[:, 2] < z1 + .95) & \
               (d > swall - .35) & (d < swall + .35)
        ids = np.unique(fverts[wall])
        targets[ids, a] += swall * sg; weights[ids, a] += 1
    has = weights > 0
    co[has] = targets[has] / weights[has]
    me.vertices.foreach_set('co', co.ravel()); me.update()
    rails, railed = find_rails(centre, normal, area, mats, fverts, fedges, co, (TEAL, ROOF), remove, log)
    balconies = railed + find_balconies(built, railed, centre, normal, area, fmin, fmax, remove, log)
    # Rail caps often come in pieces: one balcony each, spanning all its pieces.
    balconies.sort(key=lambda b: (b[0], b[1], round(b[2] * 2), b[5]))
    joined = []
    for bal in balconies:
        j = joined[-1] if joined else None
        if j and j[0] == bal[0] and j[1] == bal[1] and abs(j[2] - bal[2]) < .3 and bal[5] < j[6] + .8 and abs(j[4] - bal[4]) < .45:
            j[3] = min(j[3], bal[3]); j[4] = max(j[4], bal[4]); j[6] = max(j[6], bal[6]); j[8] = max(j[8], bal[8])
            if j[7] is None: j[7] = bal[7]
        else: joined.append(list(bal))
    balconies = [tuple(j) for j in joined]
    log('balconies after joining pieces', len(balconies))
    for bal in balconies: clear_balcony(centre, normal, remove, *bal[:7])
    # Glass the window finder couldn't place (shards behind balusters): solid wall render, not crumpled glass.
    stray = (mats == GLASS) & ~remove
    mats_new = mats.copy(); mats_new[stray] = WALL
    me.polygons.foreach_set('material_index', mats_new.astype(np.int32))
    log('stray glass faces to render', int(stray.sum()))
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.faces[i] for i in np.nonzero(remove)[0]], context='FACES')
    bm.to_mesh(me); bm.free(); me.update()

    verts, faces, fmat, fcentre = [], [], [], []

    def box(a, sg, s0, s1, u0, u1, z0, z1, m):
        """Box in facade coordinates: signed depth s (outward), along-wall u, height z. No back face (it's in the wall)."""
        u = 1 - a; base = len(verts)
        for s_ in (s0, s1):
            for uu in (u0, u1):
                for zz in (z0, z1):
                    p = [0.0, 0.0, zz]; p[a] = s_ * sg; p[u] = uu; verts.append(p)
        mid = np.mean(verts[base:], axis=0)
        for q in ((4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)):
            faces.append([base + i for i in q]); fmat.append(m); fcentre.append(mid)

    def box_xyz(lo, hi, m):
        base = len(verts)
        for x in (lo[0], hi[0]):
            for y in (lo[1], hi[1]):
                for z in (lo[2], hi[2]): verts.append([x, y, z])
        mid = (np.array(lo) + np.array(hi)) / 2
        for q in ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)):
            faces.append([base + i for i in q]); fmat.append(m); fcentre.append(mid)

    for a, sg, sglass, swall, u0, u1, z0, z1, teal in built:
        w, h = u1 - u0, z1 - z0
        surround = TEAL if teal else WALL
        # Continuous planar backing around the opening closes holes left by long
        # source triangles. The frame stays recessed by 6 cm, with a straight reveal.
        old_glass = sglass
        sglass = swall - .06
        L = .12
        outer = .76
        for b in ((u0 - outer, u0, z0 - .75, z1 + .75),
                  (u1, u1 + outer, z0 - .75, z1 + .75),
                  (u0, u1, z0 - .75, z0), (u0, u1, z1, z1 + .75)):
            box(a, sg, swall - .07, swall + .012, *b, WALL)
        box(a, sg, sglass - .03, sglass, u0, u1, z0, z1, GLASS)                                  # flat pane
        f = .06
        for b in ((u0 - .01, u0 + f, z0, z1), (u1 - f, u1 + .01, z0, z1), (u0, u1, z0 - .01, z0 + f), (u0, u1, z1 - f, z1 + .01)):
            box(a, sg, sglass, sglass + .05, *b, WHITE)                                           # frame
        panes = max(1, int(round(w / .95)))
        for i in range(1, panes):
            x = u0 + w * i / panes
            box(a, sg, sglass, sglass + .045, x - .025, x + .025, z0, z1, WHITE)                  # mullions
        if h > 2.3:
            zt = z0 + h * .78
            box(a, sg, sglass, sglass + .045, u0, u1, zt - .025, zt + .025, WHITE)                # transom
        L = .12                                                                                  # reveal lining
        for b in ((u0 - L, u0, z0 - L, z1 + L), (u1, u1 + L, z0 - L, z1 + L), (u0, u1, z1, z1 + L), (u0, u1, z0 - L, z0)):
            box(a, sg, sglass - .02, swall + .03, *b, surround)
        box(a, sg, sglass, swall + .07, u0 - .16, u1 + .16, z0 - .07, z0 + .005, WHITE)          # sill
        if teal:
            T = .24
            for b in ((u0 - L - T, u0 - L, z0 - L - T, z1 + L + T), (u1 + L, u1 + L + T, z0 - L - T, z1 + L + T),
                      (u0 - L, u1 + L, z1 + L, z1 + L + T), (u0 - L, u1 + L, z0 - L - T, z0 - L)):
                box(a, sg, swall - .03, swall + .04, *b, TEAL)                                   # teal surround
            g = .03                                                                              # gold fillet
            for b in ((u0 - L - g, u0 - L, z0 - L - g, z1 + L + g), (u1 + L, u1 + L + g, z0 - L - g, z1 + L + g),
                      (u0 - L, u1 + L, z1 + L, z1 + L + g), (u0 - L, u1 + L, z0 - L - g, z0 - L)):
                box(a, sg, swall - .03, swall + .046, *b, GOLD)
    for a, sg, zs, swall, front, ul, ur, capmat, captop in balconies:                          # clean balconies
        u = 1 - a
        def rail_box(d0, d1, u_0, u_1, z_0, z_1, mat):
            p, q = [0.0, 0.0, z_0], [0.0, 0.0, z_1]
            p[a], q[a] = min(d0 * sg, d1 * sg), max(d0 * sg, d1 * sg); p[u], q[u] = u_0, u_1
            box_xyz(p, q, mat)
        cap = WHITE if capmat is None else capmat
        rail_top = min(max(captop, zs + .95), zs + 1.15)
        rail_box(swall - .1, front + .06, ul - .03, ur + .03, zs - .42, zs, WALL)               # one clean slab
        rail_box(front + .06, front + .1, ul - .05, ur + .05, zs - .16, zs - .06, WHITE)        # drip moulding
        rail_box(front - .14, front - .03, ul + .02, ur - .02, rail_top - .08, rail_top, cap)   # front handrail
        rail_box(front - .15, front - .02, ul + .02, ur - .02, rail_top, rail_top + .03, GOLD)    # gold cap
        rail_box(front + .06, front + .075, ul - .03, ur + .03, zs - .055, zs - .025, GOLD)     # gold slab line
        rail_box(front - .13, front - .05, ul + .02, ur - .02, zs, zs + .1, WHITE)              # bottom rail
        n = max(2, int((ur - ul - .4) / .18))
        for i in range(n + 1):
            x = ul + .22 + (ur - ul - .44) * i / n
            rail_box(front - .12, front - .06, x - .025, x + .025, zs + .1, rail_top - .08, WHITE)   # balusters
        for side in (ul + .02, ur - .18):
            rail_box(front - .2, front - .02, side, side + .16, zs, rail_top + .04, WHITE)      # corner posts
            rail_box(front - .17, front - .05, side + .02, side + .14, rail_top + .04, rail_top + .13, GOLD)  # finial
            rail_box(swall + .02, front - .2, side + .04, side + .12, rail_top - .08, rail_top, cap)  # side rails
            rail_box(swall + .02, front - .2, side + .05, side + .11, zs, zs + .1, WHITE)
            k = max(1, int((front - swall - .3) / .18))
            for i in range(k):
                d = swall + .15 + (front - swall - .4) * i / k
                rail_box(d, d + .05, side + .055, side + .105, zs + .1, rail_top - .08, WHITE)
    for lo, hi, m, slab, la in rails:                                                            # roof rails
        box_xyz(lo, hi, m)
    mesh = bpy.data.meshes.new('HotelWindows')
    mesh.from_pydata(verts, [], faces)
    for m in me.materials: mesh.materials.append(m)
    mesh.polygons.foreach_set('material_index', np.array(fmat, dtype=np.int32))
    bm = bmesh.new(); bm.from_mesh(mesh); bm.faces.ensure_lookup_table()
    bm.normal_update()
    for face, mid in zip(bm.faces, fcentre):                 # every box face points away from its box
        if face.normal.dot(face.calc_center_median() - Vector(mid)) < 0:
            face.normal_flip()
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bm.to_mesh(mesh); bm.free(); mesh.update()
    details = bpy.data.objects.new('HotelWindows', mesh); bpy.context.collection.objects.link(details)
    Path(__file__).resolve().parents[2].joinpath('Logs/hotel-window-repairs.json').write_text(
        json.dumps({'windows': built, 'railings': rails, 'balconies': balconies,
                    'removed_source_faces': int(remove.sum())}, indent=2,
                   default=lambda value: value.item() if hasattr(value, 'item') else list(value)), encoding='utf-8')
    log('window geometry tris', len(mesh.polygons), 'windows', len(built))
    return details, len(built)
