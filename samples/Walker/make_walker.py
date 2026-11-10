#!/usr/bin/env python3
"""Makes M8's animated character (ADR 0078): Walker.gltf and its buffer in
the sample project's Assets folder.

  Walker   a figure of boxes on a 13-joint skeleton: hips, spine, head,
           upper and lower arms, upper and lower legs, and feet. Each box
           moves with its own joint, except the torso, whose lower corners
           are shared half and half between the hips and the spine.
  Walk     a one-second loop: legs and arms swing, knees bend on the way
           forward, the hips bob twice a step. Each foot is planted at its
           rearmost point: the left at 0.5 s, the right at 0.0 s.
  Idle     a two-second loop moving the spine only (breathing), so every
           other joint's tracks are constant.

glTF's convention: right-handed, Y up, counter-clockwise front faces, the
figure facing +Z. Keys every 1/16 s, linearly interpolated.

Standard library only. Run it again after changing it, and commit the output.
"""

import json
import math
import pathlib
import struct

HERE = pathlib.Path(__file__).resolve().parent
MODELS = HERE / "Project" / "Assets" / "Models"

# (name, parent, translation from the parent) in the bind pose.
JOINTS = [
    ("Hips", None, (0.0, 1.0, 0.0)),
    ("Spine", "Hips", (0.0, 0.1, 0.0)),
    ("Head", "Spine", (0.0, 0.55, 0.0)),
    ("LeftUpperArm", "Spine", (0.28, 0.45, 0.0)),
    ("LeftLowerArm", "LeftUpperArm", (0.0, -0.3, 0.0)),
    ("RightUpperArm", "Spine", (-0.28, 0.45, 0.0)),
    ("RightLowerArm", "RightUpperArm", (0.0, -0.3, 0.0)),
    ("LeftUpperLeg", "Hips", (0.12, -0.05, 0.0)),
    ("LeftLowerLeg", "LeftUpperLeg", (0.0, -0.45, 0.0)),
    ("LeftFoot", "LeftLowerLeg", (0.0, -0.45, 0.0)),
    ("RightUpperLeg", "Hips", (-0.12, -0.05, 0.0)),
    ("RightLowerLeg", "RightUpperLeg", (0.0, -0.45, 0.0)),
    ("RightFoot", "RightLowerLeg", (0.0, -0.45, 0.0)),
]
INDEX = {name: i for i, (name, _, _) in enumerate(JOINTS)}


def world(name):
    """A joint's bind position in model space (the bind pose has no rotation)."""
    _, parent, offset = JOINTS[INDEX[name]]
    if parent is None:
        return list(offset)
    base = world(parent)
    return [base[i] + offset[i] for i in range(3)]


# Boxes: (joint, centre relative to the joint, half extents, colour).
BOXES = [
    ("Spine", (0.0, 0.25, 0.0), (0.22, 0.28, 0.12), (0.25, 0.45, 0.8)),
    ("Head", (0.0, 0.12, 0.0), (0.11, 0.12, 0.11), (0.95, 0.8, 0.65)),
    ("LeftUpperArm", (0.0, -0.15, 0.0), (0.05, 0.15, 0.05), (0.25, 0.45, 0.8)),
    ("LeftLowerArm", (0.0, -0.13, 0.0), (0.045, 0.13, 0.045), (0.95, 0.8, 0.65)),
    ("RightUpperArm", (0.0, -0.15, 0.0), (0.05, 0.15, 0.05), (0.25, 0.45, 0.8)),
    ("RightLowerArm", (0.0, -0.13, 0.0), (0.045, 0.13, 0.045), (0.95, 0.8, 0.65)),
    ("LeftUpperLeg", (0.0, -0.22, 0.0), (0.07, 0.22, 0.07), (0.2, 0.2, 0.25)),
    ("LeftLowerLeg", (0.0, -0.22, 0.0), (0.06, 0.22, 0.06), (0.2, 0.2, 0.25)),
    ("LeftFoot", (0.0, -0.03, 0.06), (0.06, 0.03, 0.12), (0.35, 0.22, 0.15)),
    ("RightUpperLeg", (0.0, -0.22, 0.0), (0.07, 0.22, 0.07), (0.2, 0.2, 0.25)),
    ("RightLowerLeg", (0.0, -0.22, 0.0), (0.06, 0.22, 0.06), (0.2, 0.2, 0.25)),
    ("RightFoot", (0.0, -0.03, 0.06), (0.06, 0.03, 0.12), (0.35, 0.22, 0.15)),
]

# Each face: its outward normal and four corners as signs of the half extents,
# counter-clockwise seen from outside.
FACES = [
    ((1, 0, 0), [(1, -1, 1), (1, -1, -1), (1, 1, -1), (1, 1, 1)]),
    ((-1, 0, 0), [(-1, -1, -1), (-1, -1, 1), (-1, 1, 1), (-1, 1, -1)]),
    ((0, 1, 0), [(-1, 1, 1), (1, 1, 1), (1, 1, -1), (-1, 1, -1)]),
    ((0, -1, 0), [(-1, -1, -1), (1, -1, -1), (1, -1, 1), (-1, -1, 1)]),
    ((0, 0, 1), [(-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)]),
    ((0, 0, -1), [(1, -1, -1), (-1, -1, -1), (-1, 1, -1), (1, 1, -1)]),
]


def build_mesh():
    positions, normals, colors, joints, weights, indices = [], [], [], [], [], []
    for joint, centre, half, color in BOXES:
        origin = world(joint)
        for normal, corners in FACES:
            first = len(positions)
            for sign in corners:
                local = [centre[i] + sign[i] * half[i] for i in range(3)]
                positions.append([origin[i] + local[i] for i in range(3)])
                normals.append(list(normal))
                colors.append(list(color) + [1.0])
                # The torso's lower corners bend with the hips as much as the spine.
                if joint == "Spine" and sign[1] < 0:
                    joints.append([INDEX["Spine"], INDEX["Hips"], 0, 0])
                    weights.append([0.5, 0.5, 0.0, 0.0])
                else:
                    joints.append([INDEX[joint], 0, 0, 0])
                    weights.append([1.0, 0.0, 0.0, 0.0])
            indices += [first, first + 1, first + 2, first, first + 2, first + 3]
    return positions, normals, colors, joints, weights, indices


def quat_axis(axis, angle):
    s = math.sin(angle / 2)
    return [axis[0] * s, axis[1] * s, axis[2] * s, math.cos(angle / 2)]


def walk(t):
    """Each animated joint's rotation (and the hips' translation) at t seconds."""
    phase = 2 * math.pi * t
    swing = 0.5 * math.sin(phase)  # the left leg forward at 0.25 s, back at 0.75 s
    rotations = {
        "LeftUpperLeg": quat_axis((1, 0, 0), -swing),
        "RightUpperLeg": quat_axis((1, 0, 0), swing),
        # Knees bend while their leg swings forward.
        "LeftLowerLeg": quat_axis((1, 0, 0), 0.6 * max(0.0, math.cos(phase))),
        "RightLowerLeg": quat_axis((1, 0, 0), 0.6 * max(0.0, -math.cos(phase))),
        "LeftUpperArm": quat_axis((1, 0, 0), swing * 0.8),
        "RightUpperArm": quat_axis((1, 0, 0), -swing * 0.8),
    }
    hips = [0.0, 1.0 + 0.03 * math.cos(2 * phase), 0.0]
    return rotations, hips


def idle(t):
    return {"Spine": quat_axis((1, 0, 0), 0.04 * math.sin(math.pi * t))}, None


class Buffer:
    def __init__(self):
        self.data = bytearray()
        self.views = []
        self.accessors = []

    def add(self, values, component, kind, fmt, count, target=None, bounds=False):
        while len(self.data) % 4:
            self.data.append(0)
        offset = len(self.data)
        flat = [v for value in values for v in (value if isinstance(value, list) else [value])]
        self.data += struct.pack("<" + fmt * len(flat), *flat)
        view = {"buffer": 0, "byteOffset": offset, "byteLength": len(self.data) - offset}
        if target:
            view["target"] = target
        self.views.append(view)
        accessor = {"bufferView": len(self.views) - 1, "componentType": component, "count": count, "type": kind}
        if bounds:
            width = len(values[0])
            accessor["min"] = [min(v[i] for v in values) for i in range(width)]
            accessor["max"] = [max(v[i] for v in values) for i in range(width)]
        self.accessors.append(accessor)
        return len(self.accessors) - 1


def main():
    buffer = Buffer()
    positions, normals, colors, joints, weights, indices = build_mesh()
    count = len(positions)
    attributes = {
        "POSITION": buffer.add(positions, 5126, "VEC3", "f", count, 34962, bounds=True),
        "NORMAL": buffer.add(normals, 5126, "VEC3", "f", count, 34962),
        "COLOR_0": buffer.add(colors, 5126, "VEC4", "f", count, 34962),
        "JOINTS_0": buffer.add(joints, 5123, "VEC4", "H", count, 34962),
        "WEIGHTS_0": buffer.add(weights, 5126, "VEC4", "f", count, 34962),
    }
    index_accessor = buffer.add(indices, 5123, "SCALAR", "H", len(indices), 34963)

    # The inverse bind matrices: the bind pose is translations only.
    inverse_binds = []
    for name, _, _ in JOINTS:
        p = world(name)
        inverse_binds.append([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, -p[0], -p[1], -p[2], 1])
    inverse_bind_accessor = buffer.add(inverse_binds, 5126, "MAT4", "f", len(JOINTS))

    # Nodes: the mesh node first, then the joints.
    nodes = [{"name": "Walker", "mesh": 0, "skin": 0}]
    for name, parent, offset in JOINTS:
        nodes.append({"name": name, "translation": list(offset)})
    for i, (name, parent, _) in enumerate(JOINTS):
        if parent is not None:
            nodes[1 + INDEX[parent]].setdefault("children", []).append(1 + i)

    animations = []
    for clip, duration, pose in (("Walk", 1.0, walk), ("Idle", 2.0, idle)):
        keys = int(duration * 16) + 1
        times = [i / 16 for i in range(keys)]
        time_accessor = buffer.add(times, 5126, "SCALAR", "f", keys, bounds=False)
        buffer.accessors[time_accessor]["min"] = [0.0]
        buffer.accessors[time_accessor]["max"] = [duration]
        samples = [pose(t) for t in times]
        channels, samplers = [], []
        for name in samples[0][0]:
            output = buffer.add([s[0][name] for s in samples], 5126, "VEC4", "f", keys)
            samplers.append({"input": time_accessor, "output": output, "interpolation": "LINEAR"})
            channels.append({"sampler": len(samplers) - 1, "target": {"node": 1 + INDEX[name], "path": "rotation"}})
        if samples[0][1] is not None:
            output = buffer.add([s[1] for s in samples], 5126, "VEC3", "f", keys)
            samplers.append({"input": time_accessor, "output": output, "interpolation": "LINEAR"})
            channels.append({"sampler": len(samplers) - 1, "target": {"node": 1 + INDEX["Hips"], "path": "translation"}})
        animations.append({"name": clip, "channels": channels, "samplers": samplers})

    gltf = {
        "asset": {"version": "2.0", "generator": "Crucible samples/Walker/make_walker.py"},
        "scene": 0,
        "scenes": [{"nodes": [0, 1]}],
        "nodes": nodes,
        "meshes": [{"name": "Body", "primitives": [{"attributes": attributes, "indices": index_accessor, "material": 0}]}],
        "materials": [{"name": "Walker", "pbrMetallicRoughness": {"baseColorFactor": [1, 1, 1, 1], "roughnessFactor": 0.8}}],
        "skins": [{"name": "Walker", "joints": [1 + i for i in range(len(JOINTS))],
                   "inverseBindMatrices": inverse_bind_accessor, "skeleton": 1}],
        "animations": animations,
        "accessors": buffer.accessors,
        "bufferViews": buffer.views,
        "buffers": [{"uri": "Walker.bin", "byteLength": len(buffer.data)}],
    }
    MODELS.mkdir(parents=True, exist_ok=True)
    (MODELS / "Walker.bin").write_bytes(bytes(buffer.data))
    (MODELS / "Walker.gltf").write_text(json.dumps(gltf, indent=2) + "\n")
    print(f"Walker: {count} vertices, {len(indices) // 3} triangles, {len(JOINTS)} joints, "
          f"{len(animations)} clips")


if __name__ == "__main__":
    main()
