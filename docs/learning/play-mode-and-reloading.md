# Play mode and reloading: running a game inside its editor

A learning note for M7 step 6 (ADR 0072).

## Building a game the editor doesn't know

The editor is a program that was compiled long before your game's code was
written. To run that code, it has to compile it and load the result while
it's running. In C# that's two steps: `dotnet build` turns the .cs files
into an assembly (a .dll), and the runtime loads the assembly into the
running process.

The editor writes the project file itself, rather than asking you to keep
one: it knows where the engine is, which source generators the engine
needs, and which settings keep the game AOT-safe. You only write .cs files
in the project's Code folder.

## Loading, unloading, and moving the data across

.NET can unload code only by unloading a whole *load context*, a box of
assemblies loaded together. So each build of the game is loaded into a
fresh context. To swap versions, the new one is loaded beside the old,
the world's components are moved over (a component's type in version 2 is
a different type from version 1's, even with the same name, so values are
copied field by field), and then the old context is let go.

"Let go" only works if nothing still points into it: a static field, an
event handler, a cached delegate. That's the most common hot-reload bug,
so the editor checks: it keeps a weak reference to the old context and
asks the garbage collector whether it's gone. If not, it says so.

## Play is a snapshot

Playing changes the scene: things move, spawn, die. When you stop, you
want the scene back exactly as you left it. The simplest reliable way is
to save it: Play writes the scene into memory in the scene format, Stop
throws away everything and loads that back. Because scenes keep each
entity's scene ID, the editor's selection and undo steps, which name
entities by scene ID, still point at the right things.

## The game's systems inside the editor's loop

The editor already runs a frame loop, phase by phase. The game's systems
belong to phases too (Update, LateUpdate…). Rather than rebuild the
editor's loop on every Play, the game gets a schedule of its own, and the
editor has one small system in each phase whose only job is to run that
phase's game systems while playing.

## Who gets the keyboard?

In the editor, a key press could mean "move the camera", "rename", or
"jump" in the game. So the game only sees input while its panel has it:
click the Game panel (or press Play) to give it, Escape to take it back,
the same idea as a game capturing the mouse.

## The SDK you get

`dotnet` decides which SDK to use from a `global.json` file in the folder
it runs in, or any folder above. A project inside a repository with a
`global.json` pinning an SDK you don't have would fail to build. The
editor runs the build from the temp folder, so the SDK it gets is the one
installed beside the runtime the editor itself runs on.
