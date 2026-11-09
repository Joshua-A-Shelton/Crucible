# Native modules: C++ in a C# engine's game

A learning note for M7 step 7b (ADR 0074).

## Why let a game bring C++?

Crucible's games are written in C#, and most game code should be. But
sometimes C++ is the better tool: a tight loop over thousands of
entities, a C or C++ library you want to use, or code shared with another
C++ project. A native module is a project's own C++, built into a shared
library and loaded into the engine next to its C#.

## Two copies of everything

A shared library (a .so on Linux, a .dll on Windows) is code loaded into a
running process. The engine's core is one; a module is another. They can
call each other only through what each *exports*: the functions it
publishes by name. Crucible's core used to export only the plain C
functions C# calls. To let a module use the ECS, the job system, or the
math types directly, every function its public headers declare is now
exported. Linux could do that per header; Windows needs each function
marked, so they all carry `CRUCIBLE_EXPORT`, and a test fails if a new one
doesn't.

Code that lives *in* a header (inline functions, templates, and their
variables) isn't exported: each library compiles its own copy. That
matters for `componentIdOf<T>`, a variable per component type holding its
ID. The module has its own copy, empty until filled. Since registering a
component name again returns the ID it already has, the module fills its
copies by registering the engine's components by name:
`linkEngineComponents()`.

## Same compiler, no ABI

When two libraries share C++ types (a `std::span`, a struct with a
`Vector3`), they must agree on exactly how those look in memory and how
functions are called: the *ABI*. Different compilers, or different
settings, can disagree. Crucible promises no stable ABI, so the editor
builds the module with the very compiler and settings the engine was built
with, which the engine's build writes down in `CrucibleModule.json`.

## Loading is easy; unloading isn't

Loading a library and calling its start function takes a few lines. The
hard part is the other direction. Once a module has registered a
component, entities in the world hold data laid out by its code; once it
registered a system, the schedule holds a pointer into it. Unloading the
library would leave those pointing at nothing. C# hot reload gets away with
it because .NET can find and move every reference (ADR 0035); native code
offers no such help. So a rebuilt module waits for the editor's next
start, while Run Standalone, being a fresh process, gets it immediately.

## Talking across the boundary

C# can't call C++ directly. The engine's binding generator reads headers
marked `CRUCIBLE_EXPOSE` and writes both sides: small C functions in the
module that call the C++ ones, and C# `LibraryImport` declarations that
call those, plus C# copies of the module's structs with checks that their
layout matches. For a module, the C# goes into the game's own assembly,
and when .NET asks where the library is, the game's load context points
it at the module already loaded.
