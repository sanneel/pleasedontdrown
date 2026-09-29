"""
Measures the Meshy gun models the way the game fits them (GameSceneBuilder.Weapons AttachGunModel), so hand grips
can be placed on real surfaces: slices across the gun along its length and prints each slice's extent.

    blender -b --factory-startup -P ArtSource/Tools/measure_guns.py -- <out.json> [--preview <prefix>]

Coordinates are the gun's (Unity) space: x right, y up, z forward (metres).
"""
import bpy, sys, json
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
out = argv[0]
preview = argv[argv.index("--preview") + 1] if "--preview" in argv else None
ROOT = r"F:\GameDev\PleaseDontDrown\Assets\_Game\Art\Weapons"
FIT = {  # asset: (length, centreZ, centreY), as in AttachGunModel
    "pistol": (0.28, 0.0, -0.035),
    "rifle": (0.88, 0.065, -0.025),
    "sniper": (1.19, 0.195, 0.0),
}

result = {}
for name, (length, cz, cy) in FIT.items():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=f"{ROOT}\\{name}.glb")
    pts = []
    for o in bpy.context.scene.objects:
        if o.type != 'MESH':
            continue
        m = o.matrix_world
        for v in o.data.vertices:
            p = m @ v.co
            # Blender (z up) -> glTF (y up) -> Unity (x mirrored)
            pts.append((-p.x, p.z, -p.y))
    P = np.array(pts)
    size = P.max(0) - P.min(0)
    if size[0] >= size[2]:  # long on x: the game turns it -90 about y (x -> z)
        P = np.stack([-P[:, 2], P[:, 1], P[:, 0]], axis=1)
    P *= length / max(size[0], size[2])
    centre = (P.max(0) + P.min(0)) / 2
    P += np.array([0.0, cy, cz]) - centre

    slices = []
    for z in np.arange(P[:, 2].min(), P[:, 2].max(), 0.01):
        s = P[np.abs(P[:, 2] - z) < 0.005]
        if len(s) < 3:
            continue
        slices.append({"z": round(float(z), 3), "minX": round(float(s[:, 0].min()), 4), "maxX": round(float(s[:, 0].max()), 4),
                       "minY": round(float(s[:, 1].min()), 4), "maxY": round(float(s[:, 1].max()), 4)})
    result[name] = {"min": P.min(0).round(4).tolist(), "max": P.max(0).round(4).tolist(), "slices": slices}
    print(name, "bounds", P.min(0).round(3), P.max(0).round(3))

json.dump(result, open(out, "w"), indent=1)
print("wrote", out)
