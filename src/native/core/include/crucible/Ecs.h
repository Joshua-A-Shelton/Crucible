#pragma once

// Crucible's ECS core (ADR 0012, 0023 and its amendment): worlds of entities
// whose components live in archetypes, each a list of 16 KB chunks with one
// column per component. Systems iterate chunks through queries. Names are
// placeholders (docs/naming.md).
//
//     ComponentId position = registerComponent<Position>("Position");
//     World* world = createWorld();
//     Entity e = createEntity(*world, {&position, 1});
//     get<Position>(*world, e)->x = 1;
//
// Threading: structural changes (create, destroy, add, remove) and query
// creation are for one thread at a time per world, and not while a query is
// iterating it (systems record them in command buffers instead, step 6b).
// Reading and writing components of different chunks may run in parallel,
// as parallelForEachChunk does.

#include <crucible/Expose.h>
#include <crucible/Result.h>

#include <cstddef>
#include <cstdint>
#include <span>
#include <string_view>
#include <type_traits>

namespace crucible::ecs {

// An entity: a 32-bit index and a 32-bit generation (high half), bumped when
// the index is reused so stale references are detected. 0 is no entity.
struct CRUCIBLE_EXPOSE Entity
{
    uint64_t value;
};

inline constexpr Entity nullEntity{0};

constexpr uint32_t indexOf(Entity entity)
{
    return static_cast<uint32_t>(entity.value);
}

constexpr uint32_t generationOf(Entity entity)
{
    return static_cast<uint32_t>(entity.value >> 32);
}

constexpr bool operator==(Entity a, Entity b)
{
    return a.value == b.value;
}

using ComponentId = uint32_t;
inline constexpr ComponentId invalidComponent = ~0u;

// Where a component's values live (ADR 0023).
enum class ComponentStorage : uint8_t
{
    // In the entity's archetype: a column in its chunk, iterated by queries.
    Archetype,
    // In a per-world sparse set, for components added and removed
    // constantly: adding and removing never move the entity. Queries don't
    // see them; read them per entity, or walk them with forEachSparse.
    Sparse,
};

// A sparse component's ID has this bit set, so every call knows where it
// lives without a lookup.
inline constexpr ComponentId sparseBit = 0x80000000u;

constexpr bool isSparse(ComponentId component)
{
    return component != invalidComponent && (component & sparseBit) != 0;
}

// Registers a component type: blittable data of `size` bytes (0 for a tag,
// which has no column), shared by every world. Registering a name again
// returns its existing ID if the size, alignment, and storage match.
// Thread-safe.
ComponentId registerComponent(std::string_view name, uint32_t size, uint32_t alignment,
                              ComponentStorage storage = ComponentStorage::Archetype);

struct ComponentInfo
{
    std::string_view name;
    uint32_t size;
    uint32_t alignment;
    ComponentStorage storage;
};

// Valid for any registered ID.
ComponentInfo componentInfo(ComponentId component);

// The ID registerComponent<T> gave T, or invalidComponent.
template <class T>
inline ComponentId componentIdOf = invalidComponent;

template <class T>
ComponentId registerComponent(std::string_view name, ComponentStorage storage = ComponentStorage::Archetype)
{
    static_assert(std::is_trivially_copyable_v<T>, "components are plain data: they're copied as bytes");
    if (componentIdOf<T> == invalidComponent)
    {
        componentIdOf<T> = registerComponent(name, std::is_empty_v<T> ? 0 : sizeof(T), alignof(T), storage);
    }
    return componentIdOf<T>;
}

// ---------------------------------------------------------------------------
// Worlds and entities
// ---------------------------------------------------------------------------

struct World;

World* createWorld();
// Destroys every entity and frees the world's memory. Its queries and
// command buffers must be destroyed first.
void destroyWorld(World* world);

// A new entity with `components`, zero-initialized.
Entity createEntity(World& world, std::span<const ComponentId> components = {});
// Destroys the entity and its descendants (ADR 0033). Does nothing for a
// dead entity.
void destroyEntity(World& world, Entity entity);
bool isAlive(const World& world, Entity entity);
uint32_t entityCount(const World& world);

// Adding moves the entity to the archetype with the component (zero-
// initialized); adding one it has, or removing one it lacks, does nothing.
void addComponent(World& world, Entity entity, ComponentId component);
void removeComponent(World& world, Entity entity, ComponentId component);
bool hasComponent(const World& world, Entity entity, ComponentId component);

// The component's value, or null if the entity is dead or lacks it (or it's
// a tag). Writable access stamps the column's change version (ADR 0034).
void* writeComponent(World& world, Entity entity, ComponentId component);
const void* readComponent(const World& world, Entity entity, ComponentId component);

template <class T>
T* get(World& world, Entity entity)
{
    return static_cast<T*>(writeComponent(world, entity, componentIdOf<T>));
}

template <class T>
const T* read(const World& world, Entity entity)
{
    return static_cast<const T*>(readComponent(world, entity, componentIdOf<T>));
}

// ---------------------------------------------------------------------------
// Change versions (ADR 0034)
// ---------------------------------------------------------------------------

// Starts a new version and returns it; writes stamp columns with the current
// one. A system remembers the version of its last run and asks for chunks
// changed since.
uint32_t advanceVersion(World& world);
uint32_t currentVersion(const World& world);

// ---------------------------------------------------------------------------
// Queries and chunk iteration
// ---------------------------------------------------------------------------

struct QueryDesc
{
    std::span<const ComponentId> required; // entities must have all of these
    std::span<const ComponentId> excluded; // and none of these
};

struct Query;

// Matches archetypes as they appear, including ones created later.
Query* createQuery(World& world, const QueryDesc& desc);
void destroyQuery(Query* query);

struct Chunk;

// One chunk during iteration: `count` entities and their columns. Any
// component may be asked for; a column the chunk lacks (an optional one) is
// null.
struct ChunkView
{
    World* world;
    Chunk* chunk;
};

uint32_t entityCountOf(const ChunkView& view);
const Entity* entitiesOf(const ChunkView& view);
const void* readColumn(const ChunkView& view, ComponentId component);
// Stamps the column's change version.
void* writeColumn(const ChunkView& view, ComponentId component);
// Whether the column was written, or the chunk's entities changed, after
// `version`.
bool changedSince(const ChunkView& view, ComponentId component, uint32_t version);

template <class T>
std::span<const T> readColumn(const ChunkView& view)
{
    return {static_cast<const T*>(readColumn(view, componentIdOf<T>)), entityCountOf(view)};
}

template <class T>
std::span<T> writeColumn(const ChunkView& view)
{
    return {static_cast<T*>(writeColumn(view, componentIdOf<T>)), entityCountOf(view)};
}

using ChunkFunction = void (*)(const ChunkView& view, void* data);

// Calls `function` for each non-empty chunk the query matches, in order.
void forEachChunk(Query& query, ChunkFunction function, void* data);
// The same with one job per chunk (ADR 0014), returning when all are done.
void parallelForEachChunk(Query& query, ChunkFunction function, void* data);

// Calls function(entity, value, data) for every entity with the sparse
// `component` (value is null for a tag), in no particular order. No
// structural changes meanwhile.
using SparseFunction = void (*)(Entity entity, void* value, void* data);
void forEachSparse(World& world, ComponentId component, SparseFunction function, void* data);

// ---------------------------------------------------------------------------
// Hierarchy (ADR 0033)
// ---------------------------------------------------------------------------

// The built-in component on every entity with a parent. Read it like any
// component, but change it only with setParent, which keeps the parent's
// ordered children list in step.
struct Parent
{
    Entity entity;
};

ComponentId parentComponent();

inline constexpr uint32_t appendChild = ~0u;

// Makes `child` a child of `parent` at position `index` among its children
// (appendChild: last), or a root for nullEntity. InvalidData if either is
// dead or the move would make an entity its own ancestor. Reparenting to the
// same parent moves the child to `index`.
Result<void> setParent(World& world, Entity child, Entity parent, uint32_t index = appendChild);
Entity parentOf(const World& world, Entity child);
// In order; valid until the next change to the hierarchy.
std::span<const Entity> childrenOf(const World& world, Entity parent);

// ---------------------------------------------------------------------------
// Command buffers
// ---------------------------------------------------------------------------

// Structural changes recorded while queries iterate a world, applied later
// at a sync point (ADR 0013, 0023). Recording is thread-safe and takes no
// lock on worker threads (one segment per worker); playback applies each
// thread's commands in order, segment by segment. Commands on entities that
// died meanwhile are ignored.
struct CommandBuffer;

CommandBuffer* createCommandBuffer(World& world);
// Unapplied commands are discarded; their reserved entities are released.
void destroyCommandBuffer(CommandBuffer* buffer);

// The returned entity is reserved now and final: it can be stored in
// components before the buffer is applied, and isn't alive until then.
Entity recordCreate(CommandBuffer& buffer, std::span<const ComponentId> components = {});
void recordDestroy(CommandBuffer& buffer, Entity entity);
void recordAdd(CommandBuffer& buffer, Entity entity, ComponentId component);
void recordRemove(CommandBuffer& buffer, Entity entity, ComponentId component);
// Copies the component's value now; adds the component if missing.
void recordSet(CommandBuffer& buffer, Entity entity, ComponentId component, const void* value);
void recordSetParent(CommandBuffer& buffer, Entity child, Entity parent, uint32_t index = appendChild);

template <class T>
void recordSet(CommandBuffer& buffer, Entity entity, const T& value)
{
    recordSet(buffer, entity, componentIdOf<T>, &value);
}

// Applies and clears the buffer, keeping its memory for the next frame. The
// world must not be iterating.
void apply(CommandBuffer& buffer);
// Discards the recorded commands, releasing reserved entities.
void clear(CommandBuffer& buffer);

// The size of every chunk; columns are laid out to fill it (ADR 0023).
inline constexpr uint32_t chunkBytes = 16 * 1024;

// Entities per chunk for this world's archetype with exactly `components`
// (testing and tuning).
uint32_t chunkCapacityFor(World& world, std::span<const ComponentId> components);

} // namespace crucible::ecs
