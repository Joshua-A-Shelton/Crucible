# 0100 — JIT shipping builds

**Status:** Accepted (2026-11-18); M10 step 3 built, then tried by hand by
the owner (Build Settings… now brings an open window to the front). The
design's names were settled before building; those added while building
were settled by the owner as drafted (docs/naming.md).

## Context

ADR 0001 lets a game ship on CoreCLR (JIT), ReadyToRun by default, and
ADR 0011 has a native launcher host .NET and load the game's assembly. By
M10 step 2 the pieces of a shipped game existed (the launcher, the core,
the engine's assemblies, the game's assembly and C++ module, the cooked
archive), but only in the build tree and the project's Cache, run with a
command line naming each. Nothing put them in one folder a player could
run, and nothing brought .NET along: a player would have had to install
it.

## Decision

Decided by the owner (2026-11-18):

1. **A package is a folder,** Build/Linux or Build/Windows in the project:
   only the platform the editor runs on, since the other's launcher and
   C++ compiler aren't there. At its top, the executable, named after the
   game (`Walker`, `Walker.exe`), and the engine's core library (on Windows
   also `nethost.dll` and the Agility SDK's `D3D12/D3D12Core.dll`, which
   must sit beside the executable). `Data/` holds the rest: the game's
   assembly, its C++ module if it has one, `Game.crpak`, the engine's
   assemblies, and the .NET runtime (self-contained), so a player needs
   nothing installed.
2. **The executable finds its game by its own name,** with no launch
   file: beside a `Data` folder, `Walker` runs `Data/Walker.dll`, loads
   `Data/Walker.Core` if it's there, and reads `Data/Game.crpak`. Options
   still override each (`--managed-dir`, `--game`, `--module`,
   `--archive`).
3. **Made by `dotnet publish`* of a project the editor writes
   (Cache/Build/Package/<Project>.csproj): the game's code against the
   engine, self-contained for this platform, ReadyToRun unless the project
   file's Build section says `ReadyToRun: false` (Build Settings'
   "Precompile (ReadyToRun)" checkbox, on by default). The runtime and the
   ReadyToRun compiler are Microsoft's NuGet packages (MIT), downloaded on
   the first build and cached after.
4. **Build replaces Cook.** The Build menu's Build builds the code, cooks,
   and packages; headless, the editor's `--build <folder>` (the game
   argument `build=`) replaces `--cook`. Build/Cooked goes away: a cook
   writes into the package's Data folder.
5. **Files renamed everywhere** (the owner's change, for names a player
   sees): a game's C# assembly `<Project>.Game.dll` becomes
   `<Project>.dll`, and its C++ module `<Project>.Native` becomes
   `<Project>.Core` (`.dll`, `.so`), in the editor's builds as in a
   package. To match, the engine's `crucible_core` becomes `Crucible.Core`
   (no `lib` prefix on Linux, like a game's module) and `crucible_importers`
   `Crucible.Importers`; the executables `crucible_launcher`,
   `crucible_editor`, and `crucible_launcher_aot` become `CrucibleLauncher`,
   `CrucibleEditor`, and `CrucibleLauncherAot`. CMake's target names stay.
6. **Plugins** are compiled into the game's assembly in v1 (ADR 0002), so
   in a package they register through the generated entry points as in
   the editor. Loading mods into a shipped JIT game comes later.

## Why

A folder is what every store and launcher takes, and what a player can
unzip and run. Shipping the runtime costs about 70 MB but means the game
runs anywhere the engine does, on the .NET version it was tested with.
Finding the game by the executable's name needs no file to read or name,
and the Data folder keeps the top level to a few files a player
recognises. ReadyToRun removes most of the JIT's first-run stalls (ADR
0001). One Build command is one thing to learn: what's tested is what
ships.

## How it works

- **Starting a self-contained runtime.** .NET's hosting call for a
  library (`hostfxr_initialize_for_runtime_config`, which the launcher
  used) refuses a self-contained runtime ("Initialization for
  self-contained components is not supported"). So in a package the
  launcher starts .NET for an app, `hostfxr_initialize_for_dotnet_command_line`
  with the game's assembly, whose runtimeconfig.json and deps.json the
  publish wrote, and never runs it as one: it then asks for the engine's
  entry point as before. nethost finds the package's own hostfxr through
  the game's path. Outside a package nothing changes.
- **ReadyToRun only counts in the default load context.** The editor
  loads a game into a collectible context, from a stream, so it can be
  unloaded (ADR 0001); .NET runs no ReadyToRun code for such assemblies. A
  package's game, one of the assemblies its runtime started with, is
  loaded into the default context instead (`GameLoader.LoadPackaged`): it
  is never unloaded.
- **Finding the core.** On Linux the launcher's run path includes
  `$ORIGIN`, so it finds `Crucible.Core.so` beside it, and a module in
  Data finds the core already loaded by its soname. On Windows both are
  found beside the executable.
- **Names a package can't use.** Data holds the runtime's assemblies
  (`System.dll`, `mscorlib.dll`, …) and the engine's core beside the
  game's files, so a project named like one of them, or `Crucible`, gets a
  `Game` prefix (`GameSystem`), as one starting with a digit already did.
- **A failed build changes nothing.** The package is made in
  `<folder>.building` and moved into place once whole. A target folder
  that isn't empty and has no Data folder isn't a package and is never
  replaced.

## Known limitations

- **Debug symbols** (`.pdb`) ship with the engine and game; stripping them
  is a choice for later.
- **The launcher's configuration** is the editor's: a debug build of the
  engine makes debug packages (the core alone is about 50 MB in debug).
- **No mod loading** into shipped JIT games yet (ADR 0001).

## As built

- Native: the launcher's package mode (`executablePath`, the Data folder,
  the game, module, and archive by the executable's name); `runEngine`'s
  `application` (JitHost.cpp's app start); `--build`; the output names
  (`OUTPUT_NAME`, `PREFIX ""`); `BUILD_RPATH $ORIGIN` on the launcher.
- C#: `GameLoader.LoadPackaged` and `IsPackaged`; `GameBuilder`'s
  `PackageProjectFile`, `ProjectText(package, readyToRun)`, `Publish`,
  `RuntimeIdentifier`, and the `Game` prefix for taken names;
  `Packager` and `PackageResult`; `BuildSettings.ReadyToRun`;
  `Project.PackageFolder` and `PackageDataFolder` (replacing
  `CookedFolder`); `Shell.Build` and `LastBuild`; the Build Settings
  window's Build card; the binding generator's library `Crucible.Core`.
- Tests: CTest `samples.walker-package` (samples/Walker/package_walker.cmake):
  the editor's `--build` makes the Walker's package (8 assets, ReadyToRun
  checked by the engine assembly's growth), which is copied elsewhere, to
  a folder with a space in its name, and run headless with no options, no
  `DOTNET_ROOT`, and no .NET on the PATH: it loads Walker.dll, reads its 8
  assets from Game.crpak, opens the Level with 12 entities at the top, and
  runs 120 frames. launcher.jit.build-settings turns ReadyToRun off and on
  and builds; `AGameIsNotNamedLikeTheRuntimeOrTheEngine` checks the
  prefix.
