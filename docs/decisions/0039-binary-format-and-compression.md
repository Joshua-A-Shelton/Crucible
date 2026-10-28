# 0039 — The binary format for imported data, and compression

**Status:** Accepted (2026-10-27). Names are placeholders (docs/naming.md).

## Context

ADR 0021 makes imported data binary, in Crucible's own format, cached
locally by content hash and never hand-edited; shipped games contain only
imported data, packed by the cooker later. Importers are native (ADR 0022)
and so is the renderer that consumes their output, so the format is written
and read by C++, with C# reading it through the bindings. M4 decision K
settles the layout and loading. Compression was planned for cooked archives
in a later milestone; the project owner moved it here.

## Options

**Container:** Crucible's own sectioned container for every asset kind;
KTX2 (Khronos) for textures with our own for the rest; or one flat struct
layout per asset kind with no container.

**Loading:** read into memory, or memory-mapped.

**Damage:** a CRC32C per section, or bounds checks only.

**Old versions:** rejected and reimported (the data is a cache), or
migrated like JSON (ADR 0038).

**Compression:**

| | LZ4 / LZ4HC | zstd |
|---|---|---|
| Decompression, one core | ~4–5 GB/s | ~1–1.5 GB/s |
| Ratio, typical game data | ~2.1× | ~2.8× (level 3) to ~3.3× (level 19) |
| How | matching only | matching plus entropy coding (Huffman, FSE) |
| Licence | BSD-2-Clause | BSD-3-Clause (or GPLv2) |

Or both: LZ4 for the cache, zstd for shipping. MIT/zlib alternatives
(DEFLATE through libdeflate) compress worse than zstd and decompress slower.

## Decision

1. **Crucible's own sectioned container** for every kind of imported data:
   a header, a table of sections, and each section's bytes, aligned.
   Payloads are plain fixed-size data with offsets instead of pointers,
   little-endian, used in place once loaded.
2. **Files are read into memory;** each section is decompressed into its
   destination. Memory-mapping saves little once sections are compressed.
3. **Each section carries a CRC32C** of its uncompressed bytes, checked on
   load: a damaged cache file is found (and reimported) instead of loading
   garbage. Offsets and sizes are always validated.
4. **Old versions are rejected,** never migrated: imported data is a cache,
   and the asset database reimports stale files from source.
5. **Sections are compressed with zstd** (BSD-3-Clause, accepted by the
   project owner: shipped games include its notice), one per section, so
   each decompresses straight into its destination and sections decompress
   in parallel as jobs. A section that doesn't shrink is stored raw.
6. **The import cache is compressed too,** at a fast level; shipped data
   will use high levels. Both decompress at the same speed.

## Why

One small container gives every asset kind versioning, alignment,
validation, and compression once. In-place data makes loading a copy, not a
parse. Checksums matter because a damaged cache would otherwise show up as
corrupt textures or meshes, far from the cause. Treating imported data as a
regenerable cache avoids converter code for data that can always be rebuilt.

zstd's ratio means smaller downloads and installs; its decompression,
spread over cores per section, still keeps up with an NVMe drive. A codec
field per section leaves room for another codec without a format change.

Given up: memory-mapped loading, migrating imported data, and LZ4's faster
single-core decompression.

## As built (M4 step 2)

- **Layout** (`crucible/BinaryFile.h`, namespace `crucible::binary`):
  - A 32-byte header: magic `CRBN`, container version 1, the file's kind
    and kind version (four-letter tags such as `MESH`, stored as the
    letters' bytes in file order), the section count, the CRC32C of the
    section table, and the file's size.
  - A 48-byte entry per section: tag, version, codec (raw or zstd),
    alignment, offset, stored size, uncompressed size, and the CRC32C of
    the uncompressed bytes.
  - Payloads at their alignment (a power of two up to 4 KB), zero padding
    between them.
  - Limits: 4,096 sections; sections under 2 GB, since C# reads them into
    spans indexed by `int`.
- **Writing:** a `Writer` collects sections; `writeFile` checksums and
  compresses them, one job each, and writes the file under a temporary name
  before renaming it over the real one. A section that doesn't shrink, or
  an empty one, is stored raw. `Compression::Fast` is zstd level 3 (the
  cache), `Small` level 19 (shipping), `None` raw.
- **Reading:** `openFile` (or `parseFile` from memory) reads the whole file
  into one 4 KB-aligned buffer and checks everything a reader relies on
  before any section is touched: magic, size, versions, kind, section count,
  the table's CRC, and every section's codec, alignment, and bounds. Errors:
  `NotFound` and the new `IoError` for the file, `InvalidData` for damage or
  another kind, `Unsupported` for another container or kind version (stale:
  reimport). `readSection` decompresses into the caller's buffer and checks
  the CRC; `readSections` reads several, one job each; `rawSection` hands
  out a raw section where it lies, after checking it. A file never changes
  after opening, so any thread may read sections at once.
- **CRC32C** (`crucible/Checksum.h`): the CPU instruction on x64 (the
  x86-64-v3 baseline includes SSE4.2) and on ARM64 when the compiler
  targets it, eight bytes at a time; a table-driven form elsewhere.
- **zstd** 1.5.7, pinned by hash, static, library only (no programs,
  tests, legacy formats, dictionary builder, or threads of its own). Its
  allocations go through Crucible's allocator under a new `Assets` memory
  tag (each block keeps its size in front, since zstd's free gives none).
  zstd's CMake asks for 3.10, which turns off policy CMP0091 and with it
  the static MSVC runtime: the dependency script turns the policy on for
  it. NativeAOT executables link it like SDL. Its licence text is in
  `licenses/zstd.txt`, for shipped games' notices.
- **Files** (`crucible/Platform.h`): `fileSize`, `readFile`, and
  `writeFileAtomically`, by UTF-8 path, through SDL's IO streams.
- **C#** (`Crucible.Binary`): the generated bindings, `ImportedFile` and
  `ImportedFileWriter` (disposable wrappers), and `Tag.Of("MESH")`.
  Failures throw `CrucibleException` with the core's status.
- **Tests:**
  - Native: the standard CRC32C check value, hardware against portable
    and split runs; round trips at every level and alignment (raw sections
    in place at 4 KB); damage to the header, table, and each payload,
    truncation, another kind, and both version kinds; tables edited to point
    outside the file, with their CRC recomputed so validation alone must
    catch them; 300 rounds of random bit flips, which never read back wrong
    data; atomic writes leaving no temporary files; parallel reads; and the
    `Assets` memory returning to its starting point.
  - C# (JIT and NativeAOT): written files read back, and stale, damaged,
    and missing files throw with their status.
  - ASan, UBSan, and TSan.
- **Ratios seen in the tests:** a repeating pattern shrinks more than
  fourfold at level 3; random data stays raw.
