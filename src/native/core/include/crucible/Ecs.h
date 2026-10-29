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

// Where a component's values live (ADR 0023, 0035).
enum class CRUCIBLE_EXPOSE ComponentStorage : uint8_t
{
    // In the entity's archetype: a column in its chunk, iterated by queries.
    Archetype,
    // In a per-world sparse set, for components added and removed
    // constantly: adding and removing never move the entity. Queries don't
    // see them; read them per entity, or walk them with forEachSparse.
    Sparse,
    // A C# class or a struct holding references (ADR 0012, 0035): the column
    // holds an int32 slot in a C# pool (slot + 1; 0 is empty), and the ECS
    // reports each slot it drops through the managed release callback.
    Managed,
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
CRUCIBLE_EXPOSE_INTERNAL ComponentId registerComponent(std::string_view name, uint32_t size, uint32_t alignment,
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

// Hot reload (ADR 0035): a component whose layout changed gets a new ID, and
// every world's values move to it. These run between frames, with no world
// iterating and no other ECS calls in flight; queries and command buffers
// naming a retired ID match and change nothing afterwards.

// Frees the component's name, so registerComponent gives that name a new ID;
// existing values keep the old one until migrated or removed.
CRUCIBLE_EXPOSE_INTERNAL void retireComponent(ComponentId component);

// In every live world, gives each entity with `from` a `to` instead: the
// byte ranges in `copies`, as (from offset, to offset, size) triples, are
// copied and the rest of `to` is zeroed. Both are blittable, with the same
// storage; managed components keep their ID (C# migrates their pools).
CRUCIBLE_EXPOSE_INTERNAL void migrateComponent(ComponentId from, ComponentId to, std::span<const uint32_t> copies);

// Removes the component from every entity in every live world, releasing
// managed slots: for a component type the reloaded game no longer has.
CRUCIBLE_EXPOSE_INTERNAL void removeComponentEverywhere(ComponentId component);

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

struct CRUCIBLE_EXPOSE World;

// Exposed to C# as internal functions (ADR 0035): the public C# API is the
// engine's World, Query, and CommandBuffer classes built on them.
CRUCIBLE_EXPOSE_INTERNAL World* createWorld();
// Destroys every entity and frees the world's memory. Its queries and
// command buffers must be destroyed first.
CRUCIBLE_EXPOSE_INTERNAL void destroyWorld(World* world);

// A new entity with `components`, zero-initialized.
CRUCIBLE_EXPOSE_INTERNAL Entity createEntity(World& world, std::span<const ComponentId> components = {});
// Destroys the entity and its descendants (ADR 0033). Does nothing for a
// dead entity.
CRUCIBLE_EXPOSE_INTERNAL void destroyEntity(World& world, Entity entity);
CRUCIBLE_EXPOSE_INTERNAL bool isAlive(const World& world, Entity entity);
CRUCIBLE_EXPOSE_INTERNAL uint32_t entityCount(const World& world);

// Adding moves the entity to the archetype with the component (zero-
// initialized); adding one it has, or removing one it lacks, does nothing.
CRUCIBLE_EXPOSE_INTERNAL void addComponent(World& world, Entity entity, ComponentId component);
CRUCIBLE_EXPOSE_INTERNAL void removeComponent(World& world, Entity entity, ComponentId component);
CRUCIBLE_EXPOSE_INTERNAL bool hasComponent(const World& world, Entity entity, ComponentId component);

// The component's value, or null if the entity is dead or lacks it (or it's
// a tag). Writable access stamps the column's change version (ADR 0034).
CRUCIBLE_EXPOSE_INTERNAL void* writeComponent(World& world, Entity entity, ComponentId component);
CRUCIBLE_EXPOSE_INTERNAL const void* readComponent(const World& world, Entity entity, ComponentId component);

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
CRUCIBLE_EXPOSE_INTERNAL uint32_t advanceVersion(World& world);
CRUCIBLE_EXPOSE_INTERNAL uint32_t currentVersion(const World& world);

// ---------------------------------------------------------------------------
// Queries and chunk iteration
// ---------------------------------------------------------------------------

struct QueryDesc
{
    std::span<const ComponentId> required; // entities must have all of these
    std::span<const ComponentId> excluded; // and none of these
};

struct CRUCIBLE_EXPOSE Query;

// Matches archetypes as they appear, including ones created later.
Query* createQuery(World& world, const QueryDesc& desc);
CRUCIBLE_EXPOSE_INTERNAL void destroyQuery(Query* query);

struct CRUCIBLE_EXPOSE Chunk;

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
// The same for the chunk an entity is in: true if any entity there changed
// (versions are per chunk). False for a dead entity or one without the
// component; always true for a sparse component, which keeps no versions.
bool changedSince(const World& world, Entity entity, ComponentId component, uint32_t version);

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

CRUCIBLE_EXPOSE_INTERNAL ComponentId parentComponent();

inline constexpr uint32_t appendChild = ~0u;

// Makes `child` a child of `parent` at position `index` among its children
// (appendChild: last), or a root for nullEntity. InvalidData if either is
// dead or the move would make an entity its own ancestor. Reparenting to the
// same parent moves the child to `index`.
CRUCIBLE_EXPOSE_INTERNAL Result<void> setParent(World& world, Entity child, Entity parent,
                                                uint32_t index = appendChild);
CRUCIBLE_EXPOSE_INTERNAL Entity parentOf(const World& world, Entity child);
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
struct CRUCIBLE_EXPOSE CommandBuffer;

CRUCIBLE_EXPOSE_INTERNAL CommandBuffer* createCommandBuffer(World& world);
// Unapplied commands are discarded; their reserved entities are released.
CRUCIBLE_EXPOSE_INTERNAL void destroyCommandBuffer(CommandBuffer* buffer);

// The returned entity is reserved now and final: it can be stored in
// components before the buffer is applied, and isn't alive until then.
CRUCIBLE_EXPOSE_INTERNAL Entity recordCreate(CommandBuffer& buffer, std::span<const ComponentId> components = {});
CRUCIBLE_EXPOSE_INTERNAL void recordDestroy(CommandBuffer& buffer, Entity entity);
CRUCIBLE_EXPOSE_INTERNAL void recordAdd(CommandBuffer& buffer, Entity entity, ComponentId component);
CRUCIBLE_EXPOSE_INTERNAL void recordRemove(CommandBuffer& buffer, Entity entity, ComponentId component);
// Copies the component's value now; adds the component if missing.
CRUCIBLE_EXPOSE_INTERNAL void recordSet(CommandBuffer& buffer, Entity entity, ComponentId component, const void* value);
CRUCIBLE_EXPOSE_INTERNAL void recordSetParent(CommandBuffer& buffer, Entity child, Entity parent,
                                              uint32_t index = appendChild);

template <class T>
void recordSet(CommandBuffer& buffer, Entity entity, const T& value)
{
    recordSet(buffer, entity, componentIdOf<T>, &value);
}

// Applies and clears the buffer, keeping its memory for the next frame. The
// world must not be iterating.
CRUCIBLE_EXPOSE_INTERNAL void apply(CommandBuffer& buffer);
// Discards the recorded commands, releasing reserved entities.
CRUCIBLE_EXPOSE_INTERNAL void clear(CommandBuffer& buffer);

// The size of every chunk; columns are laid out to fill it (ADR 0023).
inline constexpr uint32_t chunkBytes = 16 * 1024;

// Entities per chunk for this world's archetype with exactly `components`
// (testing and tuning).
uint32_t chunkCapacityFor(World& world, std::span<const ComponentId> components);

// ---------------------------------------------------------------------------
// C-shaped forms for C# (ADR 0035); C++ code uses the forms above.
// ---------------------------------------------------------------------------

CRUCIBLE_EXPOSE_INTERNAL Query* createQueryFrom(World& world, std::span<const ComponentId> required,
                                                std::span<const ComponentId> excluded);

// Fills `chunks` with the addresses of the query's non-empty chunks (as many
// as fit) and returns how many there are.
CRUCIBLE_EXPOSE_INTERNAL uint32_t gatherChunks(Query& query, std::span<uint64_t> chunks);
// Every non-empty chunk in the world, whatever its components: for saving
// scenes (ADR 0041). Returns the total; fills as many as fit.
CRUCIBLE_EXPOSE_INTERNAL uint32_t gatherWorldChunks(const World& world, std::span<uint64_t> chunks);
// The chunk's components (tags included, sparse ones not), sorted.
// Returns the total; fills as many as fit.
CRUCIBLE_EXPOSE_INTERNAL uint32_t chunkComponents(const Chunk* chunk, std::span<uint32_t> components);

CRUCIBLE_EXPOSE_INTERNAL uint32_t chunkEntityCount(const Chunk* chunk);
// The chunk's Entity array.
CRUCIBLE_EXPOSE_INTERNAL const void* chunkEntities(const Chunk* chunk);
// The column's start, or null; `write` stamps its change version.
CRUCIBLE_EXPOSE_INTERNAL void* chunkColumn(World& world, Chunk* chunk, ComponentId component, bool write);
CRUCIBLE_EXPOSE_INTERNAL bool chunkChangedSince(const Chunk* chunk, ComponentId component, uint32_t version);
CRUCIBLE_EXPOSE_INTERNAL bool entityChangedSince(const World& world, Entity entity, ComponentId component,
                                                 uint32_t version);

// Bracket C#'s chunk iteration, so structural changes during it are caught
// as in forEachChunk.
CRUCIBLE_EXPOSE_INTERNAL void beginIteration(World& world);
CRUCIBLE_EXPOSE_INTERNAL void endIteration(World& world);

// Copies up to `children.size()` of the parent's children, in order, and
// returns how many it has.
CRUCIBLE_EXPOSE_INTERNAL uint32_t childrenInto(const World& world, Entity parent, std::span<Entity> children);

// The engine's C# callback for managed components (ADR 0035): an
// [UnmanagedCallersOnly] method taking (component, slot), as an address. The
// ECS calls it for every managed slot it drops: when the component is
// removed, its entity destroyed, or its world destroyed.
CRUCIBLE_EXPOSE_INTERNAL void setManagedReleaseCallback(uint64_t callbackAddress);

} // namespace crucible::ecs
