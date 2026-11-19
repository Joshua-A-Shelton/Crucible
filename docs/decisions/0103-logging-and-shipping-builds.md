# 0103 — Logging and shipping builds

**Status:** Accepted (2026-11-18); M10 step 6 built, awaiting the owner's
try (names marked * were added while building it and are placeholders
until then). The design's names were settled before building
(docs/naming.md).

## Context

The core had a log (ADR 0087): information, warnings, and errors, printed,
handed to a sink, and kept for the editor's Log. C# had none: the engine
and games wrote with `Console.WriteLine`. Once packaged games stopped
opening a console on Windows (ADR 0100), their output went nowhere a
player could find, and every message was compiled into shipped games.
Engines differ here: Unreal compiles most logging out of its Shipping
configuration; Unity and Godot keep it, writing a log file a player can
attach to a bug report.

## Decision

Decided by the owner (2026-11-18):

1. **A Shipping configuration, like Unreal's.** The CMake preset
   `shipping` (option `CRUCIBLE_SHIPPING`, the symbol `CRUCIBLE_SHIPPING`
   in C++ and C#) builds the core, the launchers, and the engine with
   information messages compiled out; warnings and errors stay. It's a
   preset of its own, beside debug and release, so day-to-day builds don't
   grow: build it before making Shipping packages.
2. **Shipping or Development packages.** The project file's Build section
   `Configuration`: `Shipping` (the default; from the shipping preset's
   build) or `Development` (every message; from the editor's own build, as
   packages were). Build Settings' "Configuration" drop-down, "Shipping"
   and "Development (every message)". Development packages go to
   Build/<Platform>/JIT-Development and AOT-Development, so all four can
   sit side by side. The editor finds the shipping preset's build in
   build/shipping beside its own build folder (`CRUCIBLE_SHIPPING_DIR`
   points elsewhere); Build says how to build it if it's missing.
3. **C# logs into the core's log:** `Log.Info`, `Log.Warning`, and
   `Log.Error` in `Crucible.Diagnostics`, one stream with C++'s: printed
   (information to standard output, the rest to standard error), written
   to the log file, and shown in the editor's Log. A game's `Console`
   output is caught into it, as information (`Console.Error` as errors), so
   code that writes to the console, the game's or a library's, still
   reaches the file. The engine's and the Walker's own Console calls moved
   to the API.
4. **A log file in a package:** in the player's data folder,
   ~/.local/share/<Game>/<Game>.log on Linux ($XDG_DATA_HOME if set),
   %LOCALAPPDATA%\<Game>\<Game>.log on Windows, the run before kept as
   `<Game>-previous.log`. Nothing is written until something is logged,
   so a clean Shipping run leaves no file.

## Why

Compiling messages out is what a shipped game wants: no formatting, no
writes, no strings in the executable for messages no player reads. A
preset of its own keeps that from costing every build. Development
packages keep the old behaviour for testers and for chasing bugs that only
show packaged. Warnings stay in Shipping because a warning is often what
explains a later error (a missing asset, a fallback taken), and both land
in a file a player can send, which a game without a console needs.

## How it works

- **C++:** in a Shipping build, crucible/Log.h declares `log::info` as an
  empty inline function, so calls compile to nothing, and the core drops
  information written any other way. `log::write(level, text)` takes C#'s
  messages, printed as they are, without the core's "crucible: ".
  `log::setLogFile(path, previousPath)` sets the file; the first message
  makes its folder, moves an old file aside, and opens it.
- **C#:** `Log.Info` is `[Conditional("CRUCIBLE_LOG_INFO")]`
  (`CRUCIBLE_LOG_INFO`*): C# can't make a call conditional on a symbol
  being absent, so every build but Shipping defines that one (the engine's
  through Directory.Build.props, a game's through the project the editor
  writes), and a Shipping build's calls go, arguments and all. Engine code
  writes `Diagnostics.Log.Info`, since the namespace `Crucible.Log` (the
  core's messages for the editor) is found first inside `Crucible`; games
  write `Log.Info`.
- **Console:** the host replaces `Console.Out` and `Console.Error` with
  writers that pass each line to the log (`Log.CatchConsole`*), for games
  only; the editor keeps its console, which its Log panel already shows.
- **The launcher** of a package sets its log file before anything is
  logged, from the executable's name and the player's data folder.
- **Packaging Shipping:** the editor's module toolkit (CrucibleModule.json)
  names the shipping preset's (`ShippingToolkit`*), which records its
  launcher, core, engine assemblies, and AOT objects (`Launcher`*,
  `CoreRuntime`*, `EngineDirectory`*, `Shipping`*). The packager takes
  everything from it: the game is compiled against the shipping engine
  with `CRUCIBLE_SHIPPING` (`new GameBuilder(project, engine)`*), its C++
  module against the shipping core with the same symbol (a
  `NativeBuilder` variant, Cache/Build/NativeShipping*), each in folders
  of their own (PackageShipping*, AotShipping*).

## Known limitations

- **Building the shipping preset** is a step of its own, on each platform,
  before Shipping packages; the package tests for Shipping skip without it.
- **The shipping preset** uses the same tools as the others: configure it
  with the same `DOTNET_EXECUTABLE` (this machine's .NET SDK is in
  ~/.dotnet, not on the PATH).
- **The shipping preset builds the tests too,** without running them: the
  C# solution it compiles includes the test projects, which need files only
  the tests' build generates.
- **No log levels below Info,** and no way to turn messages back on in a
  Shipping package (that's what Development packages are for).

## As built

- Native: `log::write`, `log::setLogFile`, `info` compiled out under
  `CRUCIBLE_SHIPPING`; the launcher's `playerDataFolder`*; the option and
  preset; the toolkit's new fields.
- C#: `Crucible.Diagnostics.Log` (`Info`, `Warning`, `Error`,
  `CatchConsole`); `BuildSettings.Configuration`, `PackageConfiguration`*
  (`Shipping`, `Development`); the Build card's Configuration drop-down;
  the packager's sources by configuration.
- Tests: tests/packaging/package_game.cmake takes a configuration. CTests
  `samples.walker-package`, `samples.walker-package-aot`, and
  `samples.diorama-package-aot` (Development) check the log file has what
  was printed and the run before is kept; `samples.walker-package-shipping`*
  and `samples.walker-package-aot-shipping`* (Shipping; skipped without the
  shipping preset) check no information is printed and a clean run writes
  no file. launcher.jit.build-settings sets the configuration and back;
  samples.walker-packaged makes Development packages, whose messages it
  reads.
