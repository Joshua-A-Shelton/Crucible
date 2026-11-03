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

// Writes the matrix; stamping is atomic, so jobs may write different
// entities of one chunk.
const Matrix4x4& place(ecs::World& world, ecs::Entity entity, const Matrix4x4& matrix)
{
    WorldTransform* transform = ecs::get<WorldTransform>(world, entity);
    transform->matrix = matrix;
    return transform->matrix;
}

// Depth first, so a parent is final before its children. Every matrix is
// recomputed every run: skipping unchanged ones saved nothing measurable on
// the M5 demonstration (ADR 0056).
void placeChildren(ecs::World& world, ecs::Entity parent, const Matrix4x4& parentMatrix)
{
    for (const ecs::Entity child : ecs::childrenOf(world, parent))
    {
        if (!ecs::hasComponent(world, child, ecs::componentIdOf<WorldTransform>) ||
            !ecs::hasComponent(world, child, ecs::componentIdOf<LocalTransform>))
        {
            continue; // its children are roots of their own
        }
        const Matrix4x4 matrix =
            place(world, child, localMatrix(*ecs::read<LocalTransform>(world, child)) * parentMatrix);
        placeChildren(world, child, matrix);
    }
}

// One job per chunk: each root (no parent, or one without transforms)
// places its subtree. Subtrees are disjoint, so they run in parallel.
void placeSubtrees(const ecs::ChunkView& view, void* data)
{
    ecs::World& world = *static_cast<ecs::World*>(data);
    const std::span<const LocalTransform> locals = ecs::readColumn<LocalTransform>(view);
    const ecs::Entity* entities = ecs::entitiesOf(view);
    const bool hasParents = ecs::readColumn(view, ecs::parentComponent()) != nullptr;
    for (size_t i = 0; i < locals.size(); ++i)
    {
        if (hasParents && transformedParent(world, entities[i]) != nullptr)
        {
            continue; // placed by its root
        }
        placeChildren(world, entities[i], place(world, entities[i], localMatrix(locals[i])));
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
    ecs::parallelForEachChunk(*transformed, placeSubtrees, &world);
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
