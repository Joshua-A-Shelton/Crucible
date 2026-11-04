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
