# 0104 — Sprites

**Status:** Accepted (2026-11-18); M11 step 1 built, awaiting the owner's
try (names marked * were added while building it and are placeholders
until then). The design's names were settled before building
(docs/naming.md).

## Context

ADR 0005 gives pixel art a dedicated 2D renderer: sprite batching,
draw-order sorting (layers, order in layer, y-sort), and 2D lighting, able
to composite with 3D. M9 brought 2D gameplay (Transform2D, Box2D), but 2D
entities could only draw as meshes through the 3D renderer. M11 step 1 is
the sprite: a picture drawn at a Transform2D, sorted the way 2D games
sort.

## Options

**Where a sprite's picture comes from:** a texture and a pixel rectangle
on the component; or sprite assets sliced from a texture by an importer
(Unity's model).

**Sorting:** named sorting layers in the project, each choosing whether it
y-sorts, with an order number within a layer (Unity); or a plain number
per sprite, y-sort per parent node, and scene-tree order (Godot). An ECS
has no tree order, so the choice was named or numbered.

**Size:** pixels per unit per texture (Unity), or one number for the
project.

**Where in the frame:** after tonemapping, colors exactly the texture's;
or with the 3D before it, sharing bloom and exposure.

## Decision

Decided by the owner (2026-11-18):

1. **A texture and a rectangle.** `Sprite` (C++ `crucible::render2d`, C#
   `Crucible.Render2D`): `texture`, `region` (`SpriteRegion`*: x, y,
   width, height in pixels from the texture's top-left; all of it when
   zero), `pivot` ((0, 0) bottom-left to (1, 1) top-right; centered by
   default), `tint` (white by default), `flipX`, `flipY`, `sortingLayer`,
   `orderInLayer`. Step 3 cuts sheets into frames that fill the region.
2. **Named sorting layers in the project.** The project file's
   `SortingLayers`: an ordered list (first draws first; "Default" alone
   when there are none), each with `Name` and `YSort`. Within a layer,
   `orderInLayer`, then, where the layer y-sorts, lower on screen in
   front. `Transform2D.depth` doesn't order sprites. Edited in the project
   file, like physics layers.
3. **Floors by layers.** A map with levels (a bridge walked under and
   over) is bands of layers per floor; gameplay moves a character between
   them at triggers, with its physics layer. `sortingLayer` is a plain
   field for that, and `RuntimeSettings.SortingLayer(name)` finds a layer
   by name. Faking 2D in 3D (sprites in a depth-tested 3D world) suits
   continuous height but fights the pixel grid; that style stays on the 3D
   renderer (ADR 0005).
4. **Pixels per unit, per texture:** an import setting, `PixelsPerUnit`
   (16 by default). A texture usage `Sprite` imports the source's bytes
   exactly: RGBA8 sRGB, no mipmaps, no wrapping (compression shifts pixel
   art's colors; mipmaps blur it). Sprites always sample nearest.
5. **After tonemapping:** the `Sprites` render step draws over the
   finished image, so colors are exactly the texture's; the existing
   Camera, orthographic, is the 2D camera until step 2.
6. **Runtime settings** (asked while building: nothing project-wide had
   reached a shipped game before). The cook packs a settings entry
   (`RuntimeSettings`, under `RuntimeSettings.Id`), for now the sorting
   layers; a game installs them as it starts (`RuntimeSettings.Current`),
   from the archive, or from the project file in editor and standalone
   runs. `render2d::setYSortLayers(mask)` tells the renderer which layers
   y-sort (up to 32 layers).

## Why

A texture and a rectangle work today and leave slicing to step 3, where
sheets are needed anyway. Named layers make scenes and gameplay code
readable ("Bridge characters", not 7), and reordering the list reorders a
game at once; a y-sort switch per layer serves a top-down game's character
layer without sorting its background. Pixels per unit on the texture keeps
physics sizes sensible (a 16-pixel tile is a meter) and lets art at other
resolutions mix in. Drawing after tonemapping is what makes "pixel-exact"
possible at all. A settings entry gives later runtime settings a home.

## How it works

- **One draw call for every sprite.** Each frame the step gathers the
  world's sprites, sorts them (layer, order, height where the layer
  y-sorts, then entity, so equal sprites keep their order), and writes one
  `SpriteInstance`* each into an upload buffer: the quad's corners in
  local meters (pivot applied), the entity's 2D axes and position from
  its world matrix, the region's texture coordinates (swapped when
  flipped), the tint, and the texture's heap slot. One instanced draw
  draws them all, each reading its own texture through the bindless heap,
  so there's nothing to break batches on.
- **Exact colors:** sprite textures are sRGB RGBA8, sampled point-clamped;
  the backbuffer is sRGB, so a texel decodes to linear and encodes back to
  the same byte. Straight alpha blends over the image.
- **Textures are acquired on first use** and released once no sprite has
  shown them for 240 frames; a load starter (ADR 0099) makes scene loads
  wait for them. A texture's `SPRT` section holds its pixels per unit
  (16 if a file has none).
- **Gathered on the main thread, once a frame:** the editor's Pick step
  draws the same sprites into its entity IDs (`addSpritePicks`*), over the
  meshes and only where a sprite shows (alpha 0.5 and up), so clicking a
  sprite selects it and its outline follows.
- **The Inspector** picks `sortingLayer` by the project's names, through a
  new hint (`CRUCIBLE_HINT_SORTING_LAYER`*, C# `[SortingLayer]`*).

## Known limitations

- **No pixel snapping yet:** a sprite between pixels samples where it
  lands. Step 2's virtual resolution and snapping make it exact at every
  position; today it's exact when positions sit on the pixel grid.
- **Sorting layers are edited in the project file;** no window for them
  (nor for physics layers) yet.
- **Up to 32 sorting layers** have their y-sort switch honored.

## As built

- Native: crucible/Sprite.h (`Sprite`, `SpriteRegion`*, `registerSprite`,
  `setYSortLayers`, `ySortLayers`*), Sprites.cpp (the step, the texture
  cache, `startSpriteLoads`*), shaders/crucible/sprite.slang
  (`spriteVertex`*, `spritePixel`*, `spritePickPixel`*); the texture
  usage `Sprite` and the `SPRT` section*; `texturePixelsPerUnit`* and
  `isTextureLoading`*; the step name `Sprites`.
- C#: `Crucible.Render2D.Sprite`'s defaults; `SortingLayer`,
  `RuntimeSettings` (`Id`, `Current`, `Install`*, `FromJson`*,
  `SortingLayer(name)`); the project's `SortingLayers` and
  `RuntimeSettings`*; `TextureImportSettings.PixelsPerUnit`; the
  serializer's `IsSortingLayer`*; the Inspector's sorting-layer picker.
- Tests: native SpriteTests (exact colors by region, pivot, flips, and
  pixels per unit; sorting by layer, order, and y; tint and alpha; the
  editor's picks), on every backend; C# a sprite texture's exact texels
  and pixels per unit, the project's sorting layers through the cook, and
  the runtime settings and Sprite defaults.
