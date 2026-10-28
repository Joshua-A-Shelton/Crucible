# 0042 — Texture import

**Status:** Accepted (2026-10-28). Names are placeholders (docs/naming.md).

## Context

ADR 0022 picked stb_image to decode, Crucible's own linear-space sharp
mipmaps (Kaiser or Lanczos), and bc7enc_rdo to compress: BC7 for colour,
BC5 for normal maps, BC1–BC4 where they fit, RGBA16F for HDR. ADR 0040 gave
importers their place in the asset pipeline, and ADR 0039 their output
format. M4 step 5 builds the first real importer. The decoders and encoders
are native and editor-only.

## Options

**Where the native import code lives:** a separate editor-only native
library beside the core, with its own generated bindings; or inside the
core behind a build option.

**Formats offered:** BC7, BC5, BC4, RGBA8, and RGBA16F; or those plus BC1.

**Mipmap filter:** a Kaiser-windowed sinc (NVIDIA's texture tools): sharp,
little ringing; or Lanczos-3: slightly sharper, slightly more ringing.

**BC7 rate-distortion optimization (RDO):** on by default at a moderate
setting, or off by default.

## Decision

1. **A separate native library, `crucible_importers`,** editor-only like
   `Crucible.Assets`, which binds to it. Shipped games never carry decoders
   or encoders. The binding generator learns to target a second library
   (ADR 0017's amendment).
2. **BC7, BC5, BC4, RGBA8, and RGBA16F.** BC7 matches BC3's size with
   better quality; BC1's half size isn't worth a second colour path yet.
3. **A Kaiser-windowed sinc** for mipmaps.
4. **RDO on by default at a moderate setting** (lambda 1), per texture in
   its sidecar.

## Why

Keeping import code out of the core keeps shipped games small and their
dependencies few. The formats cover what current hardware does well: BC7
for anything colour, BC5 and BC4 for one or two channels, uncompressed for
UI's exact pixels and for HDR. Kaiser is the established sharp filter with
the fewest halos. RDO makes BC7 shrink much further under zstd for a small,
tunable quality cost, which matters for download sizes.

Given up, for now: BC1 and BC3, BC6H (HDR stays uncompressed, ADR 0022),
cubemaps and texture arrays (with lighting in M5), and Lanczos.

## As built (M4 step 5)

- **The library** (`src/native/importers`, namespace `crucible::importers`,
  C# `Crucible.Importers`): shared beside the shared core (and in the same
  folder, so whatever finds one finds the other), static for its native
  tests. It runs its work on the core's job system, so the core now exports
  `jobs::parallelFor` and `workerCount`.
- **Dependencies:** stb_image (v2.30) and bc7enc_rdo, pinned by commit and
  SHA-256 (neither tags releases). Only bc7enc_rdo's encoder, decoder,
  rgbcx (BC4/BC5), and ERT (RDO) are compiled; its ISPC encoder (Apache
  2.0) and LodePNG aren't. Both are MIT or public domain; editor-only, so
  shipped games need no notice. Two upstream problems, worked around in the
  build rather than patched: `ert.h` uses `uint8_t` without including
  `<cstdint>` (force-included), and ERT's hash reads `uint16_t` at odd
  addresses (alignment checks are off for that target in sanitizer builds).
- **The pipeline** (`importTexture`):
  1. Decode PNG, JPEG, TGA, or Radiance HDR, at 8 or 16 bits per channel;
     stb's allocations go through Crucible's allocator.
  2. Into filter space: colour to linear light with premultiplied alpha,
     normals to [-1, 1], data as stored.
  3. Each mip from the last: half the size (at least 1), filtered across
     then down with a Kaiser-windowed sinc (radius 3, beta 4), wrapping or
     clamping at the edges. Normals are renormalized each step.
  4. To output: straight alpha, sRGB for colour; with alpha coverage kept,
     each mip's alpha is scaled (by binary search) so the share of texels at
     or over the cutoff matches the top mip's.
  5. Encode each mip block by block as jobs, edge blocks repeating their
     last texels; BC7 uses perceptual weights for sRGB colour. RDO runs over
     fixed 1,024-block ranges, never one per thread, so the bytes don't
     depend on the worker count. (Debug and release builds can differ very
     slightly, through floating-point optimization; each build is
     deterministic, and the cache is per machine.)
  - Usages: Color (BC7 sRGB), Data (BC7 linear), NormalMap (BC5),
    SingleChannel (BC4, from red), Hdr (RGBA16F), Ui (RGBA8 sRGB).
  - Errors carry a reason (`lastImportError`): an unreadable image, an HDR
    image without usage Hdr, sizes over 16,384, or block formats on sizes
    that aren't multiples of 4 (resize, or use Ui).
  - `decodeTexels` decodes BC7, BC5, BC4, and RGBA8 back to RGBA8, for
    previews and tests.
- **The C# importer** (`Crucible.Assets.Importers.TextureImporter`, name
  "Texture"): `.png`, `.jpg`, `.jpeg`, `.tga`, `.hdr`; settings in the
  sidecar (`TextureImportSettings`: usage, mipmaps, wrap, alpha coverage
  and cutoff, RDO lambda, quality 0–4, default 2). `.hdr` files default to
  usage Hdr (importers can now choose a new file's settings by path). It
  writes kind `TXTR` version 1: an `INFO` section (format, size, mip count,
  sRGB) and one section per mip, `MIP0` … `MIPF`.
- **Measured** (the native tests print these; the same on Linux and
  Windows):

  | | Result |
  |---|---|
  | BC7, 256² gradients and waves | 43.6 dB PSNR |
  | BC7 with RDO (lambda 1) | 56 KB → 35 KB after zstd (38% smaller), 39.3 dB |
  | BC5 normal map of bumps | mean error 0.8°, worst 4.2° |
  | BC4 height | 47.3 dB |
- **Tests:**
  - Native (also under ASan, UBSan, and TSan): a checkerboard's mips are
    sRGB 188 (linear 0.5), not 128; mip counts and sizes; the quality
    above; normal mips stay in the unit disc; alpha coverage held at 0.29
    while plain averaging goes 0.29, 0.16, 0.03, 0; refused sizes, bad data,
    and HDR misuse, with their reasons; HDR as half floats; identical bytes
    with and without workers; the Assets memory returning to zero.
  - C# (through the asset database): a TGA becomes a `TXTR` file with every
    mip; changing the usage reimports as BC5; `.hdr` defaults to Hdr;
    failures carry the native reason; the importers' layout checks pass.
