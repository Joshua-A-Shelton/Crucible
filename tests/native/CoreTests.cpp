#include <crucible/Core.h>
#include <crucible/Version.h>

#include <doctest/doctest.h>

TEST_CASE("Core links against Slag with a render backend")
{
    CHECK(crucible::hasRenderBackend());
}

TEST_CASE("Version macros match the CMake project version")
{
    CHECK(CRUCIBLE_VERSION_MAJOR == 0);
    CHECK(CRUCIBLE_VERSION_MINOR == 1);
    CHECK(CRUCIBLE_VERSION == ((0 << 22) | (1 << 12) | 0));
}
