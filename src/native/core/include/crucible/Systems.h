#pragma once

// C++ systems (ADR 0036): registered here with a phase and constraints, and
// pulled into the schedule when the C# loop builds it, so C++ and C# systems
// sort into one order. Names are placeholders (docs/naming.md).

#include <crucible/Ecs.h>
#include <crucible/Expose.h>

#include <cstdint>
#include <span>
#include <string_view>

namespace crucible::loop {

// What a C++ system sees when it runs, filled by the C# loop before each
// call. The main world and its command buffer (record structural changes
// there; the phase's sync point applies them), and the frame's times.
struct CRUCIBLE_EXPOSE SystemContext
{
    ecs::World* world;
    ecs::CommandBuffer* commands;
    uint64_t frameNumber;
    double deltaSeconds;     // clamped like the fixed tick (ADR 0036)
    double fixedStepSeconds; // the fixed tick's step
    double fixedAlpha;       // how far the frame is between two ticks, 0..1
};

// Runs on the loop's thread, like a C# system; split work across cores with
// the job system (ADR 0014). Never throws (ADR 0030).
using SystemFunction = void (*)(const SystemContext& context, void* userData);

struct SystemDesc
{
    std::string_view name;  // unique among all systems, C# ones included
    std::string_view phase; // the phase it runs in
    SystemFunction function;
    void* userData = nullptr;
    std::span<const std::string_view> before; // systems in the same phase
    std::span<const std::string_view> after;
};

// Copies the description; registering a name again replaces it. Thread-safe.
// The registry is read when the C# loop builds a schedule, so register before
// that; changes later take effect when the schedule is built again.
void registerSystem(const SystemDesc& desc);

// Removes the system, if registered. Thread-safe.
void unregisterSystem(std::string_view name);

// The registry, read by the C# schedule builder (ADR 0036). Indices are valid
// until the registry changes; strings until the call returns.
CRUCIBLE_EXPOSE_INTERNAL uint32_t nativeSystemCount();
CRUCIBLE_EXPOSE_INTERNAL std::string_view nativeSystemName(uint32_t index);
CRUCIBLE_EXPOSE_INTERNAL std::string_view nativeSystemPhase(uint32_t index);
CRUCIBLE_EXPOSE_INTERNAL uint32_t nativeSystemConstraintCount(uint32_t index, bool before);
CRUCIBLE_EXPOSE_INTERNAL std::string_view nativeSystemConstraint(uint32_t index, bool before, uint32_t constraint);
// The function's address and its user data, which C# calls through a function pointer.
CRUCIBLE_EXPOSE_INTERNAL uint64_t nativeSystemFunction(uint32_t index);
CRUCIBLE_EXPOSE_INTERNAL void* nativeSystemUserData(uint32_t index);

// Test probe: two C++ systems in `phase`. "crucible.probe.Count" counts its
// calls and keeps the last context it saw; "crucible.probe.Spawn", ordered
// after it by a native constraint, records one entity creation per call into
// the context's command buffer.
CRUCIBLE_EXPOSE_INTERNAL void registerProbeSystems(std::string_view phase);
CRUCIBLE_EXPOSE_INTERNAL void unregisterProbeSystems();
CRUCIBLE_EXPOSE_INTERNAL uint64_t probeSystemCalls();
CRUCIBLE_EXPOSE_INTERNAL SystemContext probeLastContext();

} // namespace crucible::loop
