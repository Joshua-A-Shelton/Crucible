# 0017 — C#↔C++ binding generator

**Status:** Accepted (2026-10-25). Amended 2026-10-26: libclang is reached
through Crucible's own bindings to the system's libclang, not ClangSharp
(see "Amendment" below). Amended again 2026-10-26 (M2): how enums, spans,
strings, and Result cross, and the namespace mapping. Amended again (M3):
existing C# types and fixed-size arrays.

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

1. **(a): annotated C++ headers, parsed with libclang** (via ClangSharp
   originally; see the amendment). Build-time only: nothing ships in games
   or the editor runtime. Parsing is cached per header. (c) is the fallback
   if libclang becomes a burden.
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

## Amendment (2026-10-26): own libclang bindings

Building the generator (M1 step 7) showed two problems with ClangSharp:

- **Size:** its packages carry native libclang and libClangSharp for each
  platform. Lock files cover both platforms (ADR 0029, Directory.Build.props),
  so every machine downloads about 470 MB.
- **No built-in headers:** the packaged libclang comes without Clang's
  built-in headers (`stddef.h`, `stdarg.h`, ...), so it can't parse even
  `<cstdint>` alone. A system Clang installation is needed regardless.

Decision: the generator binds the **libclang C API** itself (about 30
functions: parsing, walking declarations, annotations, types, sizes, field
offsets, inclusions) and loads the **system's libclang**, found by CMake next
to the system Clang. The C API is ABI-stable across Clang versions, and
ClangSharp's low-level layer wraps the same API.

Correction (M2): libclang does **not** find its built-in headers on its own.
Called without a program path, its driver doesn't know where Clang is
installed and looks for them in a relative path. The generator therefore
parses with `clang_parseTranslationUnit2FullArgv` and the system `clang`'s
path as argv[0], so the driver finds Clang's built-in headers and the
platform's C++ headers exactly as the compiler does.

- libclang (Apache 2.0 with LLVM exception) becomes a build-time
  requirement found on the system, like Clang itself; ClangSharp is no
  longer a dependency.
- Given up: ClangSharp's maintained bindings. Kept small, the hand-written
  ones are also a worked example of the binding work Crucible does.

## Amendment (2026-10-26): how each shape crosses

Settled with the project owner in M2 step 2, when the platform API became
the first real exposed API. Names (`Api`, `CRUCIBLE_EXPOSE_STATUS`,
`CrucibleException`) are placeholders.

| C++ | C ABI shim | C# idiomatic layer |
|---|---|---|
| `enum class E : uint32_t` | the enum itself | `enum E : uint`, same names and values |
| `std::span<T>` / `std::span<const T>` parameter | `T*` + `size_t` | `Span<T>` / `ReadOnlySpan<T>`, pinned for the call |
| `std::string_view` parameter | `const char*` + `size_t`, UTF-8 | `string` |
| `Result<T>` return | returns `Status`; `T*` result written on success | `T`, throwing `CrucibleException` on failure |
| `CRUCIBLE_EXPOSE_STATUS Result<T>` | same | `Status F(..., out T result)` |

- **Namespaces:** `crucible::platform::createWindow` is exported as
  `crucible_platform_createWindow` and called as
  `Crucible.Platform.Api.CreateWindow`; types go into the matching C#
  namespace (`Crucible.Platform.InputEvent`). Every exposed declaration is
  inside `crucible`. Because a bare `Api` inside any `Crucible.*` namespace
  means the root `Crucible.Api`, and a game importing two namespaces sees two
  `Api` classes, calls read `Platform.Api.CreateWindow(...)`.
- **Result (ADR 0030):** the generator can't tell an unexpected failure from
  an expected or per-frame one, so the header says: throwing is the default,
  `CRUCIBLE_EXPOSE_STATUS` opts into returning the `Status`. On failure the
  C# value stays at its default.
- **Strings** are converted to UTF-8 in a 256-byte stack buffer, or a pooled
  array for longer ones, so a call creates no garbage, as .NET's own
  `LibraryImport` string marshalling does.
- **Spans** are pinned, never copied: C++ reads or writes the caller's
  memory directly, so a frame's input events are written straight into a C#
  array. Only dynamic-extent spans of non-`bool` elements cross; a C# `bool[]`
  isn't laid out like C++ `bool`s.
- **Out-parameters** (`T&`) are not supported: a C++ reference can't say
  whether it's output-only. A function returning several values returns an
  exposed struct (`WindowSize windowPixelSize(WindowHandle)`).
- **Exposed structs have no default member initializers:** C#'s `default`
  would silently ignore them, so the plain-data check rejects them.
- **Detecting standard types** uses the declaration's name and its `std`
  namespace, not type spellings, which differ between libc++, libstdc++, and
  MSVC's library. A parameter's span type is never instantiated, so its size
  is unknown; a fixed extent is detected because libclang leaves defaulted
  template arguments out of the spelling (`span<T>` versus `span<T, 4>`).

## Consequences

- The C++ math types must be layout-compatible with the C# types they
  cross as: settled by the amendment below (M3 step 2).

## Amendment (2026-10-26): existing C# types and arrays (M3 step 2)

- **`CRUCIBLE_EXPOSE_AS("System.Numerics.Vector3")`** exposes a struct as an
  existing C# type with the same layout: no mirror is generated, and the
  named type appears wherever the C++ type does. C++ `static_assert`s its
  size and field offsets; C#'s `LayoutChecks` compares the size; tests of
  the operations on it catch a field-level mismatch (the math types'
  `MathTests` compare every operation with `System.Numerics`).
- **Fixed-size arrays in exposed structs** (`int32_t values[3]`): C++
  `static_assert`s the offset, and the C# mirror's field is a generated
  `[InlineArray(N)]` struct named after the struct and field
  (`ProbeArrayValuesArray`, a placeholder), indexed like a span. Arrays of
  arrays and of `bool` are rejected.
- **`[assembly: DisableRuntimeMarshalling]`** on the engine assembly: its
  native calls pass raw memory only, as this ADR's rules already required,
  which lets `LibraryImport` pass types from other assemblies by value
  (`System.Numerics`) and turns any signature needing conversion into a
  compile error. Game assemblies are unaffected.

## Amendment (2026-10-27): internal exposure (M3 step 5)

`CRUCIBLE_EXPOSE_INTERNAL` exposes a function as an `internal` C# method:
callable by the engine's own C# and, through `InternalsVisibleTo`, its
tests, but not by games. For engine plumbing (the C# job trampoline) and
the launcher-owned setup functions the tests need. A function pointer
crosses as a `uint64_t` address, since the generator doesn't do function
pointers; the job trampoline is the only one so far.

## Amendment (2026-10-27): opaque handles and raw addresses (M3 step 7a)

- **Opaque handles:** a struct declared but not defined in the headers and
  marked `CRUCIBLE_EXPOSE` is opaque (native code owns it). A pointer or
  reference to it crosses as a generated `readonly struct
  <Name>Handle { nint Address }`; the shim dereferences it where the C++
  function takes a reference. Used for `World`, `Query`, `Chunk`, and
  `CommandBuffer`; hand-written C# classes wrap the handles.
- **Raw addresses:** `void*` and `const void*` cross as `nint`, for engine
  code that reaches native memory without copies (component values, chunk
  columns, ADR 0012); those functions are internal, and games see typed
  `ref`s and spans. Other pointers are still rejected, with a message
  naming what's allowed.

## Amendment (2026-10-27): returned strings and handle fields (M3 step 8a)

- **Returned `std::string_view`:** a plain function (not a `Result`) may
  return a view. The C shim returns its pointer and writes its length
  through an added `resultLength` parameter, a reserved name. C# copies it
  into a `string` before returning, so the view only needs to stay valid
  until the call returns. That allocates, so string returns suit startup
  and tools, not per-frame calls. An empty view may have a null pointer and
  comes back as `""`. Used to read the C++ system registry (ADR 0036).
- **Opaque pointers as struct fields:** a field pointing to an opaque type
  becomes its `<Name>Handle`, which is a pointer's size, so the layout
  checks hold as before. References and `void*` are still refused in
  fields: a reference can't be a C# field, and a raw address would be
  public in a struct mirror. Used for `SystemContext`'s world and command
  buffer.

## Amendment (2026-10-28): more than one library (M4 step 5)

ADR 0042 puts the editor's import code in its own native library,
`crucible_importers`, beside the core. The generator now serves both:

- `--library`, `--interop-namespace`, `--export-macro`, and
  `--export-header` choose the library LibraryImport loads, the namespace
  of the internal raw layer and layout checks, and how the C shims are
  exported. The defaults are the core's, whose output is unchanged.
- `--extern-header` names another library's headers: their exposed types
  resolve (an importer function can return `Result` and use `Status`), but
  nothing is generated for them, so C# uses the engine's types.
- CMake's `crucible_generate_bindings` takes the same as arguments, plus a
  `TARGET` name, once per library.
- The engine's `Utf8Argument` is public (hidden from IntelliSense), for
  other assemblies' generated code.
