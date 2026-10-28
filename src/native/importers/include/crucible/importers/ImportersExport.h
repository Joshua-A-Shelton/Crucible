#pragma once

// Marks the importers library's exported functions: the generated C ABI
// shims C# calls (ADR 0017, 0042). Like crucible/Export.h, for the other
// library.

#if defined(CRUCIBLE_IMPORTERS_EXPORTS)
#if defined(_WIN32)
#define CRUCIBLE_IMPORTERS_EXPORT __declspec(dllexport)
#else
#define CRUCIBLE_IMPORTERS_EXPORT __attribute__((visibility("default")))
#endif
#else
#define CRUCIBLE_IMPORTERS_EXPORT
#endif
