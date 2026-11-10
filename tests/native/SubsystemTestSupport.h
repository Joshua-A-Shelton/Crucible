#pragma once

// What the subsystem contract tests share (ADR 0075): finding a system in
// the registry and running it as the loop would.

#include <crucible/Ecs.h>
#include <crucible/Systems.h>

#include <cstdint>
// doctest prints a string_view with operator<<, which MSVC declares only in
// <ostream>.
#include <ostream>
#include <string_view>

namespace crucible::test {

// The registry index of a system, or -1.
inline int32_t systemIndex(std::string_view name)
{
    for (uint32_t i = 0; i < loop::nativeSystemCount(); ++i)
    {
        if (loop::nativeSystemName(i) == name)
        {
            return static_cast<int32_t>(i);
        }
    }
    return -1;
}

// Runs a registered system once on a world.
inline void runSystem(std::string_view name, ecs::World& world, double deltaSeconds = 1.0 / 60)
{
    const int32_t index = systemIndex(name);
    if (index < 0)
    {
        return;
    }
    const auto function =
        reinterpret_cast<loop::SystemFunction>(loop::nativeSystemFunction(static_cast<uint32_t>(index)));
    const loop::SystemContext context{&world, nullptr, 0, deltaSeconds, 1.0 / 60, 0, 0};
    function(context, loop::nativeSystemUserData(static_cast<uint32_t>(index)));
}

} // namespace crucible::test
