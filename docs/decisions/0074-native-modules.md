# 0074 — Native modules

**Status:** Accepted (2026-11-09); M7 step 7b built. Names settled by the
owner as drafted; those added while building are placeholders
(docs/naming.md).

## Context

ADR 0002 left open a C++ extension mechanism for the runtime (C++ never
extends the editor), and ADR 0036 left open how C++ systems from outside
the engine register. A project's code has been C# only (ADR 0072). The owner
chose to design it in M7 rather than leave it until needed: step 7b.

The engine had what a module would register into (the native system
registry, ADR 0036; components registered by name, ADR 0023) and a
precedent for a second native library beside the shared core with
generated C# bindings (the importers, ADR 0042). But the shared core
exported only the C ABI functions C# calls: its C++ API was hidden.

## Options

**Form:** a shared library loaded at start, linking the engine's shared core
(the NativeAOT launcher links the core statically, so a shipped AOT game
would link the module statically, later); or a per-game executable (and
editor) with the project's C++ linked in.

**What a module sees of the engine:** the C++ public headers, exported from
the shared core, built with the same compiler; or a C function table (a
stable ABI, every function wrapped by hand).

**Who builds it:** a CMake project the editor generates; or the project's
own CMakeLists with an installed Crucible package.

**Changes in the editor:** rebuild, and load at the next start; or hot
reload native code.

**C# access:** generated bindings into the game assembly; or none.

**Exports:** every public header; or a chosen set. **How, on Windows**
(MSVC has no per-header export): each declaration marked; or everything in
the core exported, internals included.

**Engine component IDs in a module:** each library has its own
`ecs::componentIdOf<T>`; Windows never merges them across DLLs.

## Decision

Decided by the owner (2026-11-09):

1. **A shared library,** `<Project>.Native` (`Diorama.Native.so`, `.dll`),
   built from Project/Native into Cache/Build/Native/bin. It exports
   `crucibleModuleStart()` and `crucibleModuleStop()`
   (`CRUCIBLE_MODULE_EXPORT`, crucible/Module.h). The launcher's
   `--module <file>` and the editor load it with the core's
   `loadModule(path)`, after the engine's native side starts and before
   the C# engine builds its schedule. Modules stay loaded until the
   process ends; `stopModules()` calls their stops first.
2. **The C++ headers,** used directly, with the engine's compiler. No
   stable ABI (none is promised).
3. **A generated CMake project,** built with CMake, Ninja, and the
   compiler the engine was built with, which the engine's build records in
   `CrucibleModule.json` beside the launchers (on Windows, with the
   compiler's vcvars64.bat).
4. **Rebuild, load at the next start.** Saving a C++ file rebuilds the
   module in the background; Run Standalone uses the new build at once (a
   new process); the editor says to restart to load it. Native code can't
   be unloaded safely while its components live in a world.
5. **Generated bindings:** headers in Project/Native marked
   `CRUCIBLE_EXPOSE` go through the engine's binding generator; the C# is
   compiled into the game assembly, and its components show in the
   inspector with their `CRUCIBLE_HINT_*` hints.
6. **Every public header is exported,** by marking each declaration
   `CRUCIBLE_EXPORT` (classes with out-of-line members as a whole), so
   internals stay hidden; the `exports.public` test fails on a public
   declaration without it.
7. **`linkEngineComponents()`,** an inline function in crucible/Module.h,
   registers the engine's components by name in the module's own IDs
   (registering a name again returns its existing ID); a module calls it
   first in its start.
8. **The Diorama gets a module:** `diorama::Bob` (`height`, `speed`) and the
   system "Diorama.Bob" bob the lamp posts' lights.

## Why

A shared library is the form both the editor and the JIT launcher can load
without a build per project, and the same source links statically when a
shipped game is AOT. The C++ headers are what a C++ programmer expects,
and exporting them costs nothing to keep in step but a mark, which a test
checks. Generating the CMake project matches generating the C# project
(ADR 0072): nothing for the user to maintain. Unloading native code
safely would need every component, system, and function pointer it gave
out to be found and dropped first; restarting the editor is the honest
cost, and Run Standalone keeps iteration fast.

## Details

- **Systems a module registers are marked** in the registry
  (`nativeSystemFromModule`): the editor's play schedule adds those
  (`ScheduleBuilder.IncludeModuleSystems`) but not the engine's own, which
  its main schedule already has.
- **The game's bindings find the module's library** through the game's load
  context (`ResolvingUnmanagedDll`), by its file name among the loaded
  modules (`moduleCount`, `modulePath`).
- **The editor loads a copy,** in Cache/Build/Native/Loaded/<process>/, so
  a rebuild can replace the library it holds (Windows locks a loaded DLL);
  the copy keeps the module's file name, which the bindings use.
- **The binding generator takes `--root-namespace`:** a module's exposed
  declarations are in its own namespace (the project's name, lowercased:
  `diorama`), not crucible's; its C functions are prefixed with it
  (`diorama_bobbedLastFrame`).
- **The native build runs before the C#**, whose bindings come from it; a
  finished background build starts a C# build too.
- **Errors** are read from GCC and Clang (`file:line:col: error:`) and MSVC
  (`file(line,col): error C…:`).
- **The editor's checks take a CTest resource lock:** each opens the
  Diorama's project before copying it, and opening now builds its module
  into the same Cache, so run in parallel they raced.
- **MSVC reports errors without a column** unless asked; those read as
  column 0.
- **The CMake-built Diorama sample** leaves Code/Bob.cs out: it runs
  without the module, whose bindings it doesn't build.

## Not yet

- Linking modules statically into a NativeAOT game (packaging, ADR 0021).
- Several modules per project, or modules shared between projects.
- A module's own third-party libraries (find_package and the like).
- Switching projects in one editor session keeps the first module loaded.

## As built

- **Core:** crucible/Module.h and Module.cpp (`loadModule`, `stopModules`,
  `moduleCount`, `modulePath`, `linkEngineComponents`,
  `CRUCIBLE_MODULE_EXPORT`); `nativeSystemFromModule`; 293 functions, 13
  static members, and 2 classes marked `CRUCIBLE_EXPORT`;
  cmake/CheckExports.cmake.
- **Launcher:** `--module`, repeatable; `CrucibleModule.json`.
- **Binding generator:** `--root-namespace`.
- **Engine:** `ScheduleBuilder.IncludeModuleSystems`; the game load
  context's resolver.
- **Editor:** `NativeBuilder` (`LibraryName`, `Folder`, `ProjectFile`,
  `OutputPath`, `BindingsPath`, `HasCode`, `IsBuilding`, `Start`,
  `TakeFinished`, `BuildNow`, `ReadToolkit`, `ProjectText`, `Errors`),
  `ModuleToolkit`; the shell's `NativeBuilder`, `BuildNative`,
  `ModuleLoaded`; `Project.NativeFolder`; the game's project compiles the
  bindings and allows unsafe code.
- **Sample:** samples/Diorama/Project/Native/Bob.h and Bob.cpp, Code/Bob.cs;
  the village's four lamp lights named "Lamp Light", each with a Bob; `bobbedLastFrame()` for C# to call.
- **Tests:**
  - **`ModuleTests`** (an executable on the shared core): a module loaded
    once, its system marked as a module's, stopped; a missing file and a
    library without entry points refused; engine components linked.
  - **`exports.public`:** no public declaration without `CRUCIBLE_EXPORT`.
  - **`NativeBuildTests`:** the generated CMake project, with and without
    bindings; errors from GCC, Clang, and MSVC; a missing toolkit.
  - **`launcher.jit.native`:** on a copy of the Diorama's project, the
    module built and loaded before the C#; Bob known and on the lamp
    light; Play bobs it, C# reads `bobbedLastFrame()` through the bindings
    (4), Stop puts it back; a standalone run with the module; a broken C++
    file reported with its file, line, and column; the fixed build asks
    for a restart.
