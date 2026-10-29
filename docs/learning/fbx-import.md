# FBX import

A learning note for M4 step 7 (ADR 0044): reading FBX through ufbx into the
same mesh format as glTF, and why the work is mostly about conventions.

## A format with no fixed convention

glTF fixes one coordinate system for every file: right-handed, Y up, assets
facing +Z, meters. FBX instead records the convention of whichever tool
wrote it, in the file's global settings:

| Tool | Up axis | Units |
|---|---|---|
| Maya | Y | centimeters |
| 3ds Max | Z | inches (or whatever the scene uses) |
| Blender | Z, converted on export as asked | meters, written as centimeters |

So "the vertex is at (0, 0, 40)" means nothing until you know the file's
axes and units: in a Max file in inches it is a point about a meter
*above* the origin. A reader has to convert every file into one common
convention before anything else can be shared.

ufbx does that conversion as it loads. Asked for `target_axes` (right-handed
Y up) and `target_unit_meters` (1), it works out the rotation and scale from
the file's settings. `space_conversion` says *where* to put that
correction:

- `TRANSFORM_ROOT`: on the root node. Vertices stay in the file's units and
  a hidden scale of 0.01 sits above everything.
- `ADJUST_TRANSFORMS`: into each node's transform.
- `MODIFY_GEOMETRY`: into each node's transform **and** the vertices.

Crucible uses the last, so a mesh's own vertices are in meters, and its
bounds and LOD errors mean what ADR 0043 says they mean.

## Normalize, then share

Once ufbx has produced glTF's convention, the FBX reader is in the same
place the glTF reader starts from. Both fill a `SourceScene` (corners,
submeshes, nodes, materials) and hand it to one builder, which mirrors on X
into Crucible's left-handed space (see mesh-import.md) and then generates
normals and tangents, welds, optimizes and packs.

The alternative was asking ufbx to convert straight into a left-handed
space, which it can. But then two pieces of code would each implement the
mirror (cgltf data mirrored by Crucible, FBX data mirrored by ufbx), and
any disagreement between them, a tangent sign or a quaternion component,
would show up only on one format's models. One mirror, tested once, can't
disagree with itself.

## Geometry transforms and pivots

FBX nodes can carry a **geometry transform**: an offset, rotation and
scale that applies to the node's mesh but *not* to its children. 3ds Max
uses it for an object's pivot: move the pivot and Max moves the mesh the
other way under it, leaving children alone. Most scene graphs, Crucible's
included, have no such thing: a node's transform applies to everything
below it.

ufbx's `MODIFY_GEOMETRY` handling bakes the geometry transform into the
mesh's vertices. That breaks only when one mesh is shared by several nodes
with different geometry transforms; then ufbx adds a small helper node
under each, holding the mesh with that offset. The imported hierarchy can
therefore have unnamed nodes the artist never made.

## Polygons, UVs, and tangents

FBX faces are polygons with any number of corners. ufbx triangulates each
(`ufbx_triangulate_face`, ear clipping that copes with concave faces); the
reader then emits three corners per triangle, as glTF's reader does.

FBX puts the UV origin at the **bottom left** (OpenGL's habit); glTF and
Crucible at the top left. So `v' = 1 - v`.

That flip has a consequence for tangents. A tangent frame is (T, B, N):
T points where u grows, B where v grows. glTF stores only T and a sign w,
and shaders rebuild `B = w * cross(N, T)`. FBX stores B itself. So:

1. In FBX's space, `w = sign(dot(cross(N, T), B))`.
2. Flipping v reverses the direction v grows, so B turns around: the sign
   is negated.

Get either step wrong and normal maps light bumps from the wrong side
along v. The test checks the result against the triangle's own UVs: T must
point where u grows on the surface, and `w * cross(N, T)` where v grows.

A nice lesson from testing this: breaking *both* the V flip and the sign at
once leaves the test passing, because the two errors cancel. Breaking
either alone fails it. Checking that each mutation fails on its own is how
you find out what a test really pins down.

## Materials and textures

FBX materials come from whatever shading model the tool used: Lambert,
Phong, Maya's Standard Surface, 3ds Max's Physical Material, Blender's
Principled BSDF. ufbx maps all of them onto one PBR description, so the
reader only reads `material.pbr`. Two gaps remain:

- Lambert has no specular at all; it reads as roughness 1.
- FBX keeps metalness and roughness in separate textures, while glTF (and
  so Crucible's material slot) packs them into one. Combining them is an
  import step of its own, left for later.

Texture paths are where FBX files most often break. They frequently hold
the artist's absolute path (`C:\Users\artist\Desktop\brick.png`) and a
relative one. The importer tries the relative path, then a file of the
same name beside the model, which covers the usual case of a model copied
together with its textures.

## Blender's scale of 100

Blender's FBX export, with default settings, writes every object with a
scale of 100 and its vertices at 1/100. In world space the model is the
right size, so Crucible keeps the file as it is. Exporting with "Apply
Scalings: FBX Units Scale" avoids it. Folding the scale into the vertices
on import is possible, but only safe when one node uses the mesh; it is an
open item until it bothers someone.

## Testing against references

ufbx ships its test files, and several come with an OBJ the same tool
exported. Comparing against those checks the whole conversion chain
(units, axes, geometry transforms, the mirror) with numbers ufbx and
Crucible didn't produce. For the 3ds Max files: (x, y, z) in inches in Max
is (x, z, -y) × 0.0254 m in glTF's space, and (-x, z, -y) × 0.0254 in
Crucible's. Every imported vertex, taken to world space through its nodes,
has to land on an OBJ vertex, and every OBJ vertex on an imported one.
