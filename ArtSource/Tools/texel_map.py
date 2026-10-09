"""Where on the body each texel of a character's texture is: the mesh's triangles drawn into texture space, so each
covered texel knows its 3D position (bind pose), normal and triangle. Used to clean textures in 3D, across UV seams."""
import numpy as np


def rasterize(uv, positions, normals, triangles, width, height):
    """Returns (covered bool HxW, position HxWx3, normal HxWx3, triangle id HxW (-1 = gutter))."""
    pos = np.zeros((height, width, 3), np.float32)
    nrm = np.zeros((height, width, 3), np.float32)
    tri_id = np.full((height, width), -1, np.int32)
    px = uv[:, 0] * width - 0.5
    py = uv[:, 1] * height - 0.5
    for t, (a, b, c) in enumerate(triangles):
        xa, ya, xb, yb, xc, yc = px[a], py[a], px[b], py[b], px[c], py[c]
        x0 = max(0, int(np.floor(min(xa, xb, xc))))
        x1 = min(width - 1, int(np.ceil(max(xa, xb, xc))))
        y0 = max(0, int(np.floor(min(ya, yb, yc))))
        y1 = min(height - 1, int(np.ceil(max(ya, yb, yc))))
        if x1 < x0 or y1 < y0:
            continue
        area = (xb - xa) * (yc - ya) - (xc - xa) * (yb - ya)
        if abs(area) < 1e-9:
            continue
        gx, gy = np.meshgrid(np.arange(x0, x1 + 1, dtype=np.float32), np.arange(y0, y1 + 1, dtype=np.float32))
        dx, dy = gx - xa, gy - ya
        wb = (dx * (yc - ya) - (xc - xa) * dy) / area
        wc = ((xb - xa) * dy - dx * (yb - ya)) / area
        wa = 1.0 - wb - wc
        eps = -1e-4
        inside = (wa >= eps) & (wb >= eps) & (wc >= eps)
        if not inside.any():
            continue
        ys, xs = np.nonzero(inside)
        wa_, wb_, wc_ = wa[inside][:, None], wb[inside][:, None], wc[inside][:, None]
        pos[ys + y0, xs + x0] = positions[a] * wa_ + positions[b] * wb_ + positions[c] * wc_
        nrm[ys + y0, xs + x0] = normals[a] * wa_ + normals[b] * wb_ + normals[c] * wc_
        tri_id[ys + y0, xs + x0] = t
    covered = tri_id >= 0
    n = np.linalg.norm(nrm, axis=2, keepdims=True)
    nrm = np.where(n > 1e-6, nrm / np.maximum(n, 1e-6), 0)
    return covered, pos, nrm, tri_id


def conservative(covered, pos, nrm, tri_id, uv, positions, normals, triangles, width, height):
    """Texels a triangle only touches (its edge passes through them without covering the centre) are sampled by the
    GPU too: give them the nearest covered neighbour's surface point, so nothing at an island's rim is left out."""
    for _ in range(2):
        grow = ~covered
        nb = np.zeros_like(covered)
        src = np.full(covered.shape + (2,), -1, np.int32)
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
            shifted = np.roll(np.roll(covered, dy, 0), dx, 1)
            take = grow & shifted & ~nb
            ys, xs = np.nonzero(take)
            src[ys, xs, 0] = (ys - dy) % covered.shape[0]
            src[ys, xs, 1] = (xs - dx) % covered.shape[1]
            nb |= take
        ys, xs = np.nonzero(nb)
        sy, sx = src[ys, xs, 0], src[ys, xs, 1]
        pos[ys, xs] = pos[sy, sx]
        nrm[ys, xs] = nrm[sy, sx]
        tri_id[ys, xs] = tri_id[sy, sx]
        covered = covered | nb
    return covered, pos, nrm, tri_id
