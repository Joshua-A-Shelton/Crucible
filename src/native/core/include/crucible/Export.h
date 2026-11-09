#pragma once

// Marks what the shared core exports: the C ABI functions C# calls (ADR
// 0011, 0017), and every function and class the public headers declare, for
// native modules (ADR 0074). Everything else in the shared library stays
// hidden. New declarations in include/crucible need it too.

#if defined(CRUCIBLE_CORE_EXPORTS)
#if defined(_WIN32)
#define CRUCIBLE_EXPORT __declspec(dllexport)
#else
#define CRUCIBLE_EXPORT __attribute__((visibility("default")))
#endif
#else
#define CRUCIBLE_EXPORT
#endif
