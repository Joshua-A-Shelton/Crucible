# 0132 — Asset thumbnails

**Status:** Accepted (2026-11-25); step 4 of the editor's look
(milestones). Names are placeholders until the owner settles them
(docs/naming.md).

## Context

The Assets panel's tiles showed a texture's own image, and otherwise a
64px drawing by kind: a cube for every model and prefab, a page for most
files (sounds, state machines, chains, tilemaps, and tilesets all read as
"File"). Telling the Diorama's models apart meant reading their names.
ADR 0071 left rendered thumbnails for later; the look pass's plan
(milestones) called for them, rendered once and cached.

## Options

**The shot:** a three-quarter view on a clear background; the same on a
dark backdrop; straight from the front. **Materials:** on a sphere; a flat
swatch; an icon. **The cache:** the project's Cache folder, by import key;
memory only. **Other kinds:** Lucide icons per kind; tilemaps and tilesets
rendered; a tile-size slider; a kind badge on thumbnails. **A sphere with
a material it wasn't imported with:** a MaterialOverride component in the
engine; a hidden import per material; a swatch for now. **When:** visible
tiles first, a few a frame; the whole project at open. **The slider and
badge:** 48 to 160px with a badge at the bottom right; fixed steps.

## Decision

Decided by the owner (2026-11-25):

1. **A three-quarter view on a clear background:** from the front-left
   and a little above, lit by the preview's key and fill lights (ADR
   0125). A prefab of sprites, or a tilemap, is shot flat in 2D.
2. **Materials on a sphere**, through a new engine component,
   **`MaterialOverride`** (C++ and C#, "Rendering", icon `paintbrush`):
   the entity's MeshInstance draws with that material in place of every
   slot's own. Games get it too (a paint swap, a hit flash). The sphere is
   the editor's own asset.
3. **Cached in `Cache/Thumbnails/`** by a key made from the asset's import
   and those of everything it names, so a reimport renders it again and a
   restart doesn't.
4. **Every other kind a Lucide icon**; **tilemaps rendered**, **tilesets
   showing their sheet**; **a tile-size slider**, 48 to 160px (64 by
   default), kept in editor.json as `AssetTileSize`; **a kind badge** at
   the bottom right of a rendered thumbnail, hidden below 64px.
5. **One at a time, in the order tiles ask**, in a hidden preview world,
   captured once everything it draws has loaded; the kind's icon shows
   until then.

## How it works

- **Material overrides in the renderer:** each frame, before culling,
  every instance with an override is given a variant: its model and the
  override's material, one variant for each pair. A variant's batch key
  carries the variant (a bit in the model's field) in place of the model,
  so instances of the same model and material still batch, and different
  materials don't. The batch carries the material (`DrawBatch::
  materialOverride`), which every pass draws in place of each submesh's
  own. An override waits, not drawn, until its material's textures are
  ready (as a model waits, ADR 0099); its load starter counts it, so a
  scene's first frame waits too. Override materials are acquired by asset
  ID and released two seconds after the last use, as models are, and
  all of them when the renderer stops (found on Windows, where the test
  runs D3D12 then Vulkan: a handle kept from the first waited forever in
  the second).
- **The editor's own assets:** `EditorAssets/` beside the editor holds
  `Sphere.gltf` (from `scripts/make-sphere.py`, its data embedded). The
  editor sets `Project.EditorAssetsFolder` when it opens a project, so
  the database finds and imports it under the root `Editor:`. The
  database's `Assets` listing leaves `Editor:` out, so it's never picked,
  listed, or cooked. "Editor" can't be a plugin's name.
- **Rendering one:** a `PreviewView` the shell doesn't list loads the
  asset (the sphere with a MaterialOverride for a material). Each frame it
  asks `StartLoads` whether anything is still loading (at most 10 s), then
  draws two warm-up frames. Then it fits the camera so every corner of
  the bounds is inside the image, and captures twice: on black, then on
  white.
- **A clear background from two captures:** where the two agree, the pixel
  is opaque; how far apart they are says how much background shows
  through, which gives alpha. The color is the black capture over that
  alpha. Antialiased edges come out partly clear.
- **The cache file:** the engine's own texture file (TXTR, RGBA8, sRGB),
  with mips down to 4x4, filtered by alpha so edges don't darken. It's
  written beside its name, then moved. It loads with `loadTexture` like
  any imported texture.
- **The key:** a hash of a look version, the asset's import key (its
  imported file's name), and, all the way down, the keys of what its
  import references. Prefabs aren't imported, so for a prefab: its bytes,
  and the asset IDs written in it.
- **Tiles:** a tile acquires its thumbnail when attached and releases it
  when detached. Tiles share a thumbnail by key, and a thumbnail tells its
  tiles when it's ready.

## Added while building

- **The cache holds `.crtex`, not `.png`:** a PNG would have to be decoded
  and written out again as a texture file before the renderer could load
  it. The engine's own texture file loads as it is.
- **`RenderAssets.IsLoading` wasn't needed:** `Assets.Api.StartLoads(world)`
  already says how many of a world's components wait for a load.
- **The sphere is `Sphere.gltf`, not `.glb`:** a text file with its buffer
  embedded, so the repository has no binary for it.
- **The editor imports materials:** it never registered the Material
  importer (materials came in M5, before the editor). The Shader importer
  also looked for the shader library beside the process. Under the native
  launcher that's the launcher's folder, so the editor now points it at
  its own (`ShaderImporter.LibraryFolder`).
- **`PreviewView`:** an unlisted constructor (the shell neither ticks nor
  frees it); `Capture`, a square image read back; `ContentBounds`, what
  `Frame` fits; tilemaps' bounds from their painted cells (previews
  framed tilemaps around their origin before).
- **`DrawStats::batches`** (internal): the camera's batches, for the test
  that overrides batch by model and material.
- **`Shell.SaveSettings`** is internal, for the slider.

## Consequences

- Browsing a folder of models shows what they are. The first visit
  renders each one once, about a frame each (a GPU readback stalls the
  frame it's in). After that they're read from the cache.
- Games can swap an instance's material without a new import.
- Game materials work in the editor for the first time.
- `ThumbnailChecks` (CTest `launcher.jit.thumbnails`), on a copy of the
  Diorama with a material shader, material, prefab, tileset, and tilemap
  added, checks:
  - the five models, the prefab, the material, and the tilemap render
    once, with badges;
  - around them is clear and their middle opaque;
  - the material shows in its own color, and again in its new one after a
    change;
  - forgotten thumbnails are read back from the cache;
  - the tileset shows its sheet;
  - the slider sizes tiles and is kept.

  It saves thumbnails.png and thumbnails-sheet.png (every thumbnail side
  by side). MaterialTests checks overrides on the GPU: the override's
  color, batching by model and material, and taken away. PluginTests
  checks the `Editor:` root and the reserved name.
- Not yet: thumbnails for scenes; a texture's thumbnail at its own scale
  (it's drawn from the full texture); rendering off the main thread.
