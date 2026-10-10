"""Closes the blank parts of the polished hotel (user, 10 October 2026).

- Back ground floor: the AI facade there was cut away with the lobby, leaving the plain ivory lobby box showing
  ("the stupid concrete thing"). A plaster base storey with tall windows, a glass back door and a cornice goes in front.
- Back slots between the tower and the wings (2.3 m wide, 1 m deep, the whole height): the AI's stair-like inner
  geometry showed through them. The junk goes and each slot is filled flush with the wing wall, a window per floor.
- Side slots (3 m wide, 0.6 m deep) with a hole through the building and the white lobby box behind it at the
  ground floor: same treatment.

Blender coordinates (Z up): hotel-local Unity x = x, y = z, z = -y - 4. The back of the building is +y.
Runs on each exported LOD after the reception cut (build_realistic_hotel.main).
"""
from hotel_exterior import Builder, delete_in_box

BACK_FACE = 14.9        # outer face of the new back base storey (the lobby box's outer face is at y 14.7)
BASE_TOP = 4.0          # top of the base storey's wall; cornice above
WING_FACE = 12.77       # back wall of the wings (window glass sits at 12.74)
FLOOR_WINDOWS = [(5.4, 7.4), (8.6, 10.6), (11.8, 13.7), (15.0, 16.9), (18.1, 20.1), (21.2, 23.1)]
BACK_SLOTS = [(-8.45, -6.15), (6.1, 8.45)]
SIDE_X = 23.9           # outer face of the side walls
SIDE_SLOT = (3.4, 6.6)  # y range of the side slots
SIDE_HOLE = (3.7, 6.7)  # y range of the ground-floor hole through the side (white lobby box showed through)
# Window centres of the upper back floors, repeated on the base storey.
BASE_WINDOWS = [-21.9, -19.2, -16.6, -14.05, -11.95, -10.2, -4.35, 4.4, 10.35, 11.95, 14.05, 16.6, 19.2, 21.9]


def window_y(b, idx, cx, face_y, z0, z1, w, out=1):
    """A window on a wall facing +y (out=1) or -y (out=-1): white frame, glass, stone sill."""
    G, WH, S = idx['glass'], idx['white'], idx['stone']
    y0, y1 = sorted((face_y, face_y + out * .06))
    b.box((cx - w / 2, y0, z0), (cx + w / 2, y1, z1), G)
    fy0, fy1 = sorted((face_y, face_y + out * .1))
    for x0, x1 in ((cx - w / 2 - .09, cx - w / 2), (cx + w / 2, cx + w / 2 + .09)):
        b.box((x0, fy0, z0 - .02), (x1, fy1, z1 + .09), WH)
    b.box((cx - w / 2, fy0, z1), (cx + w / 2, fy1, z1 + .09), WH)
    b.box((cx - w / 2, fy0, z0 - .04), (cx + w / 2, fy1, z0), WH)
    if w < 2:
        sy0, sy1 = sorted((face_y, face_y + out * .2))
        b.box((cx - w / 2 - .14, sy0, z0 - .12), (cx + w / 2 + .14, sy1, z0 - .02), S)
    else:
        b.box((cx - .03, y0, z0), (cx + .03, fy1, z1), WH)                      # centre mullion


def window_x(b, idx, cy, face_x, z0, z1, w, out):
    """A window on a wall facing +x (out=1) or -x (out=-1)."""
    G, WH, S = idx['glass'], idx['white'], idx['stone']
    x0, x1 = sorted((face_x, face_x + out * .06))
    b.box((x0, cy - w / 2, z0), (x1, cy + w / 2, z1), G)
    fx0, fx1 = sorted((face_x, face_x + out * .1))
    for y0, y1 in ((cy - w / 2 - .09, cy - w / 2), (cy + w / 2, cy + w / 2 + .09)):
        b.box((fx0, y0, z0 - .02), (fx1, y1, z1 + .09), WH)
    b.box((fx0, cy - w / 2, z1), (fx1, cy + w / 2, z1 + .09), WH)
    b.box((fx0, cy - w / 2, z0 - .04), (fx1, cy + w / 2, z0), WH)
    sx0, sx1 = sorted((face_x, face_x + out * .2))
    b.box((sx0, cy - w / 2 - .14, z0 - .12), (sx1, cy + w / 2 + .14, z0 - .02), S)


def fill_blanks(lod, details, names, log):
    idx = {n: i for i, n in enumerate(names)}
    W, T = idx['wall'], idx['trim']
    removed = 0
    # Inner junk in the slots and the scraps of the cut-away back ground floor.
    for x0, x1 in BACK_SLOTS:
        removed += sum(delete_in_box(o, (x0 + .08, 11.85, 3.9), (x1 - .08, 13.95, 24.0)) for o in (lod, details))
    for s in (-1, 1):
        lo, hi = sorted((s * 23.0, s * 24.05))
        removed += sum(delete_in_box(o, (lo, SIDE_SLOT[0] + .1, 0), (hi, SIDE_SLOT[1] - .1, 23.6)) for o in (lod, details))
    removed += sum(delete_in_box(o, (-24.2, BACK_FACE - .3, -.5), (24.2, 15.6, BASE_TOP + .3)) for o in (lod, details))

    b = Builder()
    # Back base storey: plaster wall, returns round the corners, cornice and a flat cap back to the wings.
    b.box((-SIDE_X - .05, BACK_FACE - .18, 0), (SIDE_X + .05, BACK_FACE, BASE_TOP), W)
    for s in (-1, 1):
        lo, hi = sorted((s * (SIDE_X - .3), s * (SIDE_X + .05)))
        b.box((lo, 12.4, 0), (hi, BACK_FACE, BASE_TOP), W)
    b.box((-SIDE_X - .2, 12.4, BASE_TOP), (SIDE_X + .2, BACK_FACE + .22, BASE_TOP + .32), T)       # cornice + cap
    b.box((-SIDE_X - .1, BACK_FACE - .02, .0), (SIDE_X + .1, BACK_FACE + .12, .35), idx['stone'])  # plinth
    for cx in BASE_WINDOWS:
        window_y(b, idx, cx, BACK_FACE, .75, 3.45, 1.5)
    window_y(b, idx, 0, BACK_FACE, .35, 3.1, 2.4)                                          # glass back door
    b.box((-1.5, BACK_FACE, 3.1), (1.5, BACK_FACE + .16, 3.35), T)                           # door head

    # Back slots: filled flush with the wing wall, one window per floor.
    for x0, x1 in BACK_SLOTS:
        b.box((x0, 11.7, 3.9), (x1, WING_FACE, 24.0), W)
        for z0, z1 in FLOOR_WINDOWS:
            window_y(b, idx, (x0 + x1) / 2, WING_FACE, z0, z1, 1.2)
        for z0, _ in FLOOR_WINDOWS:                                                           # floor bands
            b.box((x0, WING_FACE, z0 - .55), (x1, WING_FACE + .08, z0 - .4), T)

    # Side slots and the ground-floor hole through the building: plaster flush with the side walls.
    for s in (-1, 1):
        lo, hi = sorted((s * (SIDE_X - .75), s * SIDE_X))
        b.box((lo, SIDE_SLOT[0], 4.0), (hi, SIDE_SLOT[1], 23.7), W)
        hlo, hhi = sorted((s * (SIDE_X - .75), s * (SIDE_X + .03)))
        b.box((hlo, SIDE_HOLE[0], 0), (hhi, SIDE_HOLE[1], 4.05), W)
        cy = sum(SIDE_SLOT) / 2
        for z0, z1 in FLOOR_WINDOWS:
            window_x(b, idx, cy, s * SIDE_X, z0, z1, 1.2, s)
        window_x(b, idx, sum(SIDE_HOLE) / 2, s * (SIDE_X + .03), 1.1, 3.6, 1.0, s)

    b.append_to(details)
    log('filled blanks: back base storey, back slots, side slots; junk faces removed', removed)
    return removed
