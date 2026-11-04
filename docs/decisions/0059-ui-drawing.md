# 0059 — UI drawing

**Status:** Accepted (2026-11-03); built. Names settled by the owner
(docs/naming.md).

## Context

ADR 0019 draws the UI through the batched command API (ADR 0058), with a
custom-draw element that gives plugin code an immediate-style drawing API.
ADR 0020, amended, keeps exact-size glyphs by default and distance fields
for large, scaled, turned, or animated text; ADR 0057 built the glyph cache
on the CPU. M6 step 3 decides Y: the 2D drawing primitives.

## Options

**Where text becomes quads:** native code, one call per layout, writing
glyph instances straight into the frame data; or C#, per glyph, reading
positions and cache entries across the boundary.

**Batching and clipping:** one shader for every primitive, with a clip
rectangle per instance; or a pipeline per kind, with scissor commands.

**Blending:** in linear light through the backbuffer's sRGB view, with
text coverage corrected; or in gamma space like browsers, through a
non-sRGB view.

**Primitives beyond rectangles, images, text, clips, and transforms:**
lines and polylines, gradients, drop shadows, nine-slice images.

## Decision

1. **`Canvas` is the drawing API:** `FillRect` (solid or gradient, a
   radius per corner), `StrokeRect`, `DrawImage`, `DrawNineSlice`,
   `DrawText`, `DrawLine`, `DrawPolyline`, `DrawShadow`, `PushClip` /
   `PopClip`, `PushTransform` / `PopTransform`. Positions are logical
   units; `Scale` (the DPI scale) and the transform stack map them to
   physical pixels. Colors are sRGB with straight alpha.
2. **A canvas is also the render step that draws it** (`IRenderStep`),
   normally added as `UI` after Tonemap: it draws onto the backbuffer, one
   sample, antialiased in the shader.
3. **One shader, one instance per primitive** (`UiInstance`,
   `shaders/crucible/ui.slang`), with its kind, a texture slot, and a clip
   rectangle: usually one instanced draw for the whole canvas, in the order
   drawn. Shapes are signed distance fields in the quad's local space.
4. **Text becomes quads natively,** one call per layout
   (`text::writeTextQuads`), straight into the frame data.
5. **Blending is in linear light,** premultiplied, through the
   backbuffer's sRGB view, like the rest of the frame; text coverage is
   corrected toward how gamma-space blending looks.
6. **The glyph cache's pages live on the GPU,** uploaded by the
   `GlyphUpload` pass: only the rectangle that changed in each page.

## Details

- **Instances:** 160 bytes: a local rect, a 2×2 transform and translation,
  kind, texture slot, corner radii, two colors, a uv rect, two parameter
  vectors, and the clip in physical pixels. C# writes them as the
  generated `UiInstance` mirror (shaderfax), so layouts can't disagree.
- **Antialiasing:** coverage is `0.5 − distance / fwidth(distance)`, so an
  edge is one pixel wide whatever the transform; quads grow by a pixel (in
  local units) to make room. An edge on a pixel boundary is crisp; one half
  a pixel in is half covered.
- **Shadows:** Evan Wallace's closed form for a blurred rounded box (erf
  along x, four samples along y); `blur` is CSS's blur radius, twice the
  Gaussian's sigma.
- **Gradients** interpolate in linear light, so a black-to-white ramp is
  brighter in the middle than CSS's (sRGB 188, not 128).
- **Lines** are capsules (round caps); polylines are segments, so
  translucent ones darken where segments overlap.
- **Nine-slice:** the corners keep their size, the edges and middle
  stretch, in one quad (the shader maps each axis piecewise).
- **Clipping:** an axis-aligned box in whole physical pixels around the
  rectangle as transformed, intersected with the clip pushed before; the
  pixel shader multiplies by it rather than discarding, so derivatives
  stay defined at its edge.
- **Text:**
  - exact glyphs are point-sampled: when the transform is the layout's
    DPI scale and a translation, the layout's origin is rounded to a
    pixel and each glyph is placed texel for texel; otherwise they're
    drawn through the transform (and look soft: use distance fields);
  - distance-field glyphs are median-decoded with the field's range in
    screen pixels from `fwidth`;
  - coverage is raised to a power from 1/1.45 (black text) to 1.45 (white
    text), by the color's luminance: blending in linear light alone makes
    dark text on light thin and light text on dark bold;
  - a glyph without an image yet (a field generating, a page made during
    the frame's steps) is left out, and appears a frame or two later.
- **Glyph pages on the GPU:** a texture per CPU page, each with a fixed
  resource-heap slot past the surfaces' (`glyphPageDescriptors`). The
  `GlyphUpload` pass is declared before the steps run (importing every
  page) and filled after them, so glyphs made while steps draw still
  arrive before the UI samples them. Changed rectangles are staged in an
  upload buffer destroyed after the frame. The glyph cache's frame now
  advances once per rendered frame (`updateGlyphCache`).
- **Pipelines from engine shaders:** `createEngineGraphicsPipeline`, for
  the engine's C#.
- **Lifetime:** a canvas holds what's drawn until `Clear`; text layouts it
  draws must outlive it. Main thread only.

## Why

One shader and one draw keep the UI's cost flat however many primitives
it has, and keep submission order the drawing order with no batching
rules. Signed distance fields give every shape antialiasing and rotation
for free. Native text quads avoid copying glyph data across the boundary.
Linear blending matches the rest of the frame and leaves room for HDR
output; the text correction recovers most of what gamma-space blending
gives text.

Given up: CSS-identical gradients and blending; per-instance rounded
clipping; mitered and beveled polyline joints; gradients with more than
two stops.

## As built (M6 step 3)

- Native: `shaders/crucible/ui.slang` (embedded); `GlyphUpload.cpp`;
  `writeTextQuads` and `textGlyphCount` in `Shaping.cpp`;
  `createEngineGraphicsPipeline` in `RenderCommands.cpp`.
- C#: `Crucible.UI.Canvas`, `Gradient`, `GradientKind`.
- Tests:
  - native: text quads wait for page textures; on every backend, exact
    glyphs land on whole pixels at the layout's scale, are left to a
    turned transform, and distance-field glyphs come from their pages;
  - C# (JIT and NativeAOT): a canvas's bookkeeping;
  - `launcher.jit.canvas`: every primitive drawn on a white background
    and checked at 26 pixels (crisp and half-covered edges, rounded
    corners, borders, both gradients, lines, a shadow's fade, clipping, a
    rotated square, image and nine-slice texels, exact and distance-field
    text), saved as `canvas.png`.
