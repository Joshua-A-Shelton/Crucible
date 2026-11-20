# 0110 — Skipping up-to-date builds

**Status:** Accepted (2026-11-19). Names added are marked `*` until the
owner settles them (docs/naming.md).

## Context

After ADR 0109 the Windows suite (8 logical processors, `-j8`) kept all
eight slots busy for the whole run: release took 133 s with 1064 s of test
time, so only less work would make it faster. Every check copied a sample
without its Cache (ADR 0109), so each opened cold: everything imported and
the game's C# built from nothing, 24 times a run, about 3.7 s each alone on
Windows, more under load. The Diorama's C++ module also built from nothing
in each of its 8 checks (5.5 s alone).

## Decision

Decided by the owner (2026-11-19).

1. **The editor skips a C# build that's up to date.** A build that
   succeeds writes `Cache/Build/<Game>.stamp`*: a hash of its inputs, the
   hash of the assembly it made, and the folder it was built in. Before
   building, the editor (`GameBuilder.InputHash`*, `IsUpToDate`*) hashes the
   inputs again; if they and the assembly match the stamp, nothing builds
   and the log says "`Walker.dll` is up to date"*. The inputs, by contents
   (not file times, which copies and checkouts change):
   - the project file the editor writes, with the project's folder left
     out, so a copy elsewhere hashes the same;
   - every `.cs` file under `Code/`, by its path there and its contents;
   - the C++ module's bindings (`Bindings.g.cs`), if any;
   - the engine assembly and its source generators.
   A change that changes nothing (a file saved as it was) neither builds
   nor reloads ("Game up to date"* in the status bar). Packages always
   build.
2. **Not the C++ module.** Its inputs reach outside the project (engine
   headers, the toolkit, the compiler), and CMake already skips work in
   place; a stamp missing an input would load a stale module. Only the
   C# skips.
3. **Moved projects.** dotnet's `obj` folders and CMake's build folder
   hold absolute paths. When the stamp's folder isn't the project's, a
   build clears the `obj` folders first; the C++ module's build folder is
   cleared when its `CMakeCache.txt` was made elsewhere
   (`NativeBuilder.ClearIfMoved`).
4. **Warm copies for the tests.** `samples.walker-warm`* and
   `samples.diorama-warm`* (fixtures `walker-warm`*, `diorama-warm`*,
   script `tests/warm/warm_project.cmake`*) copy their sample without its
   Cache into `<build>/src/native/launcher/warm/<Sample>/`*, open it cold
   (checking everything imported and built), and open it again (checking
   nothing imported and the build was skipped). The checks get the copy as
   `--game-arg warm-project=`* and copy it Cache and all
   (`AssetChecks.IsWarm`*); the package tests start from it too, clearing
   only their package builds. `launcher.jit.assets`, which is about
   importing, still copies the sample cold. The warm-ups start first
   (`COST`), since the checks wait on them.

## Results (all 499 tests passing)

| | Before | After |
|---|---|---|
| Windows release (`-j8`) | 133 s (1064 s of tests) | 119–122 s (915 s) |
| Windows debug (`-j8`) | 165 s | 147 s |
| Linux release (`-j16`) | 23 s | 25 s |
| Linux debug (`-j16`) | 28 s | 27 s |

In a run, the C# now builds from nothing 3 times (the two warm-ups and the
asset check) instead of 24. Linux, never short of cores, gains nothing:
the warm-ups now come before the checks. On Windows the largest cost left
is frames: `samples.walker-level`'s 240 fps run is 10,802 frames, 14 s
alone at 1.3 ms a frame on D3D12, against about 0.45 ms on Linux's Vulkan.

## Consequences

- Opening a project whose code hasn't changed no longer runs
  `dotnet build`: faster for everyone, not only the tests.
- A new input to the game's C# build must be added to `InputHash`, or a
  change to it won't rebuild. `GameBuildTests` checks each input.
- To force a build, delete `Cache/Build/<Game>.stamp`.
- A new check that copies a sample should use the warm copy unless it is
  about the cold open, and require its fixture.
