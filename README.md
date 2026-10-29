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
