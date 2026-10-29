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

// Whether an entity's chunk changed its local transform or parent since
// `since`: a write, a reparent, or an entity moving in (ADR 0034). Versions
// are per chunk, so a change to any entity there counts.
bool changed(const ecs::World& world, ecs::Entity entity, uint32_t since)
{
    return ecs::changedSince(world, entity, ecs::componentIdOf<LocalTransform>, since) ||
           ecs::changedSince(world, entity, ecs::parentComponent(), since);
}

// Writes only a matrix that differs, so the column's change version says
// which world matrices really moved (what step 5's uploads skip by).
// Stamping is atomic, so jobs may write different entities of one chunk.
const Matrix4x4& place(ecs::World& world, ecs::Entity entity, const Matrix4x4& matrix)
{
    if (ecs::read<WorldTransform>(world, entity)->matrix == matrix)
    {
        return matrix;
    }
    WorldTransform* transform = ecs::get<WorldTransform>(world, entity);
    transform->matrix = matrix;
    return transform->matrix;
}

struct Walk
{
    ecs::World* world;
    uint32_t since; // 0: everything
};

// Depth first, so a parent is final before its children. A child is
// recomputed when it, or anything above it, changed; otherwise its stored
// matrix is already right.
void placeChildren(const Walk& walk, ecs::Entity parent, const Matrix4x4& parentMatrix, bool parentMoved)
{
    ecs::World& world = *walk.world;
    for (const ecs::Entity child : ecs::childrenOf(world, parent))
    {
        const WorldTransform* stored = ecs::read<WorldTransform>(world, child);
        if (stored == nullptr || !ecs::hasComponent(world, child, ecs::componentIdOf<LocalTransform>))
        {
            continue; // its children are roots of their own
        }
        const bool moved = parentMoved || changed(world, child, walk.since);
        const Matrix4x4 matrix =
            moved ? place(world, child, localMatrix(*ecs::read<LocalTransform>(world, child)) * parentMatrix)
                  : stored->matrix;
        placeChildren(walk, child, matrix, moved);
    }
}

// One job per chunk: each root (no parent, or one without transforms)
// places its subtree. Subtrees are disjoint, so they run in parallel.
void placeSubtrees(const ecs::ChunkView& view, void* data)
{
    const Walk& walk = *static_cast<const Walk*>(data);
    ecs::World& world = *walk.world;
    const std::span<const LocalTransform> locals = ecs::readColumn<LocalTransform>(view);
    const ecs::Entity* entities = ecs::entitiesOf(view);
    const bool hasParents = ecs::readColumn(view, ecs::parentComponent()) != nullptr;
    const bool chunkChanged = ecs::changedSince(view, ecs::componentIdOf<LocalTransform>, walk.since) ||
                              ecs::changedSince(view, ecs::parentComponent(), walk.since);
    for (size_t i = 0; i < locals.size(); ++i)
    {
        if (hasParents && transformedParent(world, entities[i]) != nullptr)
        {
            continue; // placed by its root
        }
        // A root under a parent without transforms is always recomputed: if
        // the parent just lost them, nothing in this chunk changed. Rare, so
        // cheap.
        const bool moved = chunkChanged || (hasParents && ecs::parentOf(world, entities[i]) != ecs::nullEntity);
        const Matrix4x4 matrix = moved ? place(world, entities[i], localMatrix(locals[i]))
                                       : ecs::read<WorldTransform>(world, entities[i])->matrix;
        placeChildren(walk, entities[i], matrix, moved);
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

void propagate(ecs::World& world, ecs::CommandBuffer* commands, uint32_t since)
{
    // Made per call: they're cheap next to the work, and nothing outlives the
    // world they match.
    const ecs::ComponentId both[] = {ecs::componentIdOf<LocalTransform>, ecs::componentIdOf<WorldTransform>};
    ecs::Query* transformed = ecs::createQuery(world, {.required = both});
    Walk walk{&world, since};
    ecs::parallelForEachChunk(*transformed, placeSubtrees, &walk);
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
        propagate(*context.world, context.commands, context.lastRunVersion);
    }
}

} // namespace

void updateWorldTransforms(ecs::World& world)
{
    // Not knowing what changed since when, it recomputes everything; place
    // still writes only what moved.
    propagate(world, nullptr, 0);
}

Matrix4x4 computeWorldTransform(const ecs::World& world, ecs::Entity entity)
{
    const LocalTransform* local = ecs::read<LocalTransform>(world, entity);
    if (local == nullptr)
    {
        return Matrix4x4::identity();
    }
    Matrix4x4 matrix = localMatrix(*local);
    // Up through the same ancestors propagation would use.
    for (ecs::Entity parent = entity; transformedParent(world, parent) != nullptr;)
    {
        parent = ecs::parentOf(world, parent);
        matrix = matrix * localMatrix(*ecs::read<LocalTransform>(world, parent));
    }
    return matrix;
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
