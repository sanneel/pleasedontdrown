"""Set a glossy inflatable finish without altering geometry or embedded textures."""
import json
import struct
from pathlib import Path

path = Path(__file__).resolve().parents[1] / "Assets/_Game/Art/Meshy/flamingo.glb"
data = path.read_bytes()
magic, version, length = struct.unpack_from("<III", data)
assert magic == 0x46546C67 and version == 2 and length == len(data)
json_length, kind = struct.unpack_from("<II", data, 12)
assert kind == 0x4E4F534A
document = json.loads(data[20:20 + json_length])
for material in document.get("materials", []):
    surface = material.setdefault("pbrMetallicRoughness", {})
    # A tight white sun glint on pink vinyl. The generated metal/roughness map
    # makes the highlight patchy; preserve the painted colour and normal detail.
    surface.pop("metallicRoughnessTexture", None)
    surface["metallicFactor"] = 0.0
    surface["roughnessFactor"] = 0.06
    if "normalTexture" in material:
        material["normalTexture"]["scale"] = 0.25
payload = json.dumps(document, separators=(",", ":")).encode()
payload += b" " * (-len(payload) % 4)
tail = data[20 + json_length:]
result = struct.pack("<III", magic, version, 20 + len(payload) + len(tail))
result += struct.pack("<II", len(payload), kind) + payload + tail
if result != data:
    path.write_bytes(result)
print(f"Flamingo: {len(document.get('materials', []))} glossy vinyl materials; binary geometry/textures preserved.")
