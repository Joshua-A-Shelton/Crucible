#include <crucible/Demo.h>

#include <crucible/Ecs.h>
#include <crucible/Systems.h>

#include <atomic>
#include <cmath>
#include <span>
#include <string_view>

namespace crucible::demo {

namespace {

struct DemoState
{
    DemoSettings settings{};
    // Made on first use, for the world the systems run on.
    ecs::World* world = nullptr;
    ecs::Query* particles = nullptr;
    uint32_t random = 1;
    std::atomic<uint64_t> respawned{0};
};

DemoState state;

constexpr std::string_view steerName = "crucible.demo.Steer";
constexpr std::string_view respawnName = "crucible.demo.Respawn";

ecs::Query* particlesOf(ecs::World& world)
{
    if (state.world != &world)
    {
        if (state.particles != nullptr)
        {
            ecs::destroyQuery(state.particles);
        }
        const ecs::ComponentId required[] = {ecs::componentIdOf<Position>, ecs::componentIdOf<Velocity>,
                                             ecs::componentIdOf<Lifetime>};
        state.particles = ecs::createQuery(world, {.required = required});
        state.world = &world;
    }
    return state.particles;
}

// xorshift32: small, fast, and the same everywhere, so runs repeat.
float nextRandom()
{
    uint32_t x = state.random;
    x ^= x << 13;
    x ^= x >> 17;
    x ^= x << 5;
    state.random = x;
    return static_cast<float>(x >> 8) * (1.0f / 16777216.0f); // [0, 1)
}

struct SteerData
{
    float step;
    DemoSettings settings;
};

void steerChunk(const ecs::ChunkView& view, void* data)
{
    const SteerData& steer = *static_cast<const SteerData*>(data);
    std::span<const Position> positions = ecs::readColumn<Position>(view);
    std::span<Velocity> velocities = ecs::writeColumn<Velocity>(view);
    const Vector3 up{0, 1, 0};
    for (size_t i = 0; i < positions.size(); ++i)
    {
        const Vector3 p = positions[i].value;
        const Vector3 v = velocities[i].value;
        const Vector3 acceleration =
            -steer.settings.pull * p + steer.settings.spin * cross(up, p) - steer.settings.drag * v;
        velocities[i].value = v + acceleration * steer.step;
    }
}

void steerSystem(const loop::SystemContext& context, void*)
{
    if (context.world == nullptr)
    {
        return;
    }
    SteerData data{static_cast<float>(context.fixedStepSeconds), state.settings};
    ecs::parallelForEachChunk(*particlesOf(*context.world), steerChunk, &data);
}

void countChunk(const ecs::ChunkView& view, void* data)
{
    *static_cast<uint32_t*>(data) += ecs::entityCountOf(view);
}

void respawnSystem(const loop::SystemContext& context, void*)
{
    if (context.world == nullptr || context.commands == nullptr)
    {
        return;
    }
    uint32_t count = 0;
    ecs::forEachChunk(*particlesOf(*context.world), countChunk, &count);
    const DemoSettings& settings = state.settings;
    const ecs::ComponentId components[] = {ecs::componentIdOf<Position>, ecs::componentIdOf<Velocity>,
                                           ecs::componentIdOf<Lifetime>};
    for (uint32_t i = count; i < settings.population; ++i)
    {
        // On a ring in the XZ plane, moving around it.
        const float angle = nextRandom() * 6.2831853f;
        const Vector3 direction{std::cos(angle), 0, std::sin(angle)};
        const Vector3 position = direction * settings.radius + Vector3{0, (nextRandom() - 0.5f) * 0.1f, 0};
        const Vector3 velocity = cross(Vector3{0, 1, 0}, direction) * settings.radius * 0.5f;
        const float lifetime = settings.minLifetime + nextRandom() * (settings.maxLifetime - settings.minLifetime);
        const ecs::Entity entity = ecs::recordCreate(*context.commands, components);
        ecs::recordSet(*context.commands, entity, Position{position});
        ecs::recordSet(*context.commands, entity, Velocity{velocity});
        ecs::recordSet(*context.commands, entity, Lifetime{lifetime});
    }
    if (count < settings.population)
    {
        state.respawned += settings.population - count;
    }
}

} // namespace

void registerDemoComponents()
{
    ecs::registerComponent<Position>("Crucible.Demo.Position");
    ecs::registerComponent<Velocity>("Crucible.Demo.Velocity");
    ecs::registerComponent<Lifetime>("Crucible.Demo.Lifetime");
}

void registerDemoSystems()
{
    registerDemoComponents();
    state.respawned = 0;
    loop::registerSystem({.name = respawnName, .phase = "PrePhysics", .function = respawnSystem});
    loop::registerSystem({.name = steerName, .phase = "Physics", .function = steerSystem});
}

void configureDemo(DemoSettings settings)
{
    state.settings = settings;
    state.random = settings.seed != 0 ? settings.seed : 1;
}

void unregisterDemoSystems()
{
    loop::unregisterSystem(respawnName);
    loop::unregisterSystem(steerName);
    if (state.particles != nullptr)
    {
        ecs::destroyQuery(state.particles);
    }
    state.particles = nullptr;
    state.world = nullptr;
}

uint64_t demoRespawned()
{
    return state.respawned.load();
}

} // namespace crucible::demo
