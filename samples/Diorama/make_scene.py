#!/usr/bin/env python3
"""Makes the M5 demonstration's models (ADR 0056): one glTF per kind of
object, with their textures and sidecars, in the sample project's Assets
folder. The game places instances of them (Diorama.cs).

  Ground     a 60 m square, a checker texture repeated 12 times (UVs past 4)
  House      hard-edged walls and a gable roof (outline normals, shadows),
             and glass windows (blended, sorted)
  Tree       a trunk and crossed leaf cards (alpha-tested, double-sided)
  LampPost   a post and a glowing head (emissive, for bloom)
  Hero       a toon figure of spheres, with a nose; outlined (its sidecar)

glTF's convention: right-handed, Y up, counter-clockwise front faces. Every
face is wound from its outward direction, so nothing renders inside out.

Standard library only. Run it again after changing it, and commit the output.
"""

import json
import math
import pathlib
import struct
import zlib

HERE = pathlib.Path(__file__).resolve().parent
ASSETS = HERE / "Project" / "Assets"


def sub(a, b):
    return [a[i] - b[i] for i in range(3)]


def cross(a, b):
    return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]


def dot(a, b):
    return sum(a[i] * b[i] for i in range(3))


def normalize(v):
    length = math.sqrt(dot(v, v))
    return [c / length for c in v]


class Mesh:
    """Positions, normals, UVs, and triangles, with flat faces wound to
    face their outward direction."""

    def __init__(self):
        self.positions, self.normals, self.uvs, self.indices = [], [], [], []

    def vertex(self, p, n, uv):
        self.positions.append(list(p))
        self.normals.append(list(n))
        self.uvs.append(list(uv))
        return len(self.positions) - 1

    def triangle(self, a, b, c, outward):
        pa, pb, pc = self.positions[a], self.positions[b], self.positions[c]
        if dot(cross(sub(pb, pa), sub(pc, pa)), outward) < 0:
            b, c = c, b
        self.indices += [a, b, c]

    def face(self, corners, uvs, normal):
        """A flat polygon (a fan), its normal also its outward direction."""
        first = [self.vertex(p, normal, uv) for p, uv in zip(corners, uvs)]
        for i in range(1, len(first) - 1):
            self.triangle(first[0], first[i], first[i + 1], normal)

    def box(self, center, half, uv_scale=1.0):
        for axis in range(3):
            for sign in (1.0, -1.0):
                normal = [0.0, 0.0, 0.0]
                normal[axis] = sign
                u, v = (axis + 1) % 3, (axis + 2) % 3
                corners, uvs = [], []
                for du, dv in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                    p = list(center)
                    p[axis] += sign * half[axis]
                    p[u] += du * half[u]
                    p[v] += dv * half[v]
                    corners.append(p)
                    uvs.append([(du + 1) / 2 * uv_scale, (1 - dv) / 2 * uv_scale])
                self.face(corners, uvs, normal)

    def sphere(self, center, radii, rings=16, segments=32):
        """An ellipsoid with smooth normals."""
        base = len(self.positions)
        for r in range(rings + 1):
            theta = math.pi * r / rings
            for s in range(segments + 1):
                phi = 2 * math.pi * s / segments
                unit = [math.sin(theta) * math.cos(phi), math.cos(theta), math.sin(theta) * math.sin(phi)]
                p = [center[i] + unit[i] * radii[i] for i in range(3)]
                n = normalize([unit[i] / radii[i] for i in range(3)])
                self.vertex(p, n, [s / segments, r / rings])
        for r in range(rings):
            for s in range(segments):
                a = base + r * (segments + 1) + s
                b, c, d = a + segments + 1, a + segments + 2, a + 1
                for tri in ((a, b, c), (a, c, d)):
                    corners = [self.positions[i] for i in tri]
                    if dot(cross(sub(corners[1], corners[0]), sub(corners[2], corners[0])),
                           cross(sub(corners[1], corners[0]), sub(corners[2], corners[0]))) < 1e-12:
                        continue  # degenerate, at a pole
                    middle = [sum(p[i] for p in corners) / 3 for i in range(3)]
                    self.triangle(*tri, sub(middle, center))

    def cylinder(self, center, radius, height, segments=12):
        """Upright, smooth sides, flat caps."""
        base = len(self.positions)
        for s in range(segments + 1):
            phi = 2 * math.pi * s / segments
            n = [math.cos(phi), 0.0, math.sin(phi)]
            for y in (0.0, height):
                self.vertex([center[0] + n[0] * radius, center[1] + y, center[2] + n[2] * radius], n,
                            [s / segments, 1 - y / height])
        for s in range(segments):
            a, b, c, d = base + s * 2, base + s * 2 + 1, base + s * 2 + 3, base + s * 2 + 2
            phi = 2 * math.pi * (s + 0.5) / segments
            out = [math.cos(phi), 0.0, math.sin(phi)]
            self.triangle(a, b, c, out)
            self.triangle(a, c, d, out)
        for y, sign in ((height, 1.0), (0.0, -1.0)):
            ring = [[center[0] + math.cos(2 * math.pi * s / segments) * radius, center[1] + y,
                     center[2] + math.sin(2 * math.pi * s / segments) * radius] for s in range(segments)]
            self.face(ring, [[0.5, 0.5]] * segments, [0.0, sign, 0.0])


def png(width, height, pixel, alpha=False):
    """An 8-bit RGB or RGBA PNG."""
    rows = b"".join(
        b"\x00" + b"".join(bytes(pixel(x, y)) for x in range(width)) for y in range(height))

    def chunk(kind, data):
        return (struct.pack(">I", len(data)) + kind + data +
                struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF))

    header = struct.pack(">IIBBBBB", width, height, 8, 6 if alpha else 2, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) +
            chunk(b"IDAT", zlib.compress(rows, 9)) + chunk(b"IEND", b""))


def write_model(name, parts, materials, textures, guid):
    """parts: [(mesh name, [(Mesh, material index)])], one node each under a root."""
    buffer = bytearray()
    views, accessors, meshes = [], [], []

    def add(data, count, kind, component, target, extra=None):
        while len(buffer) % 4:
            buffer.append(0)
        view = {"buffer": 0, "byteOffset": len(buffer), "byteLength": len(data), "target": target}
        buffer.extend(data)
        views.append(view)
        accessor = {"bufferView": len(views) - 1, "componentType": component, "count": count, "type": kind}
        accessor.update(extra or {})
        accessors.append(accessor)
        return len(accessors) - 1

    for mesh_name, primitives in parts:
        out = []
        for mesh, material in primitives:
            flat = lambda rows: [value for row in rows for value in row]
            lo = [min(p[c] for p in mesh.positions) for c in range(3)]
            hi = [max(p[c] for p in mesh.positions) for c in range(3)]
            count = len(mesh.positions)
            position = add(struct.pack(f"<{count * 3}f", *flat(mesh.positions)), count, "VEC3", 5126, 34962,
                           {"min": lo, "max": hi})
            normal = add(struct.pack(f"<{count * 3}f", *flat(mesh.normals)), count, "VEC3", 5126, 34962)
            uv = add(struct.pack(f"<{count * 2}f", *flat(mesh.uvs)), count, "VEC2", 5126, 34962)
            index = add(struct.pack(f"<{len(mesh.indices)}H", *mesh.indices), len(mesh.indices), "SCALAR", 5123,
                        34963)
            out.append({"attributes": {"POSITION": position, "NORMAL": normal, "TEXCOORD_0": uv},
                        "indices": index, "material": material})
        meshes.append({"name": mesh_name, "primitives": out})

    gltf = {
        "asset": {"version": "2.0", "generator": "Crucible make_scene.py (ADR 0056)"},
        "scene": 0,
        "scenes": [{"nodes": [0]}],
        "nodes": [{"name": name, "children": list(range(1, len(parts) + 1))}] +
                 [{"name": mesh_name, "mesh": i} for i, (mesh_name, _) in enumerate(parts)],
        "meshes": meshes,
        "materials": materials,
        "buffers": [{"uri": f"{name}.bin", "byteLength": len(buffer)}],
        "bufferViews": views,
        "accessors": accessors,
    }
    if textures:
        gltf["textures"] = [{"source": i} for i in range(len(textures))]
        gltf["images"] = [{"uri": f"../Textures/{texture}"} for texture in textures]
    models = ASSETS / "Models"
    models.mkdir(parents=True, exist_ok=True)
    (models / f"{name}.bin").write_bytes(bytes(buffer))
    (models / f"{name}.gltf").write_text(json.dumps(gltf, indent=2) + "\n", newline="\n")
    sidecar = {"Guid": guid, "Importer": "Mesh"}
    return models / f"{name}.gltf.crmeta", sidecar


def material(name, color, roughness=0.8, texture=None, alpha=None, cutoff=0.5, double=False, emissive=None):
    m = {"name": name, "pbrMetallicRoughness": {"baseColorFactor": color, "metallicFactor": 0.0,
                                                 "roughnessFactor": roughness}}
    if texture is not None:
        m["pbrMetallicRoughness"]["baseColorTexture"] = {"index": texture}
    if alpha:
        m["alphaMode"] = alpha
        if alpha == "MASK":
            m["alphaCutoff"] = cutoff
    if double:
        m["doubleSided"] = True
    if emissive:
        m["emissiveFactor"] = emissive
    return m


def main():
    sidecars = []

    # The ground: one quad facing up, its texture repeated 12 times.
    ground = Mesh()
    ground.face([[-30, 0, -30], [30, 0, -30], [30, 0, 30], [-30, 0, 30]],
                [[0, 0], [12, 0], [12, 12], [0, 12]], [0.0, 1.0, 0.0])
    sidecars.append(write_model("Ground", [("Ground", [(ground, 0)])],
                                [material("Grass", [1, 1, 1, 1], texture=0)], ["Grass.png"],
                                "8d1c2b3a-4e5f-4a6b-9c7d-0e1f2a3b4c01"))

    # A house: walls, a gable roof, a door, and four glass windows standing
    # just proud of the walls.
    walls, roof, door, glass = Mesh(), Mesh(), Mesh(), Mesh()
    walls.box([0, 1.5, 0], [2.5, 1.5, 2.0])
    door.box([0, 0.9, -2.02], [0.45, 0.9, 0.04])
    ridge, eave = 4.4, 3.0
    roof.face([[-2.8, eave, -2.3], [2.8, eave, -2.3], [2.8, ridge, 0], [-2.8, ridge, 0]], [[0, 0]] * 4,
              normalize([0, 2.3, -(ridge - eave)]))
    roof.face([[-2.8, eave, 2.3], [2.8, eave, 2.3], [2.8, ridge, 0], [-2.8, ridge, 0]], [[0, 0]] * 4,
              normalize([0, 2.3, ridge - eave]))
    for x in (-2.5, 2.5):
        roof.face([[x, eave, -2.0], [x, eave, 2.0], [x, ridge, 0]], [[0, 0]] * 3, [1.0 if x > 0 else -1.0, 0, 0])
    roof.face([[-2.8, eave, -2.3], [2.8, eave, -2.3], [2.8, eave, 2.3], [-2.8, eave, 2.3]], [[0, 0]] * 4,
              [0.0, -1.0, 0.0])
    for x in (-1.5, 1.5):
        for z in (-2.03, 2.03):
            glass.face([[x - 0.5, 1.4, z], [x + 0.5, 1.4, z], [x + 0.5, 2.3, z], [x - 0.5, 2.3, z]],
                       [[0, 0]] * 4, [0.0, 0.0, -1.0 if z < 0 else 1.0])
    sidecars.append(write_model("House", [("Walls", [(walls, 0)]), ("Roof", [(roof, 1)]), ("Door", [(door, 2)]),
                                          ("Windows", [(glass, 3)])],
                                [material("Plaster", [0.92, 0.86, 0.72, 1]), material("Tiles", [0.72, 0.22, 0.16, 1]),
                                 material("Wood", [0.42, 0.26, 0.14, 1]),
                                 material("Glass", [0.55, 0.75, 0.9, 0.35], roughness=0.1, alpha="BLEND")], [],
                                "8d1c2b3a-4e5f-4a6b-9c7d-0e1f2a3b4c02"))

    # A tree: a trunk and three crossed leaf cards.
    trunk, leaves = Mesh(), Mesh()
    trunk.cylinder([0, 0, 0], 0.18, 2.2)
    for k in range(3):
        angle = math.pi * k / 3
        dx, dz = math.cos(angle) * 1.4, math.sin(angle) * 1.4
        normal = [-math.sin(angle), 0.0, math.cos(angle)]
        leaves.face([[-dx, 1.4, -dz], [dx, 1.4, dz], [dx, 4.2, dz], [-dx, 4.2, -dz]],
                    [[0, 1], [1, 1], [1, 0], [0, 0]], normal)
    sidecars.append(write_model("Tree", [("Trunk", [(trunk, 0)]), ("Leaves", [(leaves, 1)])],
                                [material("Bark", [0.35, 0.24, 0.16, 1]),
                                 material("Leaves", [1, 1, 1, 1], texture=0, alpha="MASK", double=True)],
                                ["Leaves.png"], "8d1c2b3a-4e5f-4a6b-9c7d-0e1f2a3b4c03"))

    # A lamp post: a post, an arm, and a glowing head.
    post, head = Mesh(), Mesh()
    post.box([0, 1.6, 0], [0.07, 1.6, 0.07])
    post.box([0.3, 3.15, 0], [0.3, 0.05, 0.05])
    head.box([0.55, 2.95, 0], [0.14, 0.16, 0.14])
    sidecars.append(write_model("LampPost", [("Post", [(post, 0)]), ("Head", [(head, 1)])],
                                [material("Iron", [0.12, 0.12, 0.14, 1], roughness=0.5),
                                 material("Glow", [1, 0.9, 0.6, 1], emissive=[1, 0.85, 0.5])], [],
                                "8d1c2b3a-4e5f-4a6b-9c7d-0e1f2a3b4c04"))

    # The hero: a body, a head with a nose toward +Z (glTF's front), and
    # hands; smooth, toon-shaded, and outlined.
    body, skin, hair = Mesh(), Mesh(), Mesh()
    body.sphere([0, 0.75, 0], [0.32, 0.55, 0.26])
    skin.sphere([0, 1.55, 0], [0.3, 0.3, 0.3])
    skin.sphere([0, 1.52, 0.29], [0.045, 0.06, 0.07], rings=8, segments=12)
    for x in (-0.38, 0.38):
        skin.sphere([x, 0.85, 0.05], [0.09, 0.09, 0.09], rings=8, segments=12)
    hair.sphere([0, 1.68, -0.04], [0.31, 0.2, 0.3])
    path, sidecar = write_model("Hero", [("Body", [(body, 0)]), ("Skin", [(skin, 1)]), ("Hair", [(hair, 2)])],
                                [material("Tunic", [0.25, 0.45, 0.85, 1]), material("Skin", [0.98, 0.8, 0.68, 1]),
                                 material("Hair", [0.35, 0.2, 0.12, 1])], [],
                                "8d1c2b3a-4e5f-4a6b-9c7d-0e1f2a3b4c05")
    sidecar["Settings"] = {"Outline": {"Width": 2.0, "Color": [0.18, 0.1, 0.08], "Tint": 0.3}}
    sidecars.append((path, sidecar))

    # The houses get a thinner, darker line: hard edges, through their
    # outline normals.
    house_path, house_sidecar = sidecars[1]
    house_sidecar["Settings"] = {"Outline": {"Width": 1.2, "Color": [0.12, 0.09, 0.08], "Tint": 0.0}}

    for path, sidecar in sidecars:
        path.write_text(json.dumps(sidecar, indent=2) + "\n", newline="\n")

    textures = ASSETS / "Textures"
    textures.mkdir(parents=True, exist_ok=True)
    dark, light = (86, 140, 62), (104, 158, 74)
    (textures / "Grass.png").write_bytes(
        png(64, 64, lambda x, y: dark if (x // 32 + y // 32) % 2 else light))

    def leaf(x, y):
        # Overlapping round leaves, clear between them.
        for cx, cy, r in ((16, 20, 14), (44, 16, 15), (30, 40, 17), (12, 50, 11), (52, 48, 12), (32, 12, 9)):
            if (x - cx) ** 2 + (y - cy) ** 2 < r * r:
                shade = 0.8 + 0.2 * ((x * 7 + y * 3) % 5) / 4
                return (int(60 * shade), int(140 * shade), int(50 * shade), 255)
        return (40, 90, 30, 0)
    (textures / "Leaves.png").write_bytes(png(64, 64, leaf, alpha=True))
    for texture, guid in (("Grass.png", "8d1c2b3a-4e5f-4a6b-9c7d-0e1f2a3b4c11"),
                          ("Leaves.png", "8d1c2b3a-4e5f-4a6b-9c7d-0e1f2a3b4c12")):
        (textures / f"{texture}.crmeta").write_text(
            json.dumps({"Guid": guid, "Importer": "Texture"}, indent=2) + "\n", newline="\n")
    (HERE / "Project" / "Diorama.crproject").write_text(json.dumps({"Name": "Diorama"}, indent=2) + "\n",
                                                         newline="\n")


if __name__ == "__main__":
    main()
