#pragma once

// Marks the C ABI functions the shared core exports to C# (ADR 0011, 0017).
// Everything else in the shared library stays hidden.

#if defined(CRUCIBLE_CORE_EXPORTS)
#if defined(_WIN32)
#define CRUCIBLE_EXPORT __declspec(dllexport)
#else
#define CRUCIBLE_EXPORT __attribute__((visibility("default")))
#endif
#else
#define CRUCIBLE_EXPORT
#endif
