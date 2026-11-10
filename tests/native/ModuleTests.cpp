// Native modules (ADR 0074), with the shared core as the launcher and editor
// use it: a module loaded once, its systems marked as a module's, stopped
// newest first; a missing file, and a library that isn't a module, refused;
// and the engine's components linked into a module's own IDs.

#include <crucible/Module.h>
#include <crucible/Systems.h>

#include <doctest/doctest.h>

// doctest prints a string_view with operator<<, which MSVC declares only in
// <ostream>.
#include <ostream>
#include <string>
#include <string_view>

using namespace crucible;

namespace {

bool registered(std::string_view name, bool* fromModule = nullptr)
{
    for (uint32_t i = 0; i < loop::nativeSystemCount(); ++i)
    {
        if (loop::nativeSystemName(i) == name)
        {
            if (fromModule != nullptr)
            {
                *fromModule = loop::nativeSystemIsGameplay(i);
            }
            return true;
        }
    }
    return false;
}

} // namespace

TEST_CASE("A module is loaded once and starts and stops")
{
    const std::string path = CRUCIBLE_TEST_MODULE;
    REQUIRE(loadModule(path));
    bool fromModule = false;
    CHECK(registered("test.ModuleSystem", &fromModule));
    CHECK(fromModule);
    const uint32_t count = moduleCount();
    CHECK(count >= 1);
    CHECK(modulePath(count - 1) == path);
    REQUIRE(loadModule(path)); // again: nothing happens
    CHECK(moduleCount() == count);

    loop::registerSystem({.name = "test.NotFromModule", .phase = "Update", .function = nullptr});
    CHECK(registered("test.NotFromModule", &fromModule));
    CHECK_FALSE(fromModule);
    loop::unregisterSystem("test.NotFromModule");

    stopModules();
    CHECK_FALSE(registered("test.ModuleSystem"));
}

TEST_CASE("What isn't a module is refused")
{
    CHECK(loadModule("no/such/module.so").status() == Status::NotFound);
    CHECK(loadModule(CRUCIBLE_NOT_A_MODULE).status() == Status::InvalidData);
}

TEST_CASE("The engine's components link by name")
{
    const ecs::ComponentId engine = ecs::registerComponent(
        "Crucible.Scenes.LocalTransform", sizeof(scenes::LocalTransform), alignof(scenes::LocalTransform));
    ecs::componentIdOf<scenes::LocalTransform> = ecs::invalidComponent;
    linkEngineComponents();
    CHECK(ecs::componentIdOf<scenes::LocalTransform> == engine);
}
