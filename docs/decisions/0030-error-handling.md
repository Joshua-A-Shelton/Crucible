# 0030 — Error handling, exceptions, and RTTI

**Status:** Accepted (2026-10-26).

## Context

On x64, C++ exceptions are "zero-cost" on the normal path, but they add
unwind tables, block some optimizations, and cost microseconds per throw.
For Crucible:

- An exception escaping an `extern "C"` shim into .NET is undefined
  behavior, in practice a crash with no useful message; ADR 0017 already
  bars it.
- Slag builds without exceptions or RTTI and returns `Result<T>` (Slag ADR
  0060), and Crucible's core calls Slag constantly.
- No chosen dependency needs exceptions (SDL3, FreeType, Box3D, Box2D,
  cgltf, ufbx, and stb are C; HarfBuzz and meshoptimizer don't use them).
- Some console toolchains discourage exceptions.

RTTI (`dynamic_cast`, `typeid`) costs binary size per polymorphic class.
Crucible has no deep class hierarchies to cast through; ECS component types
use Crucible's own runtime-registered IDs (ADR 0023).

## Options

- **(a) Exceptions off,** errors returned as values.
- **(b) Exceptions inside the core,** caught at every generated shim and
  converted to error codes.

## Decision

1. **C++ exceptions are off** (`-fno-exceptions`, `/EHs-c-`). Errors are
   returned as values; programmer errors are caught by validation and
   asserts; crashes go to the native crash handler (ADR 0011).
2. **RTTI is off** (`-fno-rtti`, `/GR-`).
3. **Crucible has its own `crucible::Result<T>`,** modeled on
   `slag::Result` (a status plus a value on success), not Slag's type
   reused. Slag errors are mapped to Crucible statuses where they surface.
4. **C# follows .NET conventions** in the idiomatic layer the binding
   generator emits (ADR 0017):
   - unexpected failures throw a C# exception (the native side returned
     only a status);
   - expected failures use the Try pattern (`bool TryLoadScene(path, out
     Scene scene)`);
   - per-frame APIs never throw; they return status values.

## Why

With exceptions off, "no exception crosses the boundary" holds by
construction instead of by generated `try`/`catch`, the generator stays
simpler, and the core matches Slag's error model. Crucible's errors aren't
Slag's statuses, so it keeps its own `Result`. C# developers get normal C#,
while the native side stays exception-free and hot paths stay cheap.

Given up: exceptions as a C++ error mechanism, and `dynamic_cast`.
