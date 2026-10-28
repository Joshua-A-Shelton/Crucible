// The C++ system registry (ADR 0036). Running C++ systems is tested from C#,
// where the schedule lives.

#include <crucible/Systems.h>

#include <doctest/doctest.h>

// doctest prints a failed check's string_view with operator<<, which MSVC
// declares only in <ostream>.
#include <ostream>
#include <string>

using namespace crucible;
using namespace crucible::loop;

namespace {

void doNothing(const SystemContext&, void*) {}

// The registry's index of a system, or -1.
int indexOf(std::string_view name)
{
    for (uint32_t i = 0; i < nativeSystemCount(); ++i)
    {
        if (nativeSystemName(i) == name)
        {
            return static_cast<int>(i);
        }
    }
    return -1;
}

} // namespace

TEST_CASE("The registry copies descriptions and replaces by name")
{
    int data = 0;
    {
        // Strings the registry must copy: they're gone after registering.
        std::string name = "test.Registry";
        std::string phase = "Update";
        std::string first = "test.First";
        const std::string_view after[] = {first};
        registerSystem({.name = name, .phase = phase, .function = doNothing, .userData = &data, .after = after});
    }
    const int index = indexOf("test.Registry");
    REQUIRE(index >= 0);
    const auto i = static_cast<uint32_t>(index);
    CHECK(nativeSystemPhase(i) == "Update");
    CHECK(nativeSystemConstraintCount(i, true) == 0);
    REQUIRE(nativeSystemConstraintCount(i, false) == 1);
    CHECK(nativeSystemConstraint(i, false, 0) == "test.First");
    CHECK(nativeSystemFunction(i) == reinterpret_cast<uint64_t>(&doNothing));
    CHECK(nativeSystemUserData(i) == &data);

    // Registering the name again replaces the system rather than adding one.
    const uint32_t count = nativeSystemCount();
    registerSystem({.name = "test.Registry", .phase = "LateUpdate", .function = doNothing});
    CHECK(nativeSystemCount() == count);
    CHECK(nativeSystemPhase(static_cast<uint32_t>(indexOf("test.Registry"))) == "LateUpdate");
    CHECK(nativeSystemConstraintCount(static_cast<uint32_t>(indexOf("test.Registry")), false) == 0);

    unregisterSystem("test.Registry");
    CHECK(indexOf("test.Registry") == -1);
    CHECK(nativeSystemCount() == count - 1);
    unregisterSystem("test.Registry"); // already gone: nothing happens
}
