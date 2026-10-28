# Compression and checksums

A learning note for M4 step 2 (ADR 0039): how zstd differs from LZ4, and
why every section of a binary file carries a CRC32C.

## Two halves of a compressor

Most general-purpose compressors, LZ4 and zstd included, start the same
way: **LZ77 matching**. They walk the data and, wherever bytes repeat
something seen recently, write "copy N bytes from D bytes back" instead of
the bytes. Repetitive data (meshes with similar vertices, padding, tables)
shrinks a lot.

**LZ4 stops there.** Its output is literals and copy commands in a simple
byte format, so decoding is little more than `memcpy` in a loop: about
4–5 GB/s per core.

**zstd adds entropy coding.** After matching, it encodes the literals and
the copy commands with Huffman coding and FSE (Finite State Entropy, an
ANS coder), which give frequent symbols fewer bits. That squeezes out
another 25–35%, at the cost of more work per byte when decoding: about
1–1.5 GB/s per core.

Higher levels (zstd 19, LZ4HC) search harder for matches when
*compressing*, which is slow, but the data they produce decodes just as
fast. That's why Crucible uses a fast level for the import cache (written
on every import) and a high level for shipped data (written once, read
millions of times).

## Why per section

Each section is its own zstd frame:

- it decompresses straight into where it's needed (later, a GPU upload
  buffer), with no intermediate copy of the whole file;
- different sections decompress on different cores at once, so even zstd
  keeps up with an NVMe drive;
- a section that doesn't shrink (random data, or BC7 textures without RDO)
  is simply stored raw.

## What compresses well

| Data | Typical result |
|---|---|
| Index buffers, meshoptimizer-encoded vertices | Large gains: those encoders are designed to feed a compressor |
| Plain BC7 / BC5 textures | Little: block compression already removed most redundancy |
| BC7 from bc7enc_rdo | Better: "rate-distortion optimization" trades a little quality for more repetition |
| Random data | Nothing: stored raw |

## Checksums

A CRC (cyclic redundancy check) is a short number computed from data, so
that almost any change to the data changes it. CRC32C ("Castagnoli") is a
32-bit CRC that x64 (SSE4.2) and ARM64 CPUs compute with a single
instruction, eight bytes at a time.

Each section stores the CRC32C of its *uncompressed* bytes, and the header
stores one over the section table:

- a bit flipped on disk in a compressed section usually makes zstd fail,
  but not always; in a raw section, nothing else would notice at all. The
  CRC catches both.
- the table's CRC means nothing in the table (offsets, sizes) is trusted
  before it's checked; separately, every offset and size is validated, so
  even a table that *passes* can't make the reader touch memory outside
  the file. The tests forge such tables on purpose.

A CRC catches accidents (bad disks, interrupted copies), not attackers: it
isn't a cryptographic hash. That's enough for a local cache, and a damaged
file is simply reimported.

## Writing safely

`writeFileAtomically` writes to a temporary name beside the file, then
renames it into place. A rename within a folder is atomic on every
supported OS, so a reader sees either the old file or the new one, never a
half-written mix, even if the editor crashes mid-write.
