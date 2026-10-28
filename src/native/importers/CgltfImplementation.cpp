// cgltf's implementation (ADR 0043), in a unit of its own so its warnings
// stay out of Crucible's code. Its allocations and file access are set per
// call (Mesh.cpp), so nothing is configured here.

#define CGLTF_IMPLEMENTATION
#if defined(_MSC_VER)
#pragma warning(push, 0)
#else
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-Wall"
#pragma GCC diagnostic ignored "-Wextra"
#pragma GCC diagnostic ignored "-Wpedantic"
#pragma GCC diagnostic ignored "-Wmissing-field-initializers"
#endif
#include <cgltf.h>
#if defined(_MSC_VER)
#pragma warning(pop)
#else
#pragma GCC diagnostic pop
#endif
