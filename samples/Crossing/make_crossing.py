#!/usr/bin/env python3
"""Makes the M11 demonstration (ADR 0114): the Crossing sample's project,
art, map, 3D background, and scene, in samples/Crossing/Project. Its game
code is in Project/Code, written by hand.

  The island    32 by 22 tiles of grass, ringed by rocks, over a 3D sea.
  The crossing  a sunken road (north to south, walls on both sides, a ramp
                at each end) passes under a raised road (west to east,
                railings on both sides, stairs at each end). Walking the
                sunken road goes under the deck; climbing the stairs goes
                over it. FloorTriggers at the stairs switch the hero's
                floor: its sorting layer (Low or High) and physics layer
                (Ground or Bridge) together.
  The art       pixel art drawn here: the hero (16x16 frames, idle and a
                four-frame walk facing down, left, right, and up), the
                tiles, and a lamp post; 16 pixels a meter.
  The light     a dusk ambient, lamps along the roads, a spot under the
                bridge, and a lantern the hero carries.

Every ID is fixed (uuid5 of a name), so the scene, the map, and the art's
sidecars agree from run to run. Standard library only. Run it again after
changing it, and commit the output.
"""

import json
import math
import pathlib
import struct
import uuid
import zlib

HERE = pathlib.Path(__file__).resolve().parent
PROJECT = HERE / "Project"
ASSETS = PROJECT / "Assets"
NAMESPACE = uuid.UUID("6a1c2f0e-4b7d-4e5a-9c3b-0d2e8f1a7b64")


def guid(name):
    return str(uuid.uuid5(NAMESPACE, name))


# Scene entity IDs: 16 hex digits, numbered.
_next_entity = [0]


def entity_id():
    _next_entity[0] += 1
    return f"c7055e{_next_entity[0]:010x}"


# ---------------------------------------------------------------------------
# PNG
# ---------------------------------------------------------------------------

def write_png(path, width, height, pixels):
    """pixels: rows top to bottom of (r, g, b, a) tuples."""
    raw = bytearray()
    for row in pixels:
        raw.append(0)
        for r, g, b, a in row:
            raw += bytes((r, g, b, a))

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(bytes(raw), 9))
    png += chunk(b"IEND", b"")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(png)


class Canvas:
    def __init__(self, width, height):
        self.width, self.height = width, height
        self.pixels = [[(0, 0, 0, 0)] * width for _ in range(height)]

    def set(self, x, y, color):
        if 0 <= x < self.width and 0 <= y < self.height:
            self.pixels[y][x] = color if len(color) == 4 else (*color, 255)

    def rect(self, x, y, w, h, color):
        for yy in range(y, y + h):
            for xx in range(x, x + w):
                self.set(xx, yy, color)

    def save(self, path):
        write_png(path, self.width, self.height, self.pixels)


def noise(x, y, seed):
    """A repeatable pseudo-random number in [0, 1) for a pixel."""
    n = (x * 374761393 + y * 668265263 + seed * 1442695041) & 0xFFFFFFFF
    n = (n ^ (n >> 13)) * 1274126177 & 0xFFFFFFFF
    return ((n ^ (n >> 16)) & 0xFFFF) / 65536


# ---------------------------------------------------------------------------
# The hero: 4 rows (down, left, right, up) of 4 frames, 16x16
# ---------------------------------------------------------------------------

SKIN = (238, 196, 150)
HAIR = (92, 56, 36)
TUNIC = (52, 104, 172)
TUNIC_DARK = (36, 72, 128)
BELT = (120, 82, 40)
LEGS = (70, 58, 50)
BOOTS = (48, 36, 30)
OUTLINE = (28, 22, 30)
EYE = (30, 26, 40)


def hero_frame(canvas, ox, oy, facing, step):
    """One frame at (ox, oy), its feet on the bottom row but one. Steps 0
    and 2 stand; 1 and 3 stride, one leg lifted, the body a pixel lower."""
    bob = 1 if step in (1, 3) else 0
    # Legs: two 2-pixel legs, rows 11 to 14; striding, one lifts a pixel
    # (seen from the side, they part instead).
    if facing in ("down", "up"):
        lifts = {0: (0, 0), 1: (1, 0), 2: (0, 0), 3: (0, 1)}[step]
        for lx, lift in ((6, lifts[0]), (8, lifts[1])):
            canvas.rect(ox + lx, oy + 11, 2, 3 - lift, LEGS)
            canvas.rect(ox + lx, oy + 14 - lift, 2, 1, BOOTS)
    else:
        part = {0: 0, 1: 2, 2: 0, 3: -2}[step]
        for lx in (7 - part // 2, 7 + part // 2):
            canvas.rect(ox + lx, oy + 11, 2, 3, LEGS)
            canvas.rect(ox + lx, oy + 14, 2, 1, BOOTS)
    # Body: a tunic with a belt, shaded on one side.
    canvas.rect(ox + 5, oy + 6 + bob, 6, 5, TUNIC)
    canvas.rect(ox + 5, oy + 9 + bob, 6, 1, BELT)
    canvas.rect(ox + 5, oy + 6 + bob, 1, 5, TUNIC_DARK)
    # Arms swing with the stride.
    swing = {0: 0, 1: 1, 2: 0, 3: -1}[step]
    if facing in ("down", "up"):
        for ax, down in ((4, max(swing, 0)), (11, max(-swing, 0))):
            canvas.rect(ox + ax, oy + 7 + bob + down, 1, 3, TUNIC_DARK)
            canvas.set(ox + ax, oy + 10 + bob + down, SKIN)
    else:
        arm = 7 + swing
        canvas.rect(ox + arm, oy + 7 + bob, 2, 3, TUNIC_DARK)
        canvas.set(ox + arm, oy + 10 + bob, SKIN)
    # Head: skin, hair, and eyes where it's facing.
    canvas.rect(ox + 5, oy + 1 + bob, 6, 5, SKIN)
    canvas.rect(ox + 5, oy + 0 + bob, 6, 2, HAIR)
    if facing == "up":
        canvas.rect(ox + 5, oy + 0 + bob, 6, 5, HAIR)
    elif facing == "down":
        canvas.set(ox + 6, oy + 3 + bob, EYE)
        canvas.set(ox + 9, oy + 3 + bob, EYE)
        canvas.set(ox + 5, oy + 2 + bob, HAIR)
        canvas.set(ox + 10, oy + 2 + bob, HAIR)
    else:  # left; right is drawn mirrored
        canvas.rect(ox + 8, oy + 0 + bob, 3, 4, HAIR)
        canvas.set(ox + 6, oy + 3 + bob, EYE)
    # A dark outline round what's drawn (the frame keeps a pixel free).
    drawn = {(x, y) for y in range(oy, oy + 16) for x in range(ox, ox + 16) if canvas.pixels[y][x][3]}
    for (x, y) in list(drawn):
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            n = (x + dx, y + dy)
            if ox <= n[0] < ox + 16 and oy <= n[1] < oy + 16 and n not in drawn:
                canvas.set(n[0], n[1], OUTLINE)


def make_hero():
    canvas = Canvas(64, 64)
    for row, facing in enumerate(("down", "left", "right", "up")):
        for step in range(4):
            draw = "left" if facing == "right" else facing
            frame = Canvas(16, 16)
            hero_frame(frame, 0, 0, draw, step)
            for y in range(16):
                for x in range(16):
                    sx = 15 - x if facing == "right" else x
                    if frame.pixels[y][sx][3]:
                        canvas.set(step * 16 + x, row * 16 + y, frame.pixels[y][sx])
    canvas.save(ASSETS / "Art" / "Hero.png")
    clips = []
    sub_assets = {}
    for row, facing in enumerate(("Down", "Left", "Right", "Up")):
        first = row * 4
        clips.append({"Name": f"Walk{facing}", "FramesPerSecond": 8, "Loop": True,
                      "Frames": [{"Frame": first + i, "Duration": 0.125, "Events": []} for i in range(4)]})
        clips.append({"Name": f"Idle{facing}", "FramesPerSecond": 8, "Loop": True,
                      "Frames": [{"Frame": first, "Duration": 0.5, "Events": []}]})
        sub_assets[f"Walk{facing}"] = guid(f"clip Walk{facing}")
        sub_assets[f"Idle{facing}"] = guid(f"clip Idle{facing}")
    write_sidecar(ASSETS / "Art" / "Hero.png", guid("Hero.png"), sprite_settings(16, 16, clips), sub_assets)


# ---------------------------------------------------------------------------
# Tiles: an 8x2 sheet of 16x16 tiles
# ---------------------------------------------------------------------------

GRASS, FLOWERS, ROAD, WALL, ROCK, BANK, DECK, RAILING, STAIRS, RAMP, FACE = range(11)


def tile_pixel(tile, x, y):
    n = noise(x, y, tile + 1)
    if tile in (GRASS, FLOWERS):
        base = (74, 138, 64) if n > 0.18 else (58, 116, 52)
        if n > 0.93:
            base = (96, 160, 78)
        if tile == FLOWERS and (x, y) in ((3, 4), (11, 9), (6, 13), (13, 2)):
            return (236, 220, 110)
        if tile == FLOWERS and (x, y) in ((4, 4), (3, 5), (2, 4), (3, 3)):
            return (240, 240, 236)
        return base
    if tile == ROAD:
        base = (124, 98, 70) if n > 0.2 else (106, 82, 58)
        return (150, 132, 112) if n > 0.95 else base
    if tile in (WALL, BANK, FACE):
        # Stone blocks: 8x4 (a face) or 8x8, staggered, with mortar.
        h = 4 if tile == FACE else 8
        row = y // h
        shift = 4 if row % 2 else 0
        mortar = y % h == 0 or (x + shift) % 8 == 0
        stone = {WALL: (128, 124, 118), BANK: (104, 100, 96), FACE: (116, 108, 98)}[tile]
        if mortar:
            return (72, 68, 66)
        shade = 10 if n > 0.6 else 0
        return tuple(min(255, c + shade) for c in stone)
    if tile == ROCK:
        base = (92, 86, 80) if n > 0.35 else (70, 66, 62)
        return (120, 114, 104) if n > 0.9 else base
    if tile in (DECK, RAILING, STAIRS):
        plank = y // 4
        seam = y % 4 == 3 or (x + plank * 5) % 16 == 0
        wood = (156, 112, 66) if plank % 2 else (144, 102, 60)
        color = (98, 66, 38) if seam else wood
        if tile == STAIRS:
            step = y // 4
            color = (98, 66, 38) if y % 4 == 3 else tuple(c - step * 6 for c in (170, 124, 74))
        if tile == RAILING:
            # A rail across the middle and posts.
            if 6 <= y <= 9:
                color = (110, 70, 40) if y in (6, 9) else (176, 128, 76)
            elif x % 8 in (3, 4):
                color = (110, 70, 40)
        return color
    if tile == RAMP:
        base = (124, 98, 70) if n > 0.2 else (106, 82, 58)
        return (88, 68, 48) if y % 5 == 0 else base
    return (255, 0, 255)


def make_tiles():
    canvas = Canvas(128, 32)
    for tile in range(11):
        tx, ty = tile % 8 * 16, tile // 8 * 16
        for y in range(16):
            for x in range(16):
                canvas.set(tx + x, ty + y, tile_pixel(tile, x, y))
    canvas.save(ASSETS / "Art" / "Tiles.png")
    write_sidecar(ASSETS / "Art" / "Tiles.png", guid("Tiles.png"), sprite_settings(16, 16, []), {})
    full = [[[0, 0], [16, 0], [16, 16], [0, 16]]]
    tiles = [{"Tile": tile, "Shapes": full} for tile in (WALL, ROCK, BANK, RAILING, FACE)]
    tileset = {"$guid": guid("Tiles.crtileset"), "Texture": guid("Tiles.png"), "TileWidth": 16, "TileHeight": 16,
               "Margin": 0, "Spacing": 0, "Tiles": tiles}
    (ASSETS / "Art" / "Tiles.crtileset").write_text(json.dumps(tileset, indent=2) + "\n")


def make_lamp():
    canvas = Canvas(16, 32)
    canvas.rect(7, 8, 2, 22, (54, 50, 56))      # the post
    canvas.rect(5, 29, 6, 3, (40, 36, 40))      # its foot
    canvas.rect(4, 2, 8, 7, (40, 36, 40))       # the lantern's frame
    canvas.rect(5, 3, 6, 5, (255, 214, 120))    # its light
    canvas.rect(6, 4, 4, 3, (255, 244, 190))
    canvas.rect(6, 0, 4, 2, (40, 36, 40))       # its cap
    canvas.save(ASSETS / "Art" / "Lamp.png")
    write_sidecar(ASSETS / "Art" / "Lamp.png", guid("Lamp.png"), sprite_settings(0, 0, []), {})


def sprite_settings(cell_width, cell_height, clips):
    return {"Usage": "Sprite", "Mipmaps": False, "Wrap": False, "PreserveAlphaCoverage": False, "AlphaCutoff": 0.5,
            "RdoLambda": 1, "Quality": 2, "PixelsPerUnit": 16, "Slicing": "Grid", "CellWidth": cell_width,
            "CellHeight": cell_height, "Margin": 0, "Spacing": 0, "Frames": [], "Clips": clips}


def write_sidecar(asset, id, settings, sub_assets, importer="Texture"):
    sidecar = {"Guid": id, "Importer": importer, "Settings": settings}
    if sub_assets:
        sidecar["SubAssets"] = dict(sorted(sub_assets.items()))
    pathlib.Path(str(asset) + ".crmeta").write_text(json.dumps(sidecar, indent=2) + "\n")


# ---------------------------------------------------------------------------
# The map: three layers
# ---------------------------------------------------------------------------

ISLAND = (-16, -11, 16, 11)        # x0, y0, x1, y1 (cells, exclusive ends)
ROAD_X = (-2, 2)                   # the sunken road's surface
ROAD_Y = (-9, 9)
DECK_X = (-9, 8)                   # the raised road's deck
DECK_Y = (-1, 1)
WEST_STAIRS = (-11, -9)
EAST_STAIRS = (8, 10)


def make_map():
    ground, deck, railings = {}, {}, {}
    x0, y0, x1, y1 = ISLAND
    for y in range(y0, y1):
        for x in range(x0, x1):
            edge = x in (x0, x1 - 1) or y in (y0, y1 - 1)
            ground[(x, y)] = ROCK if edge else (FLOWERS if noise(x, y, 99) > 0.9 else GRASS)
    # The sunken road, its walls, the ramps in them, and its end walls.
    for y in range(ROAD_Y[0], ROAD_Y[1]):
        for x in range(*ROAD_X):
            ground[(x, y)] = ROAD
    for y in range(ROAD_Y[0] - 1, ROAD_Y[1] + 1):
        ground[(ROAD_X[0] - 1, y)] = WALL
        ground[(ROAD_X[1], y)] = WALL
    for x in range(ROAD_X[0] - 1, ROAD_X[1] + 1):
        ground[(x, ROAD_Y[0] - 1)] = WALL
        ground[(x, ROAD_Y[1])] = WALL
    for y in (ROAD_Y[0], ROAD_Y[0] + 1):
        ground[(ROAD_X[0] - 1, y)] = RAMP   # the south ramp, through the west wall
    for y in (ROAD_Y[1] - 2, ROAD_Y[1] - 1):
        ground[(ROAD_X[1], y)] = RAMP       # the north ramp, through the east wall
    # The raised road over the grass: its bank (solid on the ground floor,
    # under the deck), its south face, and its stairs.
    footprint = range(DECK_Y[0] - 1, DECK_Y[1] + 1)   # railing, deck, deck, railing
    for y in footprint:
        for x in range(DECK_X[0], DECK_X[1]):
            if not ROAD_X[0] - 1 <= x <= ROAD_X[1]:
                ground[(x, y)] = BANK
    for x in range(DECK_X[0], DECK_X[1]):
        if not ROAD_X[0] - 1 <= x <= ROAD_X[1]:
            ground[(x, DECK_Y[0] - 2)] = FACE
    for stairs in (WEST_STAIRS, EAST_STAIRS):
        for x in range(*stairs):
            for y in range(*DECK_Y):
                ground[(x, y)] = STAIRS
            ground[(x, DECK_Y[0] - 1)] = BANK      # solid under the stairs' railings
            ground[(x, DECK_Y[1])] = BANK
    # The deck, and its railings (over the stairs too).
    for x in range(DECK_X[0], DECK_X[1]):
        for y in range(*DECK_Y):
            deck[(x, y)] = DECK
    for x in range(WEST_STAIRS[0], EAST_STAIRS[1]):
        railings[(x, DECK_Y[0] - 1)] = RAILING
        railings[(x, DECK_Y[1])] = RAILING

    def cells(layer):
        rows = {}
        for (x, y), tile in layer.items():
            rows.setdefault(y, []).append(f"[{x}, {y}, 0, {tile}, 0]")
        return ",\n        ".join(", ".join(sorted(row, key=lambda c: int(c[1:].split(",")[0])))
                                   for y, row in sorted(rows.items(), reverse=True))

    layers = [(0, "Ground", ground), (1, "Deck", deck), (2, "Railings", railings)]
    text = "{\n"
    text += f'  "$guid": "{guid("Crossing.crtilemap")}",\n'
    text += f'  "Tilesets": ["{guid("Tiles.crtileset")}"],\n'
    text += '  "Layers": ['
    text += ",".join(f'\n    {{ "Id": {i}, "Name": "{name}",\n      "Cells": [\n        {cells(layer)}\n      ] }}'
                     for i, name, layer in layers)
    text += "\n  ]\n}\n"
    (ASSETS / "Maps").mkdir(parents=True, exist_ok=True)
    (ASSETS / "Maps" / "Crossing.crtilemap").write_text(text)


# ---------------------------------------------------------------------------
# The 3D background: a sea and a turning rock
# ---------------------------------------------------------------------------

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


def write_model(name, triangles):
    """triangles: (three corners, color), flat-shaded."""
    positions, normals, colors, indices = [], [], [], []
    for corners, color in triangles:
        a, b, c = corners
        u = [b[i] - a[i] for i in range(3)]
        v = [c[i] - a[i] for i in range(3)]
        n = [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]]
        length = math.sqrt(sum(x * x for x in n)) or 1
        n = [x / length for x in n]
        for corner in corners:
            indices.append(len(positions))
            positions.append([float(x) for x in corner])
            normals.append(n)
            colors.append(list(color) + [1.0])
    buffer = Buffer()
    count = len(positions)
    attributes = {
        "POSITION": buffer.add(positions, 5126, "VEC3", "f", count, 34962, bounds=True),
        "NORMAL": buffer.add(normals, 5126, "VEC3", "f", count, 34962),
        "COLOR_0": buffer.add(colors, 5126, "VEC4", "f", count, 34962),
    }
    index_accessor = buffer.add(indices, 5123, "SCALAR", "H", len(indices), 34963)
    gltf = {
        "asset": {"version": "2.0", "generator": "Crucible samples/Crossing/make_crossing.py"},
        "scene": 0,
        "scenes": [{"nodes": [0]}],
        "nodes": [{"name": name, "mesh": 0}],
        "meshes": [{"name": name, "primitives": [{"attributes": attributes, "indices": index_accessor, "material": 0}]}],
        "materials": [{"name": name, "pbrMetallicRoughness": {"baseColorFactor": [1, 1, 1, 1], "roughnessFactor": 0.8}}],
        "accessors": buffer.accessors,
        "bufferViews": buffer.views,
        "buffers": [{"uri": f"{name}.bin", "byteLength": len(buffer.data)}],
    }
    models = ASSETS / "Models"
    models.mkdir(parents=True, exist_ok=True)
    (models / f"{name}.bin").write_bytes(bytes(buffer.data))
    (models / f"{name}.gltf").write_text(json.dumps(gltf, indent=2) + "\n")
    write_sidecar(models / f"{name}.gltf", guid(f"{name}.gltf"), {}, {}, importer="Mesh")
    # The buffer has no importer; its sidecar only keeps its ID fixed.
    (models / f"{name}.bin.crmeta").write_text(json.dumps({"Guid": guid(f"{name}.bin"), "Importer": ""}, indent=2) + "\n")


def make_background():
    # The sea: a wide plane, darker far away.
    sea = []
    size, step = 120, 20
    for i in range(-size, size, step):
        for j in range(-size, size, step):
            far = (j + size) / (2 * size)
            color = (0.06 + 0.04 * far, 0.18 + 0.08 * far, 0.36 + 0.1 * far)
            a, b, c, d = (i, 0, j), (i + step, 0, j), (i + step, 0, j + step), (i, 0, j + step)
            sea += [((a, c, b), color), ((a, d, c), color)]
    write_model("Sea", sea)
    # The rock: a faceted crystal, six sides, pointed at both ends.
    rock = []
    sides = 6
    ring = [(1.4 * math.cos(2 * math.pi * k / sides), 2.0, 1.4 * math.sin(2 * math.pi * k / sides)) for k in range(sides)]
    top, bottom = (0, 5.0, 0), (0, -1.0, 0)
    for k in range(sides):
        a, b = ring[k], ring[(k + 1) % sides]
        light = (0.55, 0.62, 0.78) if k % 2 else (0.45, 0.5, 0.68)
        rock.append(((a, top, b), light))
        rock.append(((b, bottom, a), (0.3, 0.32, 0.42)))
    write_model("Rock", rock)


# ---------------------------------------------------------------------------
# The project and the scene
# ---------------------------------------------------------------------------

# In drawing order: the ground's tiles; a hero on the ground under the deck
# (inside a Cover); the deck; everything else on the ground, y-sorted (in
# front of the deck from the south); everything on the bridge.
SORTING = ["Ground", "Under", "Deck", "Low", "High"]
UNDER, DECK_LAYER, LOW, HIGH = 1, 2, 3, 4
GROUND_BIT, BRIDGE_BIT = 1, 2


def make_project():
    project = {
        "Name": "Crossing",
        "PhysicsLayers": ["Ground", "Bridge"],
        "SortingLayers": [{"Name": "Ground"}, {"Name": "Under", "YSort": True}, {"Name": "Deck"},
                          {"Name": "Low", "YSort": True}, {"Name": "High", "YSort": True}],
        "Build": {"Scenes": [guid("Crossing.crscene")], "Include": []},
    }
    PROJECT.mkdir(parents=True, exist_ok=True)
    (PROJECT / "Crossing.crproject").write_text(json.dumps(project, indent=2) + "\n")
    (PROJECT / ".gitignore").write_text("/Cache/\n/Build/\n")
    (PROJECT / "Code").mkdir(exist_ok=True)


def t2(x, y, angle=0.0):
    return {"Crucible.Scenes.Transform2D": {"Position": [x, y], "Angle": angle, "Scale": [1, 1], "Depth": 0}}


def named(name, components, children=None):
    entity = {"Id": entity_id(), "Components": {"Crucible.Scenes.EntityName": {"Value": name}, **components}}
    if children:
        entity["Children"] = children
    return entity


def sprite(texture, region, pivot, layer, unlit=False):
    return {"Crucible.Render2D.Sprite": {
        "Texture": texture, "Region": {"X": region[0], "Y": region[1], "Width": region[2], "Height": region[3]},
        "Pivot": list(pivot), "Tint": [1, 1, 1, 1], "FlipX": False, "FlipY": False, "Unlit": unlit,
        "SortingLayer": layer, "OrderInLayer": 0}}


def collider(shape, size, offset=(0, 0), friction=0.5, trigger=False):
    return {"Crucible.Physics2D.Collider2D": {
        "Shape": shape, "Size": list(size), "Offset": list(offset), "Friction": friction, "Restitution": 0,
        "IsTrigger": trigger, "Points": [[0, 0]] * 8, "PointCount": 0, "Chain": None}}


def body(kind, layers, mask):
    return {"Crucible.Physics2D.RigidBody2D": {
        "Kind": kind, "Mass": 1, "LinearVelocity": [0, 0], "AngularVelocity": 0, "GravityScale": 0,
        "Layers": layers, "Mask": mask}}


def point_light(color, intensity, light_range, bands=0):
    return {"Crucible.Render2D.PointLight2D": {"Color": list(color), "Intensity": intensity, "Range": light_range,
                                               "Bands": bands}}


def lamp(name, x, y, layer, bands):
    return named(name, {**t2(x, y), **sprite(guid("Lamp.png"), (0, 0, 16, 32), (0.5, 0.03), layer)},
                 [named(f"{name} Light", {**t2(0, 1.6), **point_light((1.0, 0.78, 0.45), 1.3, 4.5, bands)})])


def make_scene():
    clip = lambda name: guid(f"clip {name}")
    map_id = guid("Crossing.crtilemap")
    tilemap_layer = lambda layer_id, name, sorting, collides, bits: named(name, {
        **t2(0, 0),
        "Crucible.Render2D.Tilemap": {"Map": map_id, "Tint": [1, 1, 1, 1], "SortingLayer": sorting, "OrderInLayer": 0,
                                      "Layer": layer_id, "Unlit": False},
        **(collider("Tilemap", (1, 1)) if collides else {}),
        **(body("Static", bits, bits) if collides else {}),  # its own floor only: layers collide if either mask names the other
    })
    start = (-12.5, -8.0)
    entities = [
        # The 3D: sky, sun, camera, sea, and the rock turning out at sea.
        named("Sky", {
            "Crucible.Render.Sky": {"ZenithColor": [0.12, 0.16, 0.38], "HorizonColor": [0.95, 0.55, 0.4],
                                    "GroundColor": [0.1, 0.12, 0.2], "Intensity": 1, "SunSize": 0.04,
                                    "SunIntensity": 4},
            "Crucible.Render.AmbientLight": {"SkyColor": [0.3, 0.32, 0.5], "GroundColor": [0.12, 0.12, 0.18],
                                             "Intensity": 0.8}}),
        named("Sun", {
            "Crucible.Render.DirectionalLight": {"Color": [1, 0.7, 0.5], "Intensity": 1.2, "CastsShadows": False,
                                                 "ShadowDistance": 0},
            "Crucible.Scenes.LocalTransform": {"Position": [0, 10, 0], "Rotation": [-0.2, 0.3, 0.06, 0.93],
                                               "Scale": [1, 1, 1]}}),
        named("Sea Camera", {
            "Crucible.Render.Camera": {"Projection": "Perspective", "FieldOfViewY": 0.9, "OrthographicHeight": 0,
                                       "NearPlane": 0.1, "FarPlane": 1000, "Priority": 0},
            # 14 m up, looking down 30 degrees toward +Z.
            "Crucible.Scenes.LocalTransform": {"Position": [0, 14, -20], "Rotation": [0.258819, 0, 0, 0.965926],
                                               "Scale": [1, 1, 1]}}),
        {"Id": entity_id(), "Prefab": guid("Sea.gltf"), "Overrides": {}},
        {"Id": entity_id(), "Prefab": guid("Rock.gltf"), "Overrides": {}},
        # The 2D: the dusk, the map and its layers, the lamps, the hero, the camera.
        named("Dusk", {"Crucible.Render2D.AmbientLight2D": {"Color": [0.34, 0.38, 0.62], "Intensity": 0.85}}),
        named("Crossing", {**t2(0, 0), "Crucible.Render2D.TilemapLayers": {"Map": map_id}}, [
            tilemap_layer(0, "Ground", 0, True, GROUND_BIT),
            tilemap_layer(1, "Deck", DECK_LAYER, False, 0),
            tilemap_layer(2, "Railings", DECK_LAYER, True, BRIDGE_BIT),
        ]),
        lamp("Lamp West", -13.5, -5.0, LOW, 0),
        lamp("Lamp Road", 3.5, -6.5, LOW, 3),
        lamp("Lamp North", -5.5, 5.0, LOW, 3),
        lamp("Lamp East", 12.5, 4.0, LOW, 0),
        lamp("Lamp Deck West", -7.5, 0.7, HIGH, 2),
        lamp("Lamp Deck East", 6.5, 0.7, HIGH, 2),
        named("Under the Bridge", {**t2(0, -1.5), "Crucible.Render2D.SpotLight2D": {
            "Color": [0.6, 0.8, 1.0], "Intensity": 1.0, "Range": 3.5, "InnerConeAngle": 0.6, "OuterConeAngle": 1.2,
            "Bands": 0}}),
        # Under the deck where the sunken road passes: a hero on the ground
        # with its feet in here is drawn under it. It reaches a sprite's
        # height (0.8 m) south of the deck, so a hero walking north under it
        # has its head go under first; on the stairs beside it (not covered),
        # a hero is drawn over the railings behind it.
        named("Under the Deck", {**t2(0, -0.4), "Crossing.Cover": {"Size": [6, 4.8]}}),
        # The stairs' floor triggers: their bottoms set the ground, their tops the bridge.
        named("West Stairs Bottom", {**t2(-10.7, 0), "Crossing.FloorTrigger": {"Floor": 0, "Size": [0.6, 2]}}),
        named("West Stairs Top", {**t2(-9.35, 0), "Crossing.FloorTrigger": {"Floor": 1, "Size": [0.7, 2]}}),
        named("East Stairs Top", {**t2(8.35, 0), "Crossing.FloorTrigger": {"Floor": 1, "Size": [0.7, 2]}}),
        named("East Stairs Bottom", {**t2(9.7, 0), "Crossing.FloorTrigger": {"Floor": 0, "Size": [0.6, 2]}}),
        named("Hero", {
            **t2(*start),
            **sprite(guid("Hero.png"), (0, 0, 16, 16), (0.5, 0.0625), LOW),
            "Crucible.Render2D.SpriteAnimator": {"Clip": clip("IdleDown"), "Speed": 1, "StateMachine": None},
            **body("Dynamic", GROUND_BIT, GROUND_BIT),
            **collider("Circle", (0.3, 0), (0, 0.25), friction=0),
            "Crossing.Hero": {"Speed": 4.0, "WalkDown": clip("WalkDown"), "WalkLeft": clip("WalkLeft"),
                              "WalkRight": clip("WalkRight"), "WalkUp": clip("WalkUp"),
                              "IdleDown": clip("IdleDown"), "IdleLeft": clip("IdleLeft"),
                              "IdleRight": clip("IdleRight"), "IdleUp": clip("IdleUp")},
        }, [named("Lantern", {**t2(0, 0.6), **point_light((1.0, 0.86, 0.6), 0.7, 2.5)})]),
        named("Pixel Camera", {
            **t2(*start),
            "Crucible.Render2D.PixelCamera": {"Width": 320, "Height": 180, "PixelsPerUnit": 16, "Fit": "Exact"},
        }),
    ]
    # The rock out at sea, and the camera following the hero, by their IDs.
    rock = entities[4]
    rock["Overrides"] = {rock["Id"]: {"Components": {
        "Crucible.Scenes.LocalTransform": {"Position": [-14, 0, 22], "Rotation": [0, 0, 0, 1], "Scale": [1, 1, 1]},
        "Crossing.Spin": {"RadiansPerSecond": 0.3}}}}
    sea = entities[3]
    sea["Overrides"] = {sea["Id"]: {"Components": {
        "Crucible.Scenes.LocalTransform": {"Position": [0, 0, 0], "Rotation": [0, 0, 0, 1], "Scale": [1, 1, 1]}}}}
    hero = next(e for e in entities if e.get("Components", {}).get("Crucible.Scenes.EntityName", {}).get("Value") == "Hero")
    entities[-1]["Components"]["Crossing.Follow"] = {"Target": hero["Id"]}
    scene = {"$guid": guid("Crossing.crscene"), "Entities": entities}
    (ASSETS / "Scenes").mkdir(parents=True, exist_ok=True)
    (ASSETS / "Scenes" / "Crossing.crscene").write_text(json.dumps(scene, indent=2) + "\n")


def main():
    make_project()
    make_hero()
    make_tiles()
    make_lamp()
    make_map()
    make_background()
    make_scene()
    print("Crossing: written to", PROJECT)


if __name__ == "__main__":
    main()
