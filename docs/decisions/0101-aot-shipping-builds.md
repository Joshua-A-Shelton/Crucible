# 0101 — AOT shipping builds

**Status:** Accepted (2026-11-18); M10 step 4 built, awaiting the owner's
try (names marked * were added while building it and are placeholders
until then). The design's names were settled before building
(docs/naming.md).

## Context

ADR 0001 lets a game ship compiled ahead with NativeAOT instead of on the
JIT: faster to start, no JIT stalls, smaller, but no mods. ADR 0011 links
the launcher, the core, and the NativeAOT output into one executable, with
a generated bootstrap registering the compiled-in game; ADR 0036 left that
bootstrap as an open item. The engine's own AOT launcher
(`CrucibleLauncherAot`) linked the engine alone and refused games. Step 3
(ADR 0100) made JIT packages.

## Decision

Decided by the owner (2026-11-18):

1. **A Build Settings choice.** The project file's Build section has
   `Runtime`: `Jit` (the default) or `NativeAot` (`PackageRuntime`*).
   Build Settings shows it as a "Runtime" drop-down, "JIT (allows mods)"
   and "NativeAOT (one executable, no mods)", with the ReadyToRun checkbox
   greyed out under NativeAOT. Build makes the chosen one; headless
   `--build` follows the setting.
2. **A folder per runtime** (the owner's addition, so both can exist):
   Build/<Platform>/JIT and Build/<Platform>/AOT, replacing step 3's
   Build/<Platform>.
3. **One executable,** named after the game, holding the launcher, the
   core, the engine, and the game; beside it only Data/Game.crpak (and on
   Windows the Agility SDK's D3D12/D3D12Core.dll, which D3D12 loads from
   a file).
4. **A game's C++ module is linked in:** its sources compiled into the
   executable, its start and stop called where a loaded module's would be.
5. **Debug information stays out of the package,** kept where the
   executable was linked (Cache/Build/Aot/Link/bin: `<Game>.debug` on
   Linux, `<Game>.pdb` on Windows), for reading crash reports.

## Why

ADR 0001's per-game choice belongs with the other build settings, and
separate folders let a developer compare the two or ship both. One
executable is the simplest thing to hand a player and what NativeAOT is
for. Linking the module in keeps C++ games able to ship AOT; loading it as
a library would load a second core, since the executable already has one.

## How it works

- **Two steps instead of one** (a change from the design, which had
  NativeAOT link the executable through its custom-main option): NativeAOT
  compiles the game and the engine into a static library, then a CMake
  project the editor writes links it with the launcher and the core, as
  `CrucibleLauncherAot` is linked. That path was already built and tested,
  and it links the module's C++ with the same compiler the core used.
  NativeAOT writes the runtime's own libraries to a response file for the
  link, as for the engine's AOT launcher.
- **What the link needs comes from the toolkit** (CrucibleModule.json, ADR
  0074): the AOT launcher's object files (`crucible_launcher_aot_objects`*,
  now built with the editor, the Agility SDK's exports among them), the
  core's static libraries in link order, the system's libraries, and the
  instruction set NativeAOT targets (ADR 0024's level).
- **The bootstrap** is a file the editor writes into the NativeAOT project
  (`Crucible.Generated.CompiledInGame`*): a module initializer setting
  `GameLoader.CompiledIn`* to the game's assembly before the engine
  starts. The engine then reads the game's registrations from its
  assembly's attributes (`GameLoader.LoadCompiledIn`*), as it does for a
  loaded game; every type they name is annotated, so NativeAOT keeps it.
- **Two things NativeAOT needs told.** It exports only the published
  assembly's entry points, so the engine's `crucible_engine_run` is named
  (`UnmanagedEntryPointsAssembly`). And it keeps only what those entry
  points reach, which drops the whole game, bootstrap included, since
  nothing outside refers to it: the game's assembly is a trimming root
  (`TrimmerRootAssembly`), kept whole.
- **The linked module** gets a generated file calling `linkModule`* while
  the executable starts, before `main`; the launcher calls
  `startLinkedModules`* where it loads modules. The game's bindings call
  the module's functions directly (`DirectPInvoke`).
- **The launcher** in a package without `Data/<Game>.dll` doesn't name a
  game assembly, so a NativeAOT executable uses its compiled-in game.

## Known limitations

- **The engine's configuration:** a debug editor links debug libraries
  (the Walker's executable is about 24 MB from a debug build).
- **No mods,** by design (ADR 0001).
- **The editor's first NativeAOT build** downloads the ILCompiler from
  NuGet, as the first JIT package downloads the runtime.

## As built

- Native: `crucible_launcher_aot_objects`*; `linkModule`* and
  `startLinkedModules`* (crucible/Module.h); the toolkit's `AotObjects`*,
  `AotLibraries`*, `AotSystemLibraries`*, `AotInstructionSet`*
  (`CRUCIBLE_AOT_NATIVE_LIBRARIES`*, `CRUCIBLE_AOT_SYSTEM_LIBRARIES`*).
- C#: `BuildSettings.Runtime`, `PackageRuntime`* (`Jit`, `NativeAot`);
  `Project.PackageFolder` per runtime; `GameLoader.CompiledIn`* and
  `LoadCompiledIn`*; `GameBuilder.AotFolder`*, `AotProjectFile`*,
  `AotLinkInputs`*, `AotProjectText`*, `PublishAot`*; `NativeBuilder`'s
  `AotFolder`*, `AotProjectText`*, `LinkAot`*; the packager's NativeAOT
  path; the Build card's Runtime drop-down.
- Tests: tests/packaging/package_game.cmake* (replacing
  samples/Walker/package_walker.cmake) copies a project, sets its build
  settings, builds it with `--build`, and runs the package from elsewhere
  with no .NET: CTest `samples.walker-package` (JIT), and
  `samples.walker-package-aot`* and `samples.diorama-package-aot`*
  (NativeAOT; the Diorama's C++ module linked in, its component
  registered). launcher.jit.build-settings sets the runtime to NativeAOT
  and back.
