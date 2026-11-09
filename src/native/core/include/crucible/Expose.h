#pragma once

// Marks a function or struct for the binding generator (ADR 0017), which
// generates its C ABI shim, C# bindings, and layout checks.
//
// The annotation exists only while the generator parses (it defines
// CRUCIBLE_BINDGEN); in normal builds, with any compiler, the macro is empty.
//
//     CRUCIBLE_EXPOSE bool hasRenderBackend();
//     struct CRUCIBLE_EXPOSE Probe { int32_t number; };
//     enum class CRUCIBLE_EXPOSE Kind : uint32_t { A, B };
//
// A function returning Result<T> throws a CrucibleException in C# when it
// fails (ADR 0030). CRUCIBLE_EXPOSE_STATUS marks one whose failures are
// expected, or that runs every frame: its C# form returns the Status and
// hands the value back through an out parameter instead.
//
// CRUCIBLE_EXPOSE_INTERNAL exposes a function as an internal C# method: for
// the engine's own C# and its tests, not for games (the job trampoline, the
// launcher-owned setup functions).
//
// CRUCIBLE_EXPOSE_AS("System.Numerics.Vector3") exposes a struct as an
// existing C# type with the same layout, instead of generating a mirror
// (ADR 0024's amendment): the math storage types cross as System.Numerics.
//
// Editor hints on an exposed struct's fields (ADR 0070) become the C#
// mirror's [Color], [Range], [Tooltip], and [Header] (ADR 0069), which the
// inspector reads. A field's comment (after it on its line, or the comment
// lines just above it) is its tooltip unless CRUCIBLE_HINT_TOOLTIP gives one.
//
//     CRUCIBLE_HINT_COLOR Vector3 color; // linear
//     CRUCIBLE_HINT_RANGE(0, 3.14159f) float innerConeAngle;
//
// Names are placeholders (docs/naming.md).

#if defined(CRUCIBLE_BINDGEN)
#define CRUCIBLE_EXPOSE __attribute__((annotate("crucible_expose")))
#define CRUCIBLE_EXPOSE_STATUS __attribute__((annotate("crucible_expose"), annotate("crucible_status")))
#define CRUCIBLE_EXPOSE_AS(csharpType)                                                                                 \
    __attribute__((annotate("crucible_expose"), annotate("crucible_expose_as:" csharpType)))
#define CRUCIBLE_EXPOSE_INTERNAL __attribute__((annotate("crucible_expose"), annotate("crucible_internal")))
#define CRUCIBLE_HINT_COLOR __attribute__((annotate("crucible_hint_color")))
#define CRUCIBLE_HINT_RANGE(min, max) __attribute__((annotate("crucible_hint_range:" #min ":" #max)))
#define CRUCIBLE_HINT_TOOLTIP(text) __attribute__((annotate("crucible_hint_tooltip:" text)))
#define CRUCIBLE_HINT_HEADER(text) __attribute__((annotate("crucible_hint_header:" text)))
#else
#define CRUCIBLE_EXPOSE
#define CRUCIBLE_EXPOSE_STATUS
#define CRUCIBLE_EXPOSE_AS(csharpType)
#define CRUCIBLE_EXPOSE_INTERNAL
#define CRUCIBLE_HINT_COLOR
#define CRUCIBLE_HINT_RANGE(min, max)
#define CRUCIBLE_HINT_TOOLTIP(text)
#define CRUCIBLE_HINT_HEADER(text)
#endif
