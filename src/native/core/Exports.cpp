// The C ABI that C# calls through LibraryImport (ADR 0017). Hand-written in
// M1; the binding generator replaces this file in M1 step 7.
//
// Only C types cross: bool becomes uint8_t, because .NET does not treat a
// one-byte C++ bool as blittable.

#include <crucible/Core.h>
#include <crucible/Export.h>

#include <cstdint>

extern "C" {

CRUCIBLE_EXPORT uint8_t crucible_hasRenderBackend()
{
    return crucible::hasRenderBackend() ? 1 : 0;
}

} // extern "C"
