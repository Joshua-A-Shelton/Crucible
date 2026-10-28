# 0043 — Mesh import and the mesh format

**Status:** Accepted (2026-10-28). Names are placeholders (docs/naming.md).

## Context

ADR 0022 imports glTF through cgltf and FBX through ufbx, optimizes meshes
and generates LODs with meshoptimizer, and gives meshes Crucible's own
vertex layouts, bounds, and LODs, with textures referenced by GUID; a
file's node hierarchy becomes a prefab-like asset. ADR 0024 mirrors
right-handed sources into Crucible's left-handed space. M4 step 6 builds
glTF import; M4 decision N settles the vertex layout and the mesh format.

## Options

**Mirror axis.** ADR 0024 said to flip Z. glTF's assets face +Z, with -X
as their right; Crucible's forward is +Z with +X right. Flipping Z fixes
handedness but turns every asset around (it faces -Z); flipping X fixes it
and keeps the asset facing forward with its right side on +X.

**Vertex layout:** one stream per attribute (positions, normals, tangents,
UVs, colours, each its own array); positions alone plus the rest
interleaved; or everything interleaved as 32-bit floats. (A fully
quantized layout, positions included, loses precision on large meshes.)

**Missing tangents** (normal maps need them; glTF files often lack them):
generate them with MikkTSpace (zlib, the reference bakers match), or
require them in the source. meshoptimizer v1.3 also offers tangents
"compatible with MikkTSpace"; on a test sphere with mirrored UVs they
disagreed with MikkTSpace on 17 of 3,072 corners, each with the opposite
handedness, all where the UVs mirror: close, not identical.

**LODs:** generated now with meshoptimizer, or later with the renderer.

## Decision

1. **Mirror on X** (ADR 0024 amended): x becomes -x in positions, normals,
   and tangents, tangents' handedness flips, rotations (x, y, z, w) become
   (x, -y, -z, w), and triangle winding is reversed, so **front faces are
   clockwise**.
2. **One stream per attribute,** each its own section: positions as
   float3; normals and tangents as 16-bit octahedral vectors (tangents with
   their handedness); UVs as half floats; colours as 8-bit sRGB.
3. **MikkTSpace generates missing tangents.**
4. **LODs now,** with meshoptimizer: simplified index lists sharing the
   mesh's vertices, with settings per mesh.

## Why

Mirroring on X is the one flip that keeps glTF's forward as Crucible's, so
imported assets face the way they were authored and their left and right
are right. Separate streams let each pass read only what it needs (the
depth prepass and shadows read positions alone), make missing attributes
free, compress better, and suit bindless vertex pulling (ADR 0003); passes
that read everything make a few more fetches. Reference MikkTSpace is what
baked normal maps assume; near-misses show up as seams exactly where UVs
mirror. ADR 0022 planned LODs, and they cost little to make with the mesh.

Given up, for now: skinning data (with animation, ADR 0026), morph
targets, meshlets (with mesh shaders), and meshoptimizer's vertex and index
codecs (they would need a decoder in shipped games).

## As built (M4 step 6)

- **Dependencies** (editor-only, in `crucible_importers`): cgltf v1.15,
  meshoptimizer v1.3, and MikkTSpace (pinned by commit; zlib, its licence in
  its source), all pinned by SHA-256. The project now enables C as well, for
  MikkTSpace.
- **Reading** (`parseGltf`, `importGltf`): `.gltf` or `.glb`. Buffers in
  other files are listed for C#, which reads each as a dependency and hands
  it over (`setGltfBuffer`); cgltf never opens a file, and its allocations
  go through Crucible's allocator. Embedded buffers (`.glb`, `data:` URIs)
  are decoded natively. Files are validated before use; errors are named
  ("its JSON isn't valid glTF").
- **Each glTF mesh** becomes one Crucible mesh, its triangle primitives its
  submeshes:
  1. Every triangle corner is read with all its attributes, mirrored on X,
     with the winding reversed.
  2. Missing normals become flat ones (glTF's rule); missing tangents come
     from MikkTSpace where the primitive has UVs (a warning where it
     doesn't).
  3. Corners are welded into shared vertices. Welding compares bytes, so
     every `-0.0` (mirroring makes them) becomes `0.0` first: without it,
     mirrored meshes didn't weld, and a sphere's pole vertices looked like
     different points to the simplifier.
  4. LOD 0 is ordered for the vertex cache and for overdraw; LODs are
     simplified from LOD 0 at about 50%, 25%, … of its triangles (settings),
     each submesh separately; one that won't shrink keeps its last level, and
     levels stop when none shrinks or the error cap is reached. Each level
     stores its error in meters.
  5. Vertices are ordered for fetching across every level, then packed.
  - Lines, points, strips, and fans are left out with a warning, and so are
    skins and morph targets for now. Degenerate triangles (a UV sphere's
    poles) are kept: skinning can open them up.
- **The MESH file** (kind `MESH` version 1; the layout is in
  `crucible/importers/Mesh.h`, its structs exposed to C#): `INFO`, `MSHS`
  (per mesh: vertex range, index size, attributes, LOD range, submesh count,
  name, bounds), `LODS`, `RNGS` (per level and submesh: index range and
  material), one section per stream (`POSN`, `NRML`, `TNGT`, `UV0_`,
  `UV1_`, `COLR`), `INDX` (16-bit when a mesh has 65,535 vertices or fewer),
  `NODE` (parent, mesh, translation, rotation, scale, mirrored), `MATL`, and
  `NAME`. UV streams switch to floats when a UV leaves ±4, where halves lose
  precision.
- **Materials:** glTF's metallic-roughness values, alpha mode, cutoff, and
  double-sidedness, and five texture slots holding asset IDs (the GUID's
  bytes as .NET writes them). The C# importer resolves each texture's file,
  relative to the model, to its asset; a texture outside `Assets/`, missing,
  or embedded in the glTF is a warning.
- **The C# importer** (`MeshImporter`, name "Mesh", `.gltf` and `.glb`;
  settings: optimize, tangents, LODs, levels, ratio, error cap).
  `ImportContext` gained `IdOf` (another asset's ID by path), `Warn`, and
  `Resolve` (a path relative to the source, kept inside `Assets/`);
  `ImportResult` gained `Warnings`.
- **Measured:** a 64x32 UV sphere of 4,096 triangles gives LODs of about
  2,048, 1,024, 512, and 256 triangles with errors of 6, 9, 17, and 33 mm on
  its 2 m diameter (Linux and Windows differ by a triangle here and there,
  through floating-point rounding).
- **Tests:**
  - Native (also under ASan, UBSan, and TSan): an asset's right side
    lands on +X and it still faces +Z; front faces are clockwise; a closed
    mesh has no inside-out faces at any level (they appear when the winding
    reversal is removed); node rotations and translations mirror, from TRS
    and from matrices; source and generated tangents rebuild the right
    bitangent; corners weld; LODs shrink with growing, capped error;
    submeshes, materials, and texture paths (decoded); large UVs as floats;
    external buffers asked for and checked; lines left out; bad files
    refused; memory returned.
  - C# (through the asset database): a model with its buffer and a texture
    becomes a MESH file whose material holds the texture's ID; changing the
    buffer reimports; missing buffers fail and missing textures warn; `.glb`
    imports; paths resolve relative to the model and can't leave `Assets/`.
