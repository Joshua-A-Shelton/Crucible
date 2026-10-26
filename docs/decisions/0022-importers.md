# 0022 — Importers

**Status:** Accepted (2026-10-25). Licenses to be confirmed from each
project's repository when it is added.

## Context

ADR 0021 split assets into source and imported forms. Importers convert
source files into Crucible's own binary formats; the source formats never
ship. Importers run only in the editor, which is stripped from shipped
games (ADR 0002), so their license notices never reach a game.

## Options

| Library | License | Role |
|---|---|---|
| cgltf | MIT | Native glTF 2.0 parser |
| SharpGLTF | MIT | C# glTF library |
| ufbx | MIT or public domain | Native FBX reader |
| Assimp | BSD-3 | Many formats; large, uneven quality |
| Autodesk FBX SDK | Proprietary | Excluded |
| meshoptimizer | MIT | Vertex-cache and overdraw optimization, LOD generation |
| stb_image | Public domain or MIT | PNG, JPG, TGA, BMP, HDR decoding |
| ImageSharp | Six Labors split license | Excluded (paid license for larger companies) |
| bc7enc_rdo | MIT or public domain | BC7, BC5, BC1–BC4 encoding |
| tinyexr | BSD-3 | EXR decoding (later, if needed) |
| Compressonator, ISPC texture compressor | MIT | BC6H encoding (later) |

## Decision

1. **Models:** glTF 2.0 is the recommended, best-supported import format,
   parsed with **cgltf**; **FBX** is also supported in v1 through
   **ufbx**. **meshoptimizer** optimizes imported meshes and generates
   LODs. Mesh import and processing are native, exposed to the C# editor
   through the binding generator (ADR 0017). Imported meshes use
   Crucible's own vertex layouts, bounds, and LODs, with textures
   referenced by GUID. A file's node hierarchy also produces a prefab-like
   asset of entities (ADR 0012, 0021).
2. **Textures:** **stb_image** decodes. **Mipmaps are Crucible's own,**
   filtered in linear space with a sharp filter (Kaiser or Lanczos), not a
   box filter. **bc7enc_rdo** compresses: BC7 for color, BC5 for normal
   maps, BC1–BC4 where they fit. HDR textures are stored as uncompressed
   RGBA16F in v1.
3. **Audio importers** are decided with the audio library; **shader
   compilation** with the materials ADR.

## Why

glTF is an open interchange format that Blender exports cleanly, and FBX
covers existing content (Mixamo animations, asset stores). Native import
keeps the heavy work fast while the editor flow stays C#. Mipmaps
filtered in sRGB come out darker, and box-filtered mipmaps look soft at a
distance, which works against goal 5. Steam Deck's GPU supports every BC
format.

Given up, for now: EXR input and BC6H compression for HDR textures.
