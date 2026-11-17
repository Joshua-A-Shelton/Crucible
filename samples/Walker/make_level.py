#!/usr/bin/env python3
"""Makes M9's demonstration level (ADR 0097): Level.gltf and its buffer in
the sample project's Assets folder, one static mesh for the Walker to walk.

  Floor     24 m square, its top at 0, inside walls 1.2 m high.
  Platform  1 m high, reached by a ramp (1 in 5, about 11 degrees) on one
            side and left by four 0.2 m stairs on the other, both lower
            than the character's step and slope limits.
  Slope     45 degrees up to a 3 m ledge: too steep, so a wall.
  Wall      a free-standing low wall to walk round.

glTF's convention: right-handed, Y up, counter-clockwise front faces.
Crucible mirrors X on import, so the scene's positions have x negated.

Standard library only. Run it again after changing it, and commit the output.
"""

import json
import pathlib
import struct

HERE = pathlib.Path(__file__).resolve().parent
MODELS = HERE / "Project" / "Assets" / "Models"

FLOOR = (0.42, 0.48, 0.4)
STONE = (0.62, 0.6, 0.56)
RAMP = (0.7, 0.55, 0.35)
STEEP = (0.55, 0.35, 0.3)


def box(low, high, color):
    """Six faces of an axis-aligned box: (normal, four corners, color)."""
    (x0, y0, z0), (x1, y1, z1) = low, high
    return [
        ((1, 0, 0), [(x1, y0, z1), (x1, y0, z0), (x1, y1, z0), (x1, y1, z1)], color),
        ((-1, 0, 0), [(x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0)], color),
        ((0, 1, 0), [(x0, y1, z1), (x1, y1, z1), (x1, y1, z0), (x0, y1, z0)], color),
        ((0, -1, 0), [(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)], color),
        ((0, 0, 1), [(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)], color),
        ((0, 0, -1), [(x1, y0, z0), (x0, y0, z0), (x0, y1, z0), (x1, y1, z0)], color),
    ]


def wedge_z(x0, x1, z0, z1, height, color):
    """A ramp rising from 0 at z0 to `height` at z1, between x0 and x1."""
    length = ((z1 - z0) ** 2 + height ** 2) ** 0.5
    up = (0, (z1 - z0) / length, -height / length)
    return [
        (up, [(x1, 0, z0), (x0, 0, z0), (x0, height, z1), (x1, height, z1)], color),
        ((0, 0, 1), [(x0, 0, z1), (x1, 0, z1), (x1, height, z1), (x0, height, z1)], color),
        ((1, 0, 0), [(x1, 0, z1), (x1, 0, z0), (x1, height, z1), (x1, height, z1)], color),
        ((-1, 0, 0), [(x0, 0, z0), (x0, 0, z1), (x0, height, z1), (x0, height, z1)], color),
    ]


def wedge_x(x0, x1, z0, z1, height, color):
    """A slope rising from 0 at x1 to `height` at x0, between z0 and z1."""
    run = x1 - x0
    length = (run ** 2 + height ** 2) ** 0.5
    up = (height / length, run / length, 0)
    return [
        (up, [(x1, 0, z1), (x1, 0, z0), (x0, height, z0), (x0, height, z1)], color),
        ((0, 0, 1), [(x0, 0, z1), (x1, 0, z1), (x0, height, z1), (x0, height, z1)], color),
        ((0, 0, -1), [(x1, 0, z0), (x0, 0, z0), (x0, height, z0), (x0, height, z0)], color),
    ]


def level():
    faces = []
    faces += box((-12, -0.5, -12), (12, 0, 12), FLOOR)
    # The walls round the floor.
    faces += box((-12, 0, -12), (12, 1.2, -11.6), STONE)
    faces += box((-12, 0, 11.6), (12, 1.2, 12), STONE)
    faces += box((-12, 0, -11.6), (-11.6, 1.2, 11.6), STONE)
    faces += box((11.6, 0, -11.6), (12, 1.2, 11.6), STONE)
    # The platform, its ramp up from -z, and its stairs down to +z.
    faces += box((4, 0, -2), (10, 1, 6), STONE)
    faces += wedge_z(5, 9, -7, -2, 1, RAMP)
    for i, top in enumerate((0.8, 0.6, 0.4, 0.2)):
        faces += box((5, 0, 6 + 0.6 * i), (9, top, 6.6 + 0.6 * i), STONE)
    # Too steep to climb: 45 degrees up to a ledge.
    faces += wedge_x(-9, -6, -4, 2, 3, STEEP)
    faces += box((-11.6, 0, -4), (-9, 3, 2), STEEP)
    # A low wall to walk round.
    faces += box((-4, 0, 4.5), (1, 0.8, 5), STONE)
    return faces


class Buffer:
    def __init__(self):
        self.data = bytearray()
        self.views = []
        self.accessors = []

    def add(self, values, component, kind, fmt, count, target, bounds=False):
        while len(self.data) % 4:
            self.data.append(0)
        offset = len(self.data)
        flat = [v for value in values for v in (value if isinstance(value, list) else [value])]
        self.data += struct.pack("<" + fmt * len(flat), *flat)
        self.views.append({"buffer": 0, "byteOffset": offset, "byteLength": len(self.data) - offset,
                           "target": target})
        accessor = {"bufferView": len(self.views) - 1, "componentType": component, "count": count, "type": kind}
        if bounds:
            width = len(values[0])
            accessor["min"] = [min(v[i] for v in values) for i in range(width)]
            accessor["max"] = [max(v[i] for v in values) for i in range(width)]
        self.accessors.append(accessor)
        return len(self.accessors) - 1


def main():
    positions, normals, colors, indices = [], [], [], []
    for normal, corners, color in level():
        first = len(positions)
        for corner in corners:
            positions.append([float(c) for c in corner])
            normals.append([float(n) for n in normal])
            colors.append(list(color) + [1.0])
        # A wedge's end faces are triangles: their last two corners are one.
        if corners[2] == corners[3]:
            indices += [first, first + 1, first + 2]
        else:
            indices += [first, first + 1, first + 2, first, first + 2, first + 3]

    buffer = Buffer()
    count = len(positions)
    attributes = {
        "POSITION": buffer.add(positions, 5126, "VEC3", "f", count, 34962, bounds=True),
        "NORMAL": buffer.add(normals, 5126, "VEC3", "f", count, 34962),
        "COLOR_0": buffer.add(colors, 5126, "VEC4", "f", count, 34962),
    }
    index_accessor = buffer.add(indices, 5123, "SCALAR", "H", len(indices), 34963)
    gltf = {
        "asset": {"version": "2.0", "generator": "Crucible samples/Walker/make_level.py"},
        "scene": 0,
        "scenes": [{"nodes": [0]}],
        "nodes": [{"name": "Level", "mesh": 0}],
        "meshes": [{"name": "Level", "primitives": [{"attributes": attributes, "indices": index_accessor,
                                                     "material": 0}]}],
        "materials": [{"name": "Level", "pbrMetallicRoughness": {"baseColorFactor": [1, 1, 1, 1],
                                                                 "roughnessFactor": 0.9}}],
        "accessors": buffer.accessors,
        "bufferViews": buffer.views,
        "buffers": [{"uri": "Level.bin", "byteLength": len(buffer.data)}],
    }
    MODELS.mkdir(parents=True, exist_ok=True)
    (MODELS / "Level.bin").write_bytes(bytes(buffer.data))
    (MODELS / "Level.gltf").write_text(json.dumps(gltf, indent=2) + "\n")
    print(f"Level: {count} vertices, {len(indices) // 3} triangles")


if __name__ == "__main__":
    main()
