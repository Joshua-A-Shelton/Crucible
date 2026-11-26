#!/usr/bin/env python3
"""Writes the editor's material sphere (ADR 0132): a UV sphere of radius 0.5
as a glTF with its buffer embedded, positions, normals, tangents, and UVs."""
import base64, json, math, struct, sys

LON, LAT = 48, 24
pos, nrm, tan, uv, idx = [], [], [], [], []
for j in range(LAT + 1):
    v = j / LAT
    theta = v * math.pi
    for i in range(LON + 1):
        u = i / LON
        phi = u * 2 * math.pi
        n = (math.sin(theta) * math.cos(phi), math.cos(theta), -math.sin(theta) * math.sin(phi))
        nrm.append(n)
        pos.append(tuple(c * 0.5 for c in n))
        tan.append((-math.sin(phi), 0.0, -math.cos(phi), 1.0))
        uv.append((u, v))
for j in range(LAT):
    for i in range(LON):
        a = j * (LON + 1) + i
        b = a + LON + 1
        idx += [a, b, a + 1, a + 1, b, b + 1]

def pack(fmt, rows):
    return b"".join(struct.pack(fmt, *r) for r in rows)

parts = [pack("<3f", pos), pack("<3f", nrm), pack("<4f", tan), pack("<2f", uv), struct.pack(f"<{len(idx)}H", *idx)]
views, offset = [], 0
data = b""
for p in parts:
    views.append({"buffer": 0, "byteOffset": offset, "byteLength": len(p)})
    data += p
    offset += len(p)
    while offset % 4:
        data += b"\0"
        offset += 1
count = len(pos)
gltf = {
    "asset": {"version": "2.0", "generator": "Crucible make-sphere.py"},
    "scene": 0,
    "scenes": [{"nodes": [0]}],
    "nodes": [{"name": "Sphere", "mesh": 0}],
    "meshes": [{"name": "Sphere", "primitives": [{"attributes": {"POSITION": 0, "NORMAL": 1, "TANGENT": 2, "TEXCOORD_0": 3}, "indices": 4, "material": 0}]}],
    "materials": [{"name": "Default", "pbrMetallicRoughness": {"baseColorFactor": [0.8, 0.8, 0.8, 1], "metallicFactor": 0, "roughnessFactor": 0.5}}],
    "buffers": [{"byteLength": len(data), "uri": "data:application/octet-stream;base64," + base64.b64encode(data).decode()}],
    "bufferViews": views,
    "accessors": [
        {"bufferView": 0, "componentType": 5126, "count": count, "type": "VEC3", "min": [-0.5, -0.5, -0.5], "max": [0.5, 0.5, 0.5]},
        {"bufferView": 1, "componentType": 5126, "count": count, "type": "VEC3"},
        {"bufferView": 2, "componentType": 5126, "count": count, "type": "VEC4"},
        {"bufferView": 3, "componentType": 5126, "count": count, "type": "VEC2"},
        {"bufferView": 4, "componentType": 5123, "count": len(idx), "type": "SCALAR"},
    ],
}
with open(sys.argv[1], "w") as f:
    json.dump(gltf, f)
    f.write("\n")
