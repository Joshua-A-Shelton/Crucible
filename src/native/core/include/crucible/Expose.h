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
// Names are placeholders (docs/naming.md).

#if defined(CRUCIBLE_BINDGEN)
#define CRUCIBLE_EXPOSE __attribute__((annotate("crucible_expose")))
#define CRUCIBLE_EXPOSE_STATUS __attribute__((annotate("crucible_expose"), annotate("crucible_status")))
#else
#define CRUCIBLE_EXPOSE
#define CRUCIBLE_EXPOSE_STATUS
#endif
