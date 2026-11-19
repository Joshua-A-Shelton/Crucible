# 0102 — The M10 demonstration

**Status:** Accepted (2026-11-18); M10 step 5 built, awaiting the owner's
try by hand (names marked * were added while building it and are
placeholders until then). The design's names were settled before building
(docs/naming.md).

## Context

M10 is done when a packaged sample runs from its archives in both
shipping modes. Steps 3 and 4 (ADR 0100, 0101) made JIT and NativeAOT
packages, and their CTests run each from a copy with no .NET to find. They
show the packages start and load; not that the game plays the same. A
NativeAOT game is compiled differently from the JIT's, its floating point
included, so a game could run and still play differently.

## Decision

Decided by the owner (2026-11-18):

1. **The Walker's Level, packaged both ways,** checked against the Cache
   as M9's check compared the archive with the Cache (ADR 0097, 0099).
   LevelChecks' new mode (game argument `check-packaged=<folder>`, CTest
   `samples.walker-packaged`), on a copy of the project: the Level run
   standalone from the Cache at 30 fps for 45 s; then the JIT package and
   the NativeAOT package built with Build and each run the same way, its
   executable started as a player starts it with only the check's options,
   the Cache's imported files moved aside. Every run: the post hit once
   inside the Strike window, every waypoint reached, never below the
   floor, a clean exit. Each package: its game loaded (JIT) or compiled in
   (NativeAOT), its 8 assets read from its archive, its hit within 0.5 ms
   of the Cache run's, and its arrival within a frame.
2. **Tried by hand** on Linux (Vulkan) and Windows (D3D12): both packages,
   windowed.
3. **No Steam Deck yet:** its check (ADR 0008) stays an open item; M10 is
   done without it.

## What it found

On Linux (Vulkan, RADV) the three runs play out identically: the post is
hit 116.457 ms into the Strike window in each, and the Walker arrives at
34.900 s in each, never lower than 5 mm below the floor. On Windows
(D3D12, NVIDIA) the Cache and JIT runs match those, and the NativeAOT
package's hit comes 116.456 ms in: a microsecond earlier, from code
compiled differently, well inside the half millisecond allowed, with the
same arrival. NativeAOT's code computes the same animation, physics, and
route as the JIT's, to within rounding.

## As built

- `LevelChecks`' packaged mode, its comparison with the Cache run shared
  with the archive mode (`Compare`*); `Shell.RunPackage`* runs a package's
  executable as a player would, with only the options given.
- CTest `samples.walker-packaged` (labels integration, gpu, aot; about
  2.5 minutes).
