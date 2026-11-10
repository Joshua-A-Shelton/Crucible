# Crucible

A game engine built on [Slag](https://github.com/Joshua-A-Shelton/Slag).
Currently in the specification phase; see `CLAUDE.md` and `docs/`.

## Learning notes

Longer explanations of decisions, written to be reread:

- [Storage types vs. SIMD types](docs/learning/storage-and-simd-types.md)
  (ADR 0024): why the math library has two kinds of vector types, and
  which to use.
- [Measuring input latency](docs/learning/measuring-latency.md)
- [SIMD and data layout](docs/learning/simd-and-data-layout.md)
- [Parallel speedup and memory](docs/learning/parallel-speedup-and-memory.md)
  (ADR 0037): what the M3 demonstration's timings say.
- [Source generators](docs/learning/source-generators.md) (ADR 0036)
- [Generated serializers](docs/learning/generated-serializers.md) (ADR 0038)
- [Compression and checksums](docs/learning/compression-and-checksums.md)
  (ADR 0039): zstd and LZ4, and CRC32C.
- [The asset database](docs/learning/asset-database.md) (ADR 0040)
- [Scenes](docs/learning/scenes.md) (ADR 0041)
- [Texture import](docs/learning/texture-import.md) (ADR 0042)
- [Mesh import](docs/learning/mesh-import.md) (ADR 0043): mirroring into
  left-handed space.
- [FBX import](docs/learning/fbx-import.md) (ADR 0044)
- [Prefabs](docs/learning/prefabs.md) (ADR 0045)
- [The shader toolchain](docs/learning/shader-toolchain.md) (ADR 0047):
  DXIL and SPIR-V, and buffer layout rules.
- [The render graph and the render schedule](docs/learning/render-graph.md)
  (ADR 0048): what a render graph works out, and how steps share
  resources.
- [GPU assets](docs/learning/gpu-assets.md) (ADR 0049): uploads, resizable
  BAR, generational handles, and freeing what the GPU may still use.
- [Transforms and cameras](docs/learning/transforms-and-cameras.md)
  (ADR 0050): local and world matrices, view and projection, and why
  reverse-Z.
- [Drawing meshes](docs/learning/drawing-meshes.md) (ADR 0051): culling,
  instancing, vertex pulling, the depth prepass, the MSAA resolve, and
  measuring "only what changed".
- [Materials](docs/learning/materials.md) (ADR 0052): materials, shading
  models, and the lighting loop; generics, toon bands, and the material
  buffer.
- [Lighting](docs/learning/lighting.md) (ADR 0053): clusters, why lights
  have a range, CPU culling, and hemisphere ambient.
- [Shadows](docs/learning/shadows.md) (ADR 0054): cascades, snapping
  against shimmer, acne and bias, the tent filter, and the cached atlas.
- [Sky, transparency, and antialiasing in the shader](docs/learning/sky-and-transparency.md)
  (ADR 0055): drawing the sky last, sorting against order-independent
  transparency (with measurements), premultiplied alpha, specular AA, toon
  band AA, and alpha-to-coverage.
- [Finishing the image](docs/learning/finishing-the-image.md) (ADR 0055):
  exposure and tonemappers, resolving after tonemapping, bloom, grading,
  and inverted-hull against edge-detected outlines.
- [Comparing renders across graphics APIs](docs/learning/comparing-renders.md)
  (ADR 0056): why D3D12 and Vulkan differ, tolerances, difference images,
  and measuring before keeping an optimization.
- [Exact-size glyphs and distance fields](docs/learning/text-rendering.md)
  (ADR 0020): hinting, MSDF, and why the UI uses both.
- [Shaping and line breaking](docs/learning/shaping-and-line-breaking.md)
  (ADR 0057): glyphs and clusters, font units, and where Latin lines break.
- [Batched commands](docs/learning/batched-commands.md) (ADR 0058): C# render
  steps writing records that native code plays back, checking them once,
  and write-combined upload memory.
- [Drawing 2D with distance fields](docs/learning/drawing-2d.md) (ADR 0059):
  one quad per shape, antialiasing from the distance, blending in linear
  light, and placing exact glyphs.
- [UI layout](docs/learning/ui-layout.md) (ADR 0060): measure and arrange,
  star sizing, dirty flags, snapping edges to pixels, and anchors.
- [Styling and animation](docs/learning/styling-and-animation.md) (ADR 0061):
  value layers, specificity, transitions and tweens, easing curves, and
  real time.
- [UI input](docs/learning/ui-input.md) (ADR 0062): bubbling, capture,
  focus and scopes, spatial navigation, and stick hysteresis.
- [Controls](docs/learning/controls.md) (ADR 0063): controls made of parts,
  the popup layer, and virtualized lists with measured rows.
- [Testing a UI headless](docs/learning/testing-a-ui.md) (ADR 0064):
  scripted journeys, a testable meaning of "crisp", making sure a check can
  fail, and why captures belong in the loop.
- [Text editing](docs/learning/text-editing.md) (ADR 0065): keys versus
  typed text, carets between characters, anchors and goal columns, undo in
  groups, and scrubbing numbers.
- [Docking and windows](docs/learning/docking-and-windows.md) (ADR 0066):
  layouts as trees, drop targets, why Wayland drags windows differently,
  a Wayland connection beside SDL's, pixels versus points, and drawing
  your own window frame.
- [Viewports and picking](docs/learning/viewports-and-picking.md) (ADR 0067):
  a viewport as an image, an editor camera outside the scene, picking by
  drawing IDs, and handles a fixed size on screen.
- [Undo and hierarchies](docs/learning/undo-and-hierarchies.md) (ADR 0068):
  three ways to undo, recording edits after they're made, gestures as one
  step, scene IDs that survive deletion, and a tree drawn as a list.
- [Inspectors and metadata](docs/learning/inspectors-and-metadata.md) (ADR 0069):
  field tables instead of reflection, editing through JSON, hints as
  attributes, rotations as angles, linear and sRGB color, and overrides.
- [Drag and drop, and file watching](docs/learning/drag-drop-and-file-watching.md)
  (ADR 0071): a drag as a mode, crossing windows, a live placement preview
  from picked depth, and debouncing a folder's changes.
- [Play mode and reloading](docs/learning/play-mode-and-reloading.md) (ADR 0072):
  building a game the editor doesn't know, load contexts and unloading,
  play as a snapshot, a schedule inside a schedule, input focus, and
  reloading GPU assets while frames still use them.
- [Running standalone](docs/learning/running-standalone.md) (ADR 0073): one
  game in two hosts, the asset cache as the contract between editor and
  game, defaults over boilerplate, and reading a child process's output.
- [Native modules](docs/learning/native-modules.md) (ADR 0074): exporting a
  C++ API from a shared library, a variable per library, why there's no
  ABI promise, and why loading is easy but unloading isn't.
- [Subsystem contracts](docs/learning/subsystem-contracts.md) (ADR 0075,
  0076): where to draw a replaceable part's line, null defaults, order
  without ordering, gameplay systems, and the seams that already existed.
- [End-to-end checks](docs/learning/end-to-end-checks.md) (ADR 0077): testing
  a session rather than a feature, starting from nothing, repeatable
  captures, and checks that don't change what they measure.
- [Skinning and clips](docs/learning/skinning-and-clips.md) (ADR 0078): the
  skinning formula, weights as bytes, resampled clips and smallest-three
  rotations, testing against a reference pose, and sub-assets.
