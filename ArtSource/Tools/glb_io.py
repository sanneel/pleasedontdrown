"""Minimal GLB reading and writing for the character tools: the first mesh's arrays and its colour texture, and
writing the GLB back with that texture replaced (every other byte of the file kept as it was).
Plain Python + numpy (no Blender needed)."""
import io
import json
import struct

import numpy as np

_COMPONENTS = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
_WIDTH = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}


class Glb:
    def __init__(self, path):
        data = open(path, 'rb').read()
        magic, _, _ = struct.unpack_from('<III', data, 0)
        if magic != 0x46546C67:
            raise ValueError(f'{path}: not a GLB')
        json_len = struct.unpack_from('<I', data, 12)[0]
        self.json = json.loads(data[20:20 + json_len])
        bin_at = 20 + json_len
        bin_len = struct.unpack_from('<I', data, bin_at)[0]
        self.bin = bytearray(data[bin_at + 8:bin_at + 8 + bin_len])
        self.path = path

    def view_bytes(self, index):
        view = self.json['bufferViews'][index]
        start = view.get('byteOffset', 0)
        return bytes(self.bin[start:start + view['byteLength']])

    def accessor(self, index):
        acc = self.json['accessors'][index]
        view = self.json['bufferViews'][acc['bufferView']]
        dtype = np.dtype(_COMPONENTS[acc['componentType']])
        width = _WIDTH[acc['type']]
        start = view.get('byteOffset', 0) + acc.get('byteOffset', 0)
        stride = view.get('byteStride', 0) or dtype.itemsize * width
        count = acc['count']
        raw = np.frombuffer(self.bin, dtype=np.uint8, count=stride * (count - 1) + dtype.itemsize * width, offset=start)
        out = np.lib.stride_tricks.as_strided(raw, shape=(count, dtype.itemsize * width), strides=(stride, 1)).copy()
        arr = out.view(dtype).reshape(count, width)
        if acc.get('normalized'):
            arr = arr.astype(np.float32) / np.iinfo(dtype).max
        return arr

    def primitive(self, mesh=0, prim=0):
        return self.json['meshes'][mesh]['primitives'][prim]

    def base_color_image(self):
        """Index into images of the first material's base colour texture."""
        mat = self.json['materials'][0]
        tex = mat['pbrMetallicRoughness']['baseColorTexture']['index']
        return self.json['textures'][tex]['source']

    def image_bytes(self, image):
        return self.view_bytes(self.json['images'][image]['bufferView'])

    def replace_image(self, image, payload, mime):
        """Swap an image's bytes; the binary chunk is laid out again with every other view's bytes unchanged."""
        target = self.json['images'][image]['bufferView']
        views = self.json['bufferViews']
        chunks = [self.view_bytes(i) if i != target else payload for i in range(len(views))]
        out = bytearray()
        for i, view in enumerate(views):
            while len(out) % 4:
                out.append(0)
            view['byteOffset'] = len(out)
            view['byteLength'] = len(chunks[i])
            out += chunks[i]
        while len(out) % 4:
            out.append(0)
        self.bin = out
        self.json['buffers'][0]['byteLength'] = len(out)
        self.json['images'][image]['mimeType'] = mime

    def save(self, path):
        text = json.dumps(self.json, separators=(',', ':')).encode('utf-8')
        while len(text) % 4:
            text += b' '
        total = 12 + 8 + len(text) + 8 + len(self.bin)
        with open(path, 'wb') as f:
            f.write(struct.pack('<III', 0x46546C67, 2, total))
            f.write(struct.pack('<II', len(text), 0x4E4F534A))
            f.write(text)
            f.write(struct.pack('<II', len(self.bin), 0x004E4942))
            f.write(self.bin)


def load_image(payload):
    from PIL import Image
    return np.asarray(Image.open(io.BytesIO(payload)).convert('RGB'), dtype=np.float32) / 255.0


def png_bytes(rgb):
    from PIL import Image
    buf = io.BytesIO()
    Image.fromarray(np.clip(rgb * 255.0 + 0.5, 0, 255).astype(np.uint8), 'RGB').save(buf, 'PNG', optimize=False, compress_level=6)
    return buf.getvalue()
