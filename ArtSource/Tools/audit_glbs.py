"""Read every project GLB without changing it; write a machine-readable audit.

Run from the repository root: python ArtSource/Tools/audit_glbs.py
"""
import hashlib
import json
from io import BytesIO
import struct
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Docs/GLB-Polish-Audit.json'
COMPONENT = {5120: ('i1', 1), 5121: ('u1', 1), 5122: ('<i2', 2),
             5123: ('<u2', 2), 5125: ('<u4', 4), 5126: ('<f4', 4)}
WIDTH = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4,
         'MAT2': 4, 'MAT3': 9, 'MAT4': 16}


def accessor(doc, blob, number):
    acc = doc['accessors'][number]
    if 'bufferView' not in acc:
        if acc.get('sparse'):
            raise ValueError('sparse accessor needs separate inspection')
        return np.zeros((acc['count'], WIDTH[acc['type']]), dtype=np.float32)
    view = doc['bufferViews'][acc['bufferView']]
    if view.get('buffer', 0) != 0:
        raise ValueError('external buffer')
    dtype, unit = COMPONENT[acc['componentType']]
    width = WIDTH[acc['type']]
    offset = view.get('byteOffset', 0) + acc.get('byteOffset', 0)
    stride = view.get('byteStride', width * unit)
    end = offset + (acc['count'] - 1) * stride + width * unit if acc['count'] else offset
    if offset < 0 or end > len(blob) or end > view.get('byteOffset', 0) + view['byteLength']:
        raise ValueError(f'accessor {number} exceeds bufferView')
    return np.ndarray((acc['count'], width), dtype=np.dtype(dtype), buffer=blob,
                      offset=offset, strides=(stride, unit))


def inspect(path):
    raw = path.read_bytes()
    row = {'path': path.relative_to(ROOT).as_posix(), 'bytes': len(raw),
           'sha256': hashlib.sha256(raw).hexdigest(), 'issues': []}
    if len(raw) < 20 or raw[:4] != b'glTF':
        raise ValueError('not a GLB')
    magic, version, length = struct.unpack_from('<4sII', raw)
    if version != 2 or length != len(raw):
        raise ValueError(f'GLB header version={version}, declared length={length}')
    pos, chunks = 12, {}
    while pos < len(raw):
        if pos + 8 > len(raw):
            raise ValueError('truncated chunk header')
        size, kind = struct.unpack_from('<I4s', raw, pos)
        pos += 8
        if pos + size > len(raw):
            raise ValueError('truncated chunk')
        chunks[kind] = memoryview(raw)[pos:pos + size]
        pos += size
    doc = json.loads(chunks[b'JSON'].tobytes())
    blob = chunks.get(b'BIN\0', memoryview(b''))
    row.update(meshes=len(doc.get('meshes', [])), nodes=len(doc.get('nodes', [])),
               skins=len(doc.get('skins', [])), animations=len(doc.get('animations', [])),
               materials=len(doc.get('materials', [])), images=len(doc.get('images', [])),
               image_sizes=[],
               triangles=0, degenerate_triangles=0, primitives=0)
    if not doc.get('meshes'):
        row['issues'].append('no meshes')
    for i, view in enumerate(doc.get('bufferViews', [])):
        if view.get('buffer', 0) != 0 or view.get('byteOffset', 0) + view['byteLength'] > len(blob):
            row['issues'].append(f'bufferView {i} out of bounds')
    for i, img in enumerate(doc.get('images', [])):
        if 'uri' in img:
            row['issues'].append(f'image {i} external URI')
        elif 'bufferView' not in img or img['bufferView'] >= len(doc.get('bufferViews', [])):
            row['issues'].append(f'image {i} invalid bufferView')
        else:
            view = doc['bufferViews'][img['bufferView']]
            start = view.get('byteOffset', 0)
            try:
                with Image.open(BytesIO(blob[start:start + view['byteLength']].tobytes())) as picture:
                    row['image_sizes'].append(list(picture.size))
                    picture.verify()
            except Exception as exc:
                row['issues'].append(f'image {i} cannot decode: {exc}')
    for i, texture in enumerate(doc.get('textures', [])):
        source = texture.get('source', texture.get('extensions', {}).get('KHR_texture_basisu', {}).get('source'))
        if source is None or source >= row['images']:
            row['issues'].append(f'texture {i} invalid image source')
    for i, mat in enumerate(doc.get('materials', [])):
        slots = [mat.get('pbrMetallicRoughness', {}).get('baseColorTexture'),
                 mat.get('pbrMetallicRoughness', {}).get('metallicRoughnessTexture'),
                 mat.get('normalTexture'), mat.get('occlusionTexture'), mat.get('emissiveTexture')]
        for slot in slots:
            if slot and slot.get('index', -1) >= len(doc.get('textures', [])):
                row['issues'].append(f'material {i} invalid texture reference')
        if mat.get('alphaMode') not in (None, 'OPAQUE', 'MASK', 'BLEND'):
            row['issues'].append(f'material {i} invalid alpha mode')
        pbr = mat.get('pbrMetallicRoughness', {})
        for key in ('metallicFactor', 'roughnessFactor'):
            if key in pbr and not 0 <= pbr[key] <= 1:
                row['issues'].append(f'material {i} {key} outside [0,1]')
    bounds = []
    for mi, mesh in enumerate(doc.get('meshes', [])):
        for pi, primitive in enumerate(mesh.get('primitives', [])):
            row['primitives'] += 1
            attrs = primitive.get('attributes', {})
            label = f'mesh {mi} primitive {pi}'
            if 'POSITION' not in attrs:
                row['issues'].append(f'{label} missing positions')
                continue
            verts = accessor(doc, blob, attrs['POSITION']).astype(np.float64)
            if not np.isfinite(verts).all():
                row['issues'].append(f'{label} nonfinite positions')
            if len(verts):
                bounds.append((verts.min(axis=0), verts.max(axis=0)))
            if 'NORMAL' not in attrs:
                row['issues'].append(f'{label} missing normals')
            else:
                normals = accessor(doc, blob, attrs['NORMAL'])
                if len(normals) != len(verts) or not np.isfinite(normals).all():
                    row['issues'].append(f'{label} invalid normals')
            material = primitive.get('material')
            if material is not None and material >= row['materials']:
                row['issues'].append(f'{label} invalid material')
            if material is not None:
                mat = doc['materials'][material]
                textured = bool(mat.get('pbrMetallicRoughness', {}).get('baseColorTexture') or mat.get('normalTexture'))
                if textured and 'TEXCOORD_0' not in attrs:
                    row['issues'].append(f'{label} textured without UV0')
            if 'indices' in primitive:
                indices = accessor(doc, blob, primitive['indices']).reshape(-1)
            else:
                indices = np.arange(len(verts))
            if len(indices) and int(indices.max()) >= len(verts):
                row['issues'].append(f'{label} index outside positions')
                continue
            mode = primitive.get('mode', 4)
            if mode == 4:
                if len(indices) % 3:
                    row['issues'].append(f'{label} triangle index count not multiple of 3')
                faces = indices[:len(indices) // 3 * 3].reshape(-1, 3)
                row['triangles'] += len(faces)
                if len(faces):
                    p = verts[faces]
                    area = np.linalg.norm(np.cross(p[:, 1] - p[:, 0], p[:, 2] - p[:, 0]), axis=1)
                    row['degenerate_triangles'] += int(np.count_nonzero(area < 1e-12))
            else:
                row['issues'].append(f'{label} non-triangle mode {mode}; review separately')
    if bounds:
        low = np.min([x[0] for x in bounds], axis=0)
        high = np.max([x[1] for x in bounds], axis=0)
        row['local_bounds_min'] = [round(float(x), 5) for x in low]
        row['local_bounds_max'] = [round(float(x), 5) for x in high]
        row['local_size'] = [round(float(x), 5) for x in high - low]
    if row['degenerate_triangles']:
        row['issues'].append(f"{row['degenerate_triangles']} zero-area triangles")
    return row


def main():
    paths = sorted((*ROOT.glob('Assets/_Game/Art/**/*.glb'), *ROOT.glob('ArtSource/**/*.glb')))
    rows = []
    for path in paths:
        try:
            row = inspect(path)
        except Exception as exc:
            row = {'path': path.relative_to(ROOT).as_posix(), 'bytes': path.stat().st_size,
                   'issues': [f'PARSE ERROR: {exc}']}
        rows.append(row)
    duplicates = {}
    for row in rows:
        if 'sha256' in row:
            duplicates.setdefault(row['sha256'], []).append(row['path'])
    for row in rows:
        row['exact_copies'] = [p for p in duplicates.get(row.get('sha256'), []) if p != row['path']]
    OUT.write_text(json.dumps({'assets': rows}, indent=2) + '\n', encoding='utf-8')
    print(f'{len(rows)} GLBs; {sum(bool(r["issues"]) for r in rows)} with findings; {OUT}')
    for row in rows:
        if row['issues']:
            print(row['path'], '; '.join(row['issues']))


if __name__ == '__main__':
    main()
