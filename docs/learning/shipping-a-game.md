# Shipping a game

A learning note for M10 step 3 (ADR 0100): turning a project into a folder
a player can run, with .NET inside it.

## What a player gets

While you work, a game is spread across the engine's build and the
project's Cache: the launcher in one place, the engine's assemblies in
another, the game's code in Cache/Build, its assets as hundreds of
imported files. A run names each piece on the command line.

A player gets none of that. They get one folder, run the file named after
the game, and expect it to work: no command line, nothing to install. So
shipping is mostly gathering. Cooking (cooking.md) already turned the
assets into one archive; packaging puts it beside everything else the
game runs on, where the executable will look without being told.

Crucible keeps the folder's top level small, the executable and the
libraries the operating system must find beside it, and puts the rest in
`Data`. The executable knows its game by its own name: `Walker` loads
`Data/Walker.dll`. No settings file is needed, which also means none can
go missing.

## Bringing the runtime along

A .NET program needs the .NET runtime: the JIT, the garbage collector, the
standard library. There are two ways to ship one.

- **Framework-dependent:** the game uses a runtime the player installed.
  The package is small, but the player has to install the right version
  first, and the game runs on whatever version they have.
- **Self-contained:** the runtime is copied into the package, about 70 MB.
  The player installs nothing, and the game runs on exactly the version
  it was tested with.

Games almost always ship self-contained, and Crucible does. `dotnet
publish` makes it: given a runtime identifier (`linux-x64`, `win-x64`) it
fetches that platform's runtime as a NuGet package and copies it in.

Hosting a self-contained runtime from native code has one catch. .NET's
hosting library offers two ways to start: "for a component", from a
library's runtimeconfig.json, which Crucible's launcher used, and "for an
app", from an application's. Only the second accepts a runtime that came
with the program. So in a package the launcher starts .NET as if the
game's assembly were an app, then calls the engine's entry point instead
of running it.

## ReadyToRun: compiling ahead, keeping the JIT

A .NET assembly holds IL, a portable instruction set. Normally the JIT
compiles each method to machine code the first time it runs, which is a
small stall each time new code runs: noticeable as hitches early in a
game.

ReadyToRun (R2R) compiles the IL to machine code when the game is
published and stores both in the assembly. At run time the runtime uses
the precompiled code and keeps the IL, so the JIT can still step in: for
code R2R couldn't compile, to recompile hot methods better later, or for
mods loaded at run time. The assemblies grow (Crucible.Engine.dll roughly
doubles), and startup and first frames get smoother.

R2R code is used only for assemblies loaded the ordinary way. One loaded
into a collectible context, which the editor needs so a game can be
unloaded for hot reload, or loaded from bytes in memory, runs on the JIT
alone. A package never reloads its game, so it loads it the ordinary way.

NativeAOT (step 4) goes further: everything compiled ahead, no JIT at all,
one executable. It's faster to start and smaller, but nothing can be
loaded at run time, so no mods.

## Making the folder safely

A build that fails halfway shouldn't leave the last good package broken.
Crucible builds the new one in a folder beside it and swaps it in only
when every piece is there. The swap deletes the old package, so it
refuses to touch a folder that doesn't look like one: deleting someone's
files because they picked the wrong output folder is worse than any build
failure.

## Checking a package for real

A package that works on the machine that built it can still fail on a
player's: it may lean on something only the developer has, like an
installed .NET, a library path, or the build folder itself. The check
copies the package somewhere else, to a folder whose name has a space, and
runs it with no .NET on the PATH and nothing pointing at one. If it
starts, loads its game, and reads its archive there, it isn't leaning on
the build.
