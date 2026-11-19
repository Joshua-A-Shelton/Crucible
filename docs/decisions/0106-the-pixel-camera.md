# 0106 — The pixel camera

**Status:** Accepted (2026-11-19); M11 step 2 built, then tried by hand by
the owner, all but `Behind3D` (to try once sorting layers can be set from
the editor; open items). The design's names were settled before building;
those added while building were settled by the owner as drafted
(docs/naming.md).

## Context

Step 1 (ADR 0104) draws sprites at the window's resolution: exact only
when a sprite sits on the pixel grid. Between pixels it samples where it
lands and shimmers as it moves; turned or scaled, it shows steps finer than
its art ("mixels"). ADR 0005 asks for a virtual-resolution camera, integer
scaling with a sharp scaler for other window sizes, pixel snapping, and
compositing with 3D.

## Options

**Grid:** draw sprites into a texture at the virtual resolution and scale
it up (Unity's "upscale render texture", Godot's viewport mode); or draw at
the window's resolution with positions snapped (cheaper; mixels remain).

**Other window sizes:** whole-number scales only, with black bars; or a
sharp scale filling the window; or a choice.

**Cameras:** a 2D camera of its own, beside the 3D's; or one for both.

**Layering:** pixel art over the 3D only; or over and under.

## Decision

Decided by the owner (2026-11-19):

1. **Draw small, scale up.** Sprites are drawn into a texture at the
   virtual resolution, then scaled up over the finished image: rotated or
   scaled sprites stay as coarse as the art, nothing shimmers. The camera
   and every sprite snap to the grid.
2. **`PixelCamera`** (C++ `crucible::render2d`, C# `Crucible.Render2D`):
   `width`, `height` (320x180 by default), `pixelsPerUnit` (16), and `fit`
   (`PixelFit`: `Sharp`, the default, fills the window, scaling by the
   largest factor that fits and blending only across pixel edges; `Exact`
   scales by whole numbers, with black bars). Placed by its entity's
   transform; not a `Camera`, so the 3D keeps its own. Without one, sprites
   draw with the main camera as in step 1.
3. **Over and under the 3D.** A sorting layer's `Behind3D` draws it under
   the 3D: scaled up into the finished image only where the 3D drew nothing
   (the sky counts as nothing), judged per pixel from depth. The other
   layers draw over everything. Both after Tonemap, so colors stay exact.
4. **The editor's scene view** (a surface with its own camera) draws
   sprites directly at its resolution, to zoom in and edit; the Game panel
   and games go through the pixel camera. All in the `Sprites` step.

## Why

Drawing at the art's resolution is the only way a turned or scaled sprite
keeps the look its artist drew, and it makes "pixel-exact" true at any
position, not only on the grid. A sharp default fills any window, the way
players expect, while keeping each art pixel crisp; Exact is there for
games that want every pixel square. A 2D camera of its own lets a pixel
layer sit over a 3D scene seen in perspective, which step 6's
demonstration needs. Drawing behind-layers after tonemapping, where the 3D
left nothing, keeps their colors exact too; a 3D edge against them is
hard, per pixel, which suits pixel art.

## How it works

- **The camera** is the lowest entity with a `PixelCamera` and a
  `WorldTransform`. Its view looks along +Z from 1000 m before it, its
  projection the resolution's size in meters; its position is snapped so
  its bottom-left corner is on the grid.
- **Snapping a sprite:** the quad's first corner is moved onto a whole
  virtual pixel (exactly, when it isn't turned), so a sprite at 0.1 m draws
  as one at 0.
- **Two small images,** the sprites behind the 3D and those in front, each
  drawn with the pixel camera's view into a texture at its resolution,
  premultiplied (so a texel's alpha is how much the sprites covered).
  Sorting puts the behind sprites first in the instance buffer;
  `SpriteConstants` gives each draw its first sprite and its view.
- **The composite** (pixelComposite.slang: `compositeVertex`,
  `compositePixel`; a file of its own, since D3D12 maps every shader
  file's push-constant block to the one set of root constants all
  pipelines share, and a second block in sprite.slang read zeros) scales an
  image up over the finished one at `scale` window pixels a virtual pixel
  from `offset`. Sharp bilinear: each window pixel samples the middle of
  its virtual pixel, except within a window pixel of an edge between two,
  which blends them. At a whole-number scale that never happens, so the
  art comes out exactly. The behind image reads the scene's depth
  (reverse-Z: 0 where nothing was drawn; sample 0 under MSAA) and shows only
  there. The front image is always composited, for Exact's bars.
- **`setBehind3DLayers`*** tells the renderer which layers are behind, from
  `RuntimeSettings`, as y-sort does.

## Known limitations

- **One pixel camera** draws; more (split screen, picture in picture) come
  when a game needs them.
- **A turned sprite** is snapped by its first corner only; it's on the grid
  anyway, being drawn at the virtual resolution.
- **3D edges against behind-layers** are hard (one depth sample a pixel).
- **The camera's own movement** snaps to whole virtual pixels; smooth
  sub-pixel camera motion (scrolling the scaled image by fractions of a
  pixel) isn't there yet.

## As built

- Native: crucible/Sprite.h's `PixelCamera`, `PixelFit`,
  `setBehind3DLayers`, `behind3DLayers`; Sprites.cpp's pixel path
  (`FrameSprites`, `pixelCameraOf`, `addSpritePass`, `addComposite`,
  the passes PixelSpritesBehind, PixelCompositeBehind, PixelSprites,
  PixelComposite); `surfaceHasOwnCamera`; the surface slots
  `pixelFrontDescriptor`, `pixelBehindDescriptor`,
  `pixelDepthDescriptor`; sprite.slang's composite.
- C#: `PixelCamera`'s defaults; `SortingLayer.Behind3D`;
  `RuntimeSettings.Install` setting both masks.
- Tests: native, on every backend: sprites on the pixel camera's grid,
  scaled by whole pixels, snapped, and coarse when turned; Exact's bars and
  Sharp's edge blending at 72x68; behind-layers only where the 3D isn't;
  a surface with its own camera drawing directly. C#: the camera's
  defaults and the behind mask.
