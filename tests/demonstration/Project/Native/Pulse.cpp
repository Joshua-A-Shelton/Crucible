// The M7 demonstration's native module (ADR 0077): registers Pulse and the
// system that counts the entities with one.

#include "Pulse.h"

#include <crucible/Module.h>
#include <crucible/Systems.h>

namespace {

crucible::ecs::World* queried = nullptr;
crucible::ecs::Query* pulsing = nullptr;
uint32_t counted = 0;

void count(const crucible::loop::SystemContext& context, void*)
{
    if (queried != context.world)
    {
        if (pulsing != nullptr)
        {
            crucible::ecs::destroyQuery(pulsing);
        }
        const crucible::ecs::ComponentId required[] = {crucible::ecs::componentIdOf<demo::Pulse>};
        pulsing = crucible::ecs::createQuery(*context.world, {.required = required});
        queried = context.world;
    }
    uint32_t seen = 0;
    crucible::ecs::forEachChunk(
        *pulsing,
        [](const crucible::ecs::ChunkView& view, void* data) {
            *static_cast<uint32_t*>(data) += static_cast<uint32_t>(crucible::ecs::entityCountOf(view));
        },
        &seen);
    counted = seen;
}

} // namespace

uint32_t demo::pulsesLastFrame()
{
    return counted;
}

CRUCIBLE_MODULE_EXPORT bool crucibleModuleStart()
{
    crucible::linkEngineComponents();
    crucible::ecs::registerComponent<demo::Pulse>("Demo.Pulse");
    crucible::loop::registerSystem({.name = "Demo.Pulse", .phase = "Update", .function = count});
    return true;
}

CRUCIBLE_MODULE_EXPORT void crucibleModuleStop()
{
    if (pulsing != nullptr)
    {
        crucible::ecs::destroyQuery(pulsing);
        pulsing = nullptr;
    }
    queried = nullptr;
}
