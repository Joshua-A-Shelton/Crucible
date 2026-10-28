# Mesh import

A learning note for M4 step 6 (ADR 0043): turning a glTF model into
Crucible's mesh format, and why mirroring is the part to get right.

## Handedness, and why one axis flips

Put your thumb, index, and middle fingers at right angles: on one hand they
point X, Y, Z one way, on the other hand the other way. glTF uses a
right-handed system; Crucible a left-handed one (ADR 0024). No rotation
turns one into the other: a model has to be **mirrored**, one axis negated.

Which axis matters. glTF says the front of an asset faces +Z and its right
is -X. Crucible's forward is +Z with +X on the right. Negating X maps both
at once: the front still faces forward, and the asset's right side lands on
+X. (Negating Z would also fix handedness, but every asset would arrive
facing backwards.)

Everything with a direction mirrors too:

| Thing | Becomes |
|---|---|
| Position, normal, tangent (x, y, z) | (-x, y, z) |
| Tangent handedness w | -w |
| Rotation quaternion (x, y, z, w) | (x, -y, -z, w) |
| Triangle (A, B, C) | (A, C, B) |

The last row is the easy one to forget. A mirror image of a
counter-clockwise triangle is clockwise, so the winding is reversed to keep
the visible side visible; Crucible's front faces are clockwise as a result.
A missed reversal turns every model inside out, which the tests check by
making sure every face of a sphere points away from its centre.

## Corners, then vertices

A GPU mesh shares vertices between triangles, but only where every
attribute matches: two triangles meeting at a hard edge or a UV seam need
separate vertices with the same position. The importer first expands
everything into **corners** (three per triangle, each with all its
attributes), which makes generating flat normals and tangents per face
easy, then **welds** identical corners back into shared vertices.

Welding compares bytes, which hides a trap: floating point has two zeros,
`0.0` and `-0.0`, equal as numbers but different as bytes. Mirroring `0.0`
gives `-0.0`, so without care, vertices that should merge stay apart. The
importer turns every `-0.0` into `0.0` first.

## Tangents

Normal maps store bumps relative to each surface point's *tangent frame*:
the normal, the tangent (where U grows), and the bitangent (where V grows),
rebuilt in shaders as `cross(normal, tangent) * w`. A normal map only looks
right if the engine builds the same frame the baking tool did. Bakers
(Blender, Substance) use **MikkTSpace**, so Crucible does too. A
"compatible" alternative in meshoptimizer differed on 17 of 3,072 corners
of a test sphere, all where the UVs were mirrored: exactly where a mismatch
shows as a seam.

## Optimizing for the GPU

meshoptimizer reorders the mesh without changing it:

- **Vertex cache:** the GPU reuses recently transformed vertices, so
  triangles that share vertices are put close together.
- **Overdraw:** triangles likely to be in front are drawn first, so fewer
  hidden pixels are shaded.
- **Vertex fetch:** vertices are stored in the order triangles first use
  them, so memory is read in order.

## Levels of detail

A distant object covers few pixels, so it can be drawn with fewer
triangles. meshoptimizer's simplifier collapses edges where it changes the
shape least, until a target triangle count or an error limit is reached.
Each LOD stores its error in meters; the renderer (M5) will pick the level
whose error is smaller than a pixel at the object's distance.

## Packing

Each attribute is its own stream, packed small:

| Stream | Format | Bytes |
|---|---|---|
| Position | 3 x float | 12 |
| Normal | octahedral, 2 x int16 | 4 |
| Tangent | octahedral + handedness, 4 x int16 | 8 |
| UV | 2 x half (float past ±4) | 4 |
| Colour | 4 x uint8, sRGB | 4 |

**Octahedral encoding** stores a unit vector in two numbers: imagine the
unit sphere squashed onto an octahedron, then unfolded into a square. It's
far more precise than storing x, y, z in the same bits, and decoding is a
few instructions in a shader.
