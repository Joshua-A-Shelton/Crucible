# Texture import

A learning note for M4 step 5 (ADR 0042): what happens between a PNG and a
GPU texture, and why each step is there.

## Linear light

An 8-bit colour value in an image file isn't an amount of light. It's sRGB:
a curve that spends more of the 256 steps on dark tones, where eyes are
sensitive. sRGB 128 is only about 22% as bright as 255, not 50%.

That matters whenever pixels are *mixed*. Shrink a black-and-white
checkerboard by averaging the stored values and you get 128: a grey that's
too dark. Average the *light* instead (convert to linear, average, convert
back) and you get 0.5 linear, which is sRGB 188, the grey you see when you
squint at the checkerboard. The importer converts colour textures to linear
floats first, filters there, and converts back at the end; the first test
checks the 188.

Data textures (roughness, masks, normals) are already linear and skip this.

## Mipmaps

A mipmap chain is the texture at half size, quarter size, and so on down to
one texel. The GPU samples the level whose texels match the screen's
pixels, so distant surfaces don't shimmer.

Each level is made by filtering the one above. A 2x2 box average is the
simplest filter and blurs a lot. A **windowed sinc** is the mathematically
ideal low-pass filter (sinc) cut off smoothly (the window): sharper mips,
with a little ringing at hard edges. The Kaiser window has one parameter
trading sharpness against ringing; Crucible uses NVIDIA's defaults
(radius 3, beta 4).

Three details the filter needs:

- **Premultiplied alpha.** Colour is multiplied by alpha before filtering,
  so the colour of fully transparent texels (often black) doesn't bleed
  into the edges of what's visible.
- **Normals** are directions, not colours. After averaging, a normal is
  shorter than 1, so every mip is renormalized.
- **Alpha coverage.** For alpha-tested surfaces (leaves drawn where alpha is
  over 0.5), averaging pulls alpha towards the middle, and smaller mips
  have fewer and fewer texels over the cutoff: the test's leaves go from
  29% covered to 0% in three mips. The importer scales each mip's alpha so
  the share over the cutoff stays the same.

## Block compression

GPUs read compressed textures directly, in 4x4-texel blocks:

| Format | Bytes per block | Holds | Used for |
|---|---|---|---|
| BC7 | 16 (1 byte/texel) | RGBA, high quality | colour, data |
| BC5 | 16 | two channels | normal maps (X, Y; Z is rebuilt) |
| BC4 | 8 | one channel | height, occlusion, masks |

Uncompressed RGBA8 is 4 bytes per texel, so BC7 is a quarter of the size
(and of the memory bandwidth). BC7 encoding searches many ways to encode
each block, which is slow, so blocks are encoded in parallel on the job
system. Since a format stores whole blocks, compressed textures must be a
multiple of 4 on each side.

## RDO: making blocks friendlier to zstd

On disk, the BC7 data is compressed again with zstd (ADR 0039), but BC7
blocks look nearly random to zstd. **Rate-distortion optimization** nudges
blocks to repeat byte patterns from nearby blocks when that costs little
visible quality. In the tests, lambda 1 made the file 38% smaller after
zstd, for about 4 dB of PSNR (from 43.6 to 39.3 dB). Higher lambda trades
more quality for size; 0 turns it off, per texture.

## Measuring quality

Tests decode the compressed texture again and compare it with the source
using PSNR (peak signal-to-noise ratio): 40 dB and above is hard to tell
apart by eye, 30 dB is visibly degraded. Normal maps are measured as angle
error instead: under a degree on average.
