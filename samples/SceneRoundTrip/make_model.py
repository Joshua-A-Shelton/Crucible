#!/usr/bin/env python3
"""Makes the M4 demonstration's model (ADR 0046): Lopsided.gltf, its buffer,
and its texture, in the sample project's Assets folder.

The model is asymmetric on purpose, so a mirroring mistake shows. In glTF's
convention (right-handed, Y up, the asset facing +Z, its right on -X):
  Lopsided            the root, no mesh
    Body              a box, mesh "Body"
      RightArm        a long box on the asset's right (-X), mesh "Arm"
      Nose            a small box in front (+Z), mesh "Nose"
Every part is a closed box with flat normals and counter-clockwise front
faces. The texture is blue on its left half and red on its right; the arm
uses the red half.

Standard library only. Run it again after changing it, and commit the output.
"""

import json
import pathlib
import struct
import zlib

HERE = pathlib.Path(__file__).resolve().parent
ASSETS = HERE / "Project" / "Assets"


def box(half, u_range):
    """A closed box: 6 faces of 4 corners, counter-clockwise from outside."""
    positions, normals, uvs, indices = [], [], [], []
    for axis in range(3):
        for sign in (1.0, -1.0):
            normal = [0.0, 0.0, 0.0]
            normal[axis] = sign
            # u x v = the normal: the next two axes, swapped for the negative side.
            u_axis, v_axis = (axis + 1) % 3, (axis + 2) % 3
            if sign < 0:
                u_axis, v_axis = v_axis, u_axis
            base = len(positions)
            for du, dv in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                p = [0.0, 0.0, 0.0]
                p[axis] = sign * half[axis]
                p[u_axis] = du * half[u_axis]
                p[v_axis] = dv * half[v_axis]
                positions.append(p)
                normals.append(normal)
                u0, u1 = u_range
                uvs.append([u0 + (du + 1) / 2 * (u1 - u0), (1 - dv) / 2])
            indices += [base, base + 1, base + 2, base, base + 2, base + 3]
    return positions, normals, uvs, indices


def png(width, height, pixel):
    """An 8-bit RGB PNG."""
    rows = b"".join(
        b"\x00" + b"".join(bytes(pixel(x, y)) for x in range(width)) for y in range(height))

    def chunk(kind, data):
        return (struct.pack(">I", len(data)) + kind + data +
                struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF))

    header = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) +
            chunk(b"IDAT", zlib.compress(rows, 9)) + chunk(b"IEND", b""))


def main():
    parts = {
        "Body": box((0.4, 0.5, 0.3), (0.0, 0.5)),
        "Arm": box((0.3, 0.1, 0.1), (0.5, 1.0)),
        "Nose": box((0.08, 0.08, 0.15), (0.0, 0.5)),
    }
    buffer = bytearray()
    views, accessors, meshes = [], [], []

    def add(data, fmt, count, kind, component, target=None, extra=None):
        while len(buffer) % 4:
            buffer.append(0)
        view = {"buffer": 0, "byteOffset": len(buffer), "byteLength": len(data)}
        if target:
            view["target"] = target
        buffer.extend(data)
        views.append(view)
        accessor = {"bufferView": len(views) - 1, "componentType": component, "count": count, "type": kind}
        accessor.update(extra or {})
        accessors.append(accessor)
        return len(accessors) - 1

    for name, (positions, normals, uvs, indices) in parts.items():
        flat = lambda rows: [value for row in rows for value in row]
        lo = [min(p[c] for p in positions) for c in range(3)]
        hi = [max(p[c] for p in positions) for c in range(3)]
        position = add(struct.pack(f"<{len(positions) * 3}f", *flat(positions)), "f", len(positions), "VEC3",
                       5126, 34962, {"min": lo, "max": hi})
        normal = add(struct.pack(f"<{len(normals) * 3}f", *flat(normals)), "f", len(normals), "VEC3", 5126, 34962)
        uv = add(struct.pack(f"<{len(uvs) * 2}f", *flat(uvs)), "f", len(uvs), "VEC2", 5126, 34962)
        index = add(struct.pack(f"<{len(indices)}H", *indices), "H", len(indices), "SCALAR", 5123, 34963)
        meshes.append({"name": name, "primitives": [{
            "attributes": {"POSITION": position, "NORMAL": normal, "TEXCOORD_0": uv},
            "indices": index, "material": 0}]})

    gltf = {
        "asset": {"version": "2.0", "generator": "Crucible make_model.py (ADR 0046)"},
        "scene": 0,
        "scenes": [{"nodes": [0]}],
        "nodes": [
            {"name": "Lopsided", "children": [1]},
            {"name": "Body", "mesh": 0, "children": [2, 3]},
            {"name": "RightArm", "mesh": 1, "translation": [-0.7, 0.1, 0.0]},
            {"name": "Nose", "mesh": 2, "translation": [0.0, 0.2, 0.45]},
        ],
        "meshes": meshes,
        "materials": [{"name": "Paint", "pbrMetallicRoughness": {
            "baseColorTexture": {"index": 0}, "metallicFactor": 0.0, "roughnessFactor": 0.8}}],
        "textures": [{"source": 0}],
        "images": [{"uri": "../Textures/Lopsided.png"}],
        "buffers": [{"uri": "Lopsided.bin", "byteLength": len(buffer)}],
        "bufferViews": views,
        "accessors": accessors,
    }

    (ASSETS / "Models").mkdir(parents=True, exist_ok=True)
    (ASSETS / "Textures").mkdir(parents=True, exist_ok=True)
    (ASSETS / "Models" / "Lopsided.bin").write_bytes(bytes(buffer))
    (ASSETS / "Models" / "Lopsided.gltf").write_text(json.dumps(gltf, indent=2) + "\n", newline="\n")
    blue, red = (40, 70, 200), (210, 40, 40)
    (ASSETS / "Textures" / "Lopsided.png").write_bytes(png(64, 64, lambda x, y: red if x >= 32 else blue))


if __name__ == "__main__":
    main()
