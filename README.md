# Crucible

A game engine built on [Slag](https://github.com/Joshua-A-Shelton/Slag).
Currently in the specification phase; see `CLAUDE.md` and `docs/`.

## Learning notes

Longer explanations of decisions, written to be reread:

- [Storage types vs. SIMD types](docs/learning/storage-and-simd-types.md)
- [Measuring input latency](docs/learning/measuring-latency.md)
- [SIMD and data layout](docs/learning/simd-and-data-layout.md)
  (ADR 0024): why the math library has two kinds of vector types, and
  which to use.
