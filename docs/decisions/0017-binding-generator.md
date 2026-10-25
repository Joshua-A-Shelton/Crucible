# 0017 — C#↔C++ binding generator

**Status:** Accepted (2026-10-25).

## Context

ADR 0006: C++ reaches C# and the editor only by being exposed to C#, and
exposing it should be cheap. `LibraryImport` can only call C-ABI
(`extern "C"`) functions, and blittable components are read from the same
native memory by C++ and C# (ADR 0012), so their layouts must match
exactly.

## Options

Where the source of truth lives:

- **(a) Annotated C++ headers, parsed at build time** (Unreal's UHT model)
  with libclang (Apache 2.0 with LLVM exception) through ClangSharp (MIT).
  C++ is the single source of truth; a C++ hack is exposed by annotating
  it. Costs a large build-time dependency and parser edge cases.
- **(b) A custom IDL** generating both sides. No parser, but a third format,
  and IDL and implementation can drift.
- **(c) C# as the IDL,** generating the C++ side. No new language, but a C++
  change needs a second edit in C# to be exposed.
- **(d) Hand-written bindings.** Error-prone layouts; not cheap to expose.

## Decision

1. **(a): annotated C++ headers, parsed with libclang via ClangSharp.**
   Build-time only: neither ships in games or the editor runtime. Parsing
   is cached per header. (c) is the fallback if libclang becomes a burden.
2. **Exposure rules.** Only C-shaped signatures cross the boundary:
   blittable values and structs, IDs and handles, spans as pointer +
   length, strings as UTF-8 pointer + length. No templates, exceptions, or
   virtual calls across it. The generator rejects anything else with a
   clear error; annotated code may use anything internally.
3. **Outputs** (macro names are placeholders: `CRUCIBLE_EXPOSE`,
   `CRUCIBLE_PROPERTY`):
   - **C ABI shims:** `extern "C"` exports that unwrap strong C++ types into
     plain C types and inline the real call.
   - **C# in two layers:** an internal raw `LibraryImport` layer mirroring
     the C ABI, and a public idiomatic layer (methods on handle types,
     spans, PascalCase). The core library is listed for direct P/Invoke in
     AOT builds (ADR 0011).
   - **Struct mirrors with layout checks on both sides:** `static_assert`
     on size and offsets in C++, and a startup check of the same numbers in
     C# in validation builds. Offsets come from libclang. `bool` is
     mirrored as a byte with a C# `bool` property; padding is matched
     exactly.
   - **Editor metadata:** `CRUCIBLE_PROPERTY(...)` hints become C#
     attributes, so the normal C# source generators (ADR 0006) treat
     C++-backed types like C# ones.
   - Enums, flags, and constants.
4. **`[SuppressGCTransition]` is an explicit per-function opt-in**
   annotation, never automatic: a function marked with it that blocks or
   calls back into C# can hang the GC.
5. **Pipeline:** the generator is a C# console tool (never shipped), built
   first and run by CMake before the C++ build. It emits the shims (into
   the core library), the C# files (into the engine's C# project), and a
   depfile of every header clang opened, so a change to any included header
   reruns it. CMake orders: generator, C++ core, C#, then the AOT link.
6. **C++ changes need an editor restart.** C# changes hot-reload (ADR
   0001); native code isn't hot-reloaded, and the engine's C# layer holding
   the bindings never unloads (ADR 0011).

## Why

(a) matches the original goal most directly: the editor picks up hacks
made to the engine, with one annotation and no second edit elsewhere. The
parser is a build tool, so its license never reaches games.

Layout checks on both sides turn a C++/C# layout disagreement (from
compiler ABI differences, padding, or `bool`) into a build or startup
failure instead of silent memory corruption.

Given up: a dependency-free build, and hot reload for C++ changes.

## Consequences

- The C++ math types must be layout-compatible with the C# types they
  mirror (e.g. `Vec3` ↔ `System.Numerics.Vector3`): an open item for the
  math library choice.
