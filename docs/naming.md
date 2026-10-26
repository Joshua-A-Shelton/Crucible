# Naming

Final names chosen by the project owner. Anything not listed here is still
a placeholder (CLAUDE.md §7).

## Code organization (2026-10-26)

| Name | What it is |
|---|---|
| `crucible` | C++ namespace |
| `crucible_core` (`crucible::core`) | The C++ core library target |
| `Crucible` | C# root namespace |
| `Crucible.Engine` | The engine's C# assembly |
| `src/native/`, `src/managed/` | C++ and C# source folders, mirrored under `tests/` |
| `CRUCIBLE_` | Prefix for macros and CMake options |
| `crucible_core_static` | The static form of the core library, for AOT builds |
| `crucible_launcher` | The launcher executable target (shipped games are named after the game) |
| `Crucible.Hosting` | Namespace of the engine's managed entry point |
| `Crucible.BindGen` | The binding generator tool |
| `Crucible.TestGame` | Small test game assembly for hot-reload tests |
