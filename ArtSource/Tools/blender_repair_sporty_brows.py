"""Restore the source brow artwork on sporty variants misclassified as iris colour.

Uses the base model's UVs and facial coordinates; geometry, rigs and outfits are unchanged.
Backups are created once so reruns do not accumulate colour edits.
"""
from pathlib import Path
import bpy
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / 'Assets/_Game/Art/Characters'
BACKUP = ROOT / 'ArtSource/FaceRepair/Originals'
BACKUP.mkdir(parents=True, exist_ok=True)


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(path))
    body = next(o for o in bpy.context.scene.objects if o.type == 'MESH' and o.name.startswith('Body'))
    mat = body.data.materials[0]
    node = mat.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].links[0].from_node
    return body, node


def main():
    body, node = load(ART / 'tourist_bikini_sporty.glb')
    width, height = node.image.size
    original = np.asarray(node.image.pixels[:], dtype=np.float32).reshape(height, width, 4)
    positions = np.full((height, width, 3), np.nan, dtype=np.float32)
    co = np.array([tuple(body.matrix_world @ v.co) for v in body.data.vertices])
    uv = np.array([tuple(d.uv) for d in body.data.uv_layers.active.data])
    body.data.calc_loop_triangles()
    for tri in body.data.loop_triangles:
        points = co[list(tri.vertices)]
        if points[:, 2].max() < 1.55 or points[:, 2].min() > 1.60 or points[:, 1].min() > -.05:
            continue
        a, b, c = uv[list(tri.loops)] * (width, height)
        lo = np.maximum(0, np.floor(np.minimum(np.minimum(a, b), c)).astype(int))
        hi = np.minimum((width, height), np.ceil(np.maximum(np.maximum(a, b), c)).astype(int)+1)
        xx, yy = np.meshgrid(np.arange(lo[0], hi[0])+.5, np.arange(lo[1], hi[1])+.5)
        area = (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])
        if abs(area) < 1e-8:
            continue
        wb = ((xx-a[0])*(c[1]-a[1])-(c[0]-a[0])*(yy-a[1]))/area
        wc = ((b[0]-a[0])*(yy-a[1])-(xx-a[0])*(b[1]-a[1]))/area
        wa = 1-wb-wc
        inside = (wa >= -.015) & (wb >= -.015) & (wc >= -.015)
        region = positions[lo[1]:hi[1], lo[0]:hi[0]]
        projected = wa[...,None]*points[0]+wb[...,None]*points[1]+wc[...,None]*points[2]
        region[inside] = projected[inside]
    x, y, z = (positions[..., i] for i in range(3))
    zone = (z > 1.556) & (z < 1.59) & (abs(x) > .014) & (abs(x) < .080) & (y < -.065)
    mask = zone * np.clip((.62-original[..., 0])/.15, 0, 1)
    assert (mask > .5).sum() > 100, 'Brow mask did not cover the source eyebrows.'
    # Cover texture gutters at the boundaries of split UV islands too.
    for _ in range(3):
        for dy, dx in ((1,0),(-1,0),(0,1),(0,-1)):
            mask = np.maximum(mask, np.roll(mask, (dy,dx), (0,1)) * np.isnan(x))
    import shutil
    for path in sorted(ART.glob('tourist_bikini_sporty_v*.glb')):
        backup = BACKUP / path.name
        if not backup.exists():
            shutil.copy2(path, backup)
        body, node = load(backup)
        assert tuple(node.image.size) == (width, height)
        pixels = np.asarray(node.image.pixels[:], dtype=np.float32).reshape(height,width,4)
        # Retain the original dark-brown eyebrow artwork, including its shading and tapered edge.
        pixels[..., :3] = pixels[..., :3]*(1-mask[...,None]) + original[..., :3]*mask[...,None]
        repaired = bpy.data.images.new(path.stem+'_brows_repaired', width=width, height=height)
        repaired.pixels.foreach_set(pixels.ravel())
        repaired.pack()
        node.image = repaired
        bpy.ops.export_scene.gltf(filepath=str(path), export_format='GLB', export_animations=True)
        print('BROWS_REPAIRED', path.name, int((mask>.5).sum()))


if __name__ == '__main__':
    main()
