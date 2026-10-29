#include <crucible/Transform.h>

#include <crucible/Ecs.h>
#include <crucible/Systems.h>

#include <span>

namespace crucible::scenes {

namespace {

// Scale, then rotation, then translation, without the full products.
Matrix4x4 localMatrix(const LocalTransform& local)
{
    Matrix4x4 m = Matrix4x4::rotation(local.rotation);
    m.rows[0] = m.rows[0] * local.scale.x;
    m.rows[1] = m.rows[1] * local.scale.y;
    m.rows[2] = m.rows[2] * local.scale.z;
    m.rows[3] = {local.position.x, local.position.y, local.position.z, 1};
    return m;
}

const WorldTransform* transformedParent(const ecs::World& world, ecs::Entity entity)
{
    const ecs::Entity parent = ecs::parentOf(world, entity);
    if (parent == ecs::nullEntity || !ecs::hasComponent(world, parent, ecs::componentIdOf<LocalTransform>))
    {
        return nullptr;
    }
    return ecs::read<WorldTransform>(world, parent);
}

// Pass 1, one job per chunk: every world matrix starts as the local one.
// Writing the columns here stamps them (ADR 0034) for the whole update.
void computeLocal(const ecs::ChunkView& view, void*)
{
    const std::span<const LocalTransform> locals = ecs::readColumn<LocalTransform>(view);
    const std::span<WorldTransform> worlds = ecs::writeColumn<WorldTransform>(view);
    for (size_t i = 0; i < locals.size(); ++i)
    {
        worlds[i].matrix = localMatrix(locals[i]);
    }
}

// Depth first, so a parent is final before its children. Children of
// different roots are disjoint, so subtrees run in parallel; their columns
// were stamped in pass 1, which is why the writes go through readComponent
// (stamping from many jobs at once would race on the chunk's versions).
void placeChildren(const ecs::World& world, ecs::Entity parent, const Matrix4x4& parentMatrix)
{
    for (const ecs::Entity child : ecs::childrenOf(world, parent))
    {
        if (!ecs::hasComponent(world, child, ecs::componentIdOf<LocalTransform>))
        {
            continue; // placed as a root of its own
        }
        auto* transform = const_cast<WorldTransform*>(ecs::read<WorldTransform>(world, child)); // NOLINT: see above
        if (transform == nullptr)
        {
            continue;
        }
        transform->matrix = transform->matrix * parentMatrix;
        placeChildren(world, child, transform->matrix);
    }
}

// Pass 2, one job per chunk: each root (no parent, or one without
// transforms) places its subtree.
void placeSubtrees(const ecs::ChunkView& view, void*)
{
    const ecs::World& world = *view.world;
    const std::span<const WorldTransform> worlds = ecs::readColumn<WorldTransform>(view);
    const ecs::Entity* entities = ecs::entitiesOf(view);
    for (size_t i = 0; i < worlds.size(); ++i)
    {
        if (transformedParent(world, entities[i]) != nullptr)
        {
            continue; // placed by its root
        }
        placeChildren(world, entities[i], worlds[i].matrix);
    }
}

struct MissingData
{
    const ecs::World* world;
    ecs::CommandBuffer* commands;
};

// Entities with a LocalTransform but no WorldTransform get one, already
// placed under their parent's current matrix.
void addMissing(const ecs::ChunkView& view, void* data)
{
    const auto& missing = *static_cast<const MissingData*>(data);
    const std::span<const LocalTransform> locals = ecs::readColumn<LocalTransform>(view);
    const ecs::Entity* entities = ecs::entitiesOf(view);
    for (size_t i = 0; i < locals.size(); ++i)
    {
        WorldTransform transform{localMatrix(locals[i])};
        if (const WorldTransform* parent = transformedParent(*missing.world, entities[i]))
        {
            transform.matrix = transform.matrix * parent->matrix;
        }
        ecs::recordSet(*missing.commands, entities[i], transform);
    }
}

void propagate(ecs::World& world, ecs::CommandBuffer* commands)
{
    // Made per call: they're cheap next to the work, and nothing outlives the
    // world they match.
    const ecs::ComponentId both[] = {ecs::componentIdOf<LocalTransform>, ecs::componentIdOf<WorldTransform>};
    ecs::Query* transformed = ecs::createQuery(world, {.required = both});
    ecs::parallelForEachChunk(*transformed, computeLocal, nullptr);
    ecs::parallelForEachChunk(*transformed, placeSubtrees, nullptr);
    ecs::destroyQuery(transformed);

    if (commands != nullptr)
    {
        const ecs::ComponentId local[] = {ecs::componentIdOf<LocalTransform>};
        const ecs::ComponentId worldOnly[] = {ecs::componentIdOf<WorldTransform>};
        ecs::Query* untransformed = ecs::createQuery(world, {.required = local, .excluded = worldOnly});
        MissingData data{&world, commands};
        ecs::forEachChunk(*untransformed, addMissing, &data);
        ecs::destroyQuery(untransformed);
    }
}

void propagationSystem(const loop::SystemContext& context, void*)
{
    if (context.world != nullptr) // a loop without a world (C# tests)
    {
        propagate(*context.world, context.commands);
    }
}

} // namespace

void updateWorldTransforms(ecs::World& world)
{
    propagate(world, nullptr);
}

void registerTransforms()
{
    ecs::registerComponent<LocalTransform>("Crucible.Scenes.LocalTransform");
    ecs::registerComponent<WorldTransform>("Crucible.Scenes.WorldTransform");
}

uint64_t transformPropagationFunction()
{
    const loop::SystemFunction function = propagationSystem;
    return reinterpret_cast<uint64_t>(function);
}

} // namespace crucible::scenes
