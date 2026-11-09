// The Diorama's native module (ADR 0074): registers Bob and the system that
// moves every entity with one.

#include "Bob.h"

#include <crucible/Module.h>
#include <crucible/Systems.h>

#include <cmath>

namespace {

using crucible::ecs::ComponentId;
using crucible::scenes::LocalTransform;
using diorama::Bob;

constexpr double tau = 6.283185307179586;

// Seconds since the module started, so every light follows the same clock.
double elapsed = 0;
crucible::ecs::World* queried = nullptr;
crucible::ecs::Query* bobbing = nullptr;
uint32_t bobbed = 0;

struct Step
{
    double from;
    double to;
    uint32_t moved;
};

// Each frame moves by the change in the wave, so the entity keeps its own
// placed height and anything else moving it still can.
void bob(const crucible::loop::SystemContext& context, void*)
{
    if (queried != context.world)
    {
        if (bobbing != nullptr)
        {
            crucible::ecs::destroyQuery(bobbing);
        }
        const ComponentId required[] = {crucible::ecs::componentIdOf<Bob>,
                                        crucible::ecs::componentIdOf<LocalTransform>};
        bobbing = crucible::ecs::createQuery(*context.world, {.required = required});
        queried = context.world;
    }
    Step step{elapsed, elapsed + context.deltaSeconds, 0};
    elapsed = step.to;
    crucible::ecs::forEachChunk(
        *bobbing,
        [](const crucible::ecs::ChunkView& view, void* data) {
            Step& step = *static_cast<Step*>(data);
            const std::span<const Bob> bobs = crucible::ecs::readColumn<Bob>(view);
            const std::span<LocalTransform> transforms = crucible::ecs::writeColumn<LocalTransform>(view);
            for (size_t i = 0; i < bobs.size(); ++i)
            {
                const double angle = tau * bobs[i].speed;
                transforms[i].position.y +=
                    static_cast<float>(bobs[i].height * (std::sin(angle * step.to) - std::sin(angle * step.from)));
            }
            step.moved += static_cast<uint32_t>(bobs.size());
        },
        &step);
    bobbed = step.moved;
}

} // namespace

uint32_t diorama::bobbedLastFrame()
{
    return bobbed;
}

CRUCIBLE_MODULE_EXPORT bool crucibleModuleStart()
{
    crucible::linkEngineComponents();
    crucible::ecs::registerComponent<Bob>("Diorama.Bob");
    crucible::loop::registerSystem({.name = "Diorama.Bob", .phase = "Update", .function = bob});
    return true;
}

CRUCIBLE_MODULE_EXPORT void crucibleModuleStop()
{
    if (bobbing != nullptr)
    {
        crucible::ecs::destroyQuery(bobbing);
        bobbing = nullptr;
    }
    queried = nullptr;
}
