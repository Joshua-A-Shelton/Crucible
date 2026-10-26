#pragma once

// Marks a function or struct for the binding generator (ADR 0017), which
// generates its C ABI shim, C# bindings, and layout checks.
//
// The annotation exists only while the generator parses (it defines
// CRUCIBLE_BINDGEN); in normal builds, with any compiler, the macro is empty.
//
//     CRUCIBLE_EXPOSE bool hasRenderBackend();
//     struct CRUCIBLE_EXPOSE Probe { int32_t number; };

#if defined(CRUCIBLE_BINDGEN)
#define CRUCIBLE_EXPOSE __attribute__((annotate("crucible_expose")))
#else
#define CRUCIBLE_EXPOSE
#endif
