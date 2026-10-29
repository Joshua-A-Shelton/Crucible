# 0044 — FBX import

**Status:** Accepted (2026-10-28). Names are placeholders (docs/naming.md).

## Context

ADR 0022 imports FBX through ufbx (MIT or public domain) into the same
mesh format as glTF (ADR 0043). Unlike glTF, which fixes one convention
(right-handed, Y up, assets facing +Z, meters), every FBX file carries its
own: Maya exports Y-up, 3ds Max Z-up; units are often centimeters; faces
are polygons; UVs start at the bottom left; nodes have pivots and geometry
transforms; materials come from whichever tool made them; and textures are
often absolute paths on the artist's machine. ufbx reads each file's
settings and can convert at load: axes, units, geometry transforms, pivots,
and any material to a PBR description.

## Options

**Where handedness is converted:** ufbx normalizes FBX to glTF's
convention, then the mirroring step shared by every format runs; or ufbx's
own handedness conversion produces Crucible's space directly.

**Units and axes:** baked into vertices and node transforms; or kept as a
root node's transform, meshes staying in the file's units.

**Importer:** a separate "Fbx" importer sharing the mesh settings and
file format; or one importer for every model format.

**Texture paths:** the stored relative path, then a file of the same name
beside the model; or the relative path only.

## Decision

1. **Normalize, then share:** ufbx converts each file to glTF's convention
   (right-handed, Y up, +Z front, meters); the mesh builder then mirrors on
   X and does everything else exactly as for glTF (ADR 0043). The glTF
   reader no longer mirrors as it reads.
2. **Units and axes are baked into geometry and transforms:** meshes are in
   meters, Y up, with no hidden root scale.
3. **A separate "Fbx" importer,** sharing `MeshImportSettings` and the
   MESH writer: updating ufbx reimports only FBX files.
4. **Textures by relative path, then by name beside the model;** otherwise
   a warning.

## Details

- **The shared builder.** Every format's reader fills a source scene in
  glTF's convention (`MeshBuilder.h`); `buildMeshImport` mirrors it and
  does the rest. `Gltf.cpp` and `Fbx.cpp` are the readers, `Mesh.cpp` the
  builder; the MESH layout is unchanged.
- **ufbx's load options:** `target_axes` right-handed Y up and
  `target_unit_meters` 1, with `space_conversion` `MODIFY_GEOMETRY`;
  geometry transforms (3ds Max's object offsets) `MODIFY_GEOMETRY`, which
  adds unnamed helper nodes only for meshes several nodes share; Maya's
  segment scale compensation undone in the children (`COMPENSATE`); no
  external files opened. ufbx's implicit root node is dropped.
- **Polygons** are triangulated by ufbx (ear clipping), one submesh per
  material part. Point and line faces are left out with a warning.
- **UVs** flip V (FBX's origin is the bottom left). Source tangents are
  kept; their handedness comes from the stored bitangent's side of the
  normal and tangent, reversed by the V flip.
- **Vertex colours** are read as sRGB, as the tools that paint them show
  them; glTF's are linear.
- **Materials** come from ufbx's PBR view of any shading model. Lambert,
  with no specular, is fully rough. FBX keeps metalness and roughness in
  separate textures, which glTF's packed texture can't hold yet: they are
  left out with a warning. Per-instance material overrides are ignored.
- **Textures** embedded in the FBX are reported; their stored path is
  still tried, since tools that embed often extract beside the file.
- **Blender's default export** scales each object by 100 and its vertices
  by 1/100. World sizes are right, and kept as the file says.

## Why

One mirroring implementation, tested once, can't disagree with another;
each format's reader only has to reach a well-defined common convention.
Baked units keep bounds and LOD errors in meters, as ADR 0043 promises.
A separate importer keeps a ufbx update from reimporting every glTF model.
Looking for a texture by name covers the usual case of a model moved
together with its textures.

Given up, for now: skins, blend shapes, animation, cameras, and lights
(with animation, ADR 0026), textures embedded in the FBX, separate
metalness and roughness textures, and per-instance materials (open
items).
