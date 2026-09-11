#ifndef CRUCIBLE_API

#if defined(_WIN32) || defined(__CYGWIN__)
#define CRUCIBLE_API __declspec(dllexport)
#else
#define CRUCIBLE_API __attribute__((visibility("default")))
#endif

#endif
