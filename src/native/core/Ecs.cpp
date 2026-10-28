#include <crucible/Ecs.h>
#include <crucible/HashMap.h>
#include <crucible/Jobs.h>
#include <crucible/Memory.h>
#include <crucible/SmallVector.h>
#include <crucible/Vector.h>

#include <algorithm>
#include <atomic>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <mutex>

namespace crucible::ecs {

namespace {

[[noreturn]] void fatal(const char* message)
{
    std::fprintf(stderr, "crucible: ecs: %s\n", message);
    std::fflush(stderr);
    std::abort();
}

// Misuse that would corrupt the world: checked in debug builds only, until
// Crucible has a validation layer of its own.
void validate(bool condition, const char* message)
{
#if !defined(NDEBUG)
    if (!condition)
    {
        fatal(message);
    }
#else
    (void)condition;
    (void)message;
#endif
}

// Component types, shared by every world (ADR 0023: registered at runtime).
struct ComponentRecord
{
    Vector<char> name;
    uint32_t size;
    uint32_t alignment;
    ComponentStorage storage;
    // Replaced by a new layout on hot reload (ADR 0035): its name can be
    // registered again, and the ID lives on only in worlds not yet migrated.
    bool retired = false;
};

struct Registry
{
    std::mutex mutex;
    Vector<ComponentRecord> components{MemoryTag::Ecs};
    // Every live world, so hot reload can migrate them all.
    Vector<World*> worlds{MemoryTag::Ecs};
};

Registry& registry()
{
    static Registry instance;
    return instance;
}

uint32_t alignUp(uint32_t value, uint32_t alignment)
{
    return (value + alignment - 1) & ~(alignment - 1);
}

} // namespace

// One column of an archetype: where component values sit in each chunk.
struct Column
{
    ComponentId component;
    uint32_t size; // 0 for a tag, which has no storage
    uint32_t offset;
    bool managed; // holds a C# pool slot (ADR 0035)
};

struct Archetype
{
    Vector<ComponentId> components{MemoryTag::Ecs}; // sorted
    Vector<Column> columns{MemoryTag::Ecs};         // in the same order
    uint32_t capacity = 0;                          // entities per chunk
    // Every chunk full but the last, so iteration never meets a gap.
    Vector<Chunk*> chunks{MemoryTag::Ecs};
    // The archetype one component away, cached as moves happen.
    HashMap<ComponentId, Archetype*> addEdges{MemoryTag::Ecs};
    HashMap<ComponentId, Archetype*> removeEdges{MemoryTag::Ecs};
};

// A 16 KB block: `count` entity IDs at the start, then each column.
struct Chunk
{
    Archetype* archetype;
    std::byte* data;
    uint32_t count;
    uint32_t structuralVersion;
    Vector<uint32_t> versions{MemoryTag::Ecs}; // per column (ADR 0034)
};

struct EntityRecord
{
    uint32_t generation = 1;
    bool alive = false;
    Chunk* chunk = nullptr;
    uint32_t row = 0;
};

// A sparse component's values (ADR 0023): dense arrays of values and their
// entities, and each entity's position, so adding and removing are O(1) and
// never move the entity.
struct SparseSet
{
    uint32_t size; // 0 for a tag
    uint32_t alignment;
    std::byte* values = nullptr;
    uint32_t capacity = 0;
    Vector<Entity> entities{MemoryTag::Ecs};
    HashMap<uint32_t, uint32_t> positions{MemoryTag::Ecs}; // entity index to dense position
};

struct World
{
    Vector<EntityRecord> records{MemoryTag::Ecs};
    // Destroyed entities' indices, for reuse (ADR 0023's amendment). Entries
    // below freeAvailable can still be taken; those above it were taken
    // already, by takeIndex from any thread, and are dropped the next time
    // the list changes (releaseIndex), which never overlaps taking.
    Vector<uint32_t> freeIndices{MemoryTag::Ecs};
    std::atomic<uint32_t> freeAvailable{0};
    uint32_t aliveCount = 0;
    Vector<Archetype*> archetypes{MemoryTag::Ecs};
    // Archetypes by a hash of their sorted component IDs; a list per hash, in
    // case two sets collide.
    HashMap<uint64_t, SmallVector<Archetype*, 1>> archetypeLookup{MemoryTag::Ecs};
    // Chunk blocks no longer in use, reused before allocating more.
    Vector<std::byte*> freeBlocks{MemoryTag::Ecs};
    uint32_t version = 1;
    std::atomic<uint32_t> iterating{0};
    // Indices never used yet start here. Atomic, because command buffers
    // reserve entities from any thread while queries iterate.
    std::atomic<uint32_t> nextFreshIndex{0};
    HashMap<ComponentId, SparseSet*> sparseSets{MemoryTag::Ecs};
    // Each parent's children, in order (ADR 0033), by the parent's index.
    HashMap<uint32_t, Vector<Entity>> children{MemoryTag::Ecs};
};

struct Query
{
    World* world;
    Vector<ComponentId> required{MemoryTag::Ecs};
    Vector<ComponentId> excluded{MemoryTag::Ecs};
    Vector<Archetype*> matched{MemoryTag::Ecs};
    size_t archetypesSeen = 0;
};

namespace {

// ---------------------------------------------------------------------------
// Archetypes
// ---------------------------------------------------------------------------

uint64_t hashComponents(std::span<const ComponentId> components)
{
    uint64_t hash = 14695981039346656037ull; // FNV-1a
    for (ComponentId component : components)
    {
        hash = (hash ^ component) * 1099511628211ull;
    }
    return hash;
}

bool sameComponents(const Archetype& archetype, std::span<const ComponentId> components)
{
    return archetype.components.size() == components.size() &&
           std::equal(components.begin(), components.end(), archetype.components.begin());
}

int columnIndex(const Archetype& archetype, ComponentId component)
{
    const ComponentId* begin = archetype.components.begin();
    const ComponentId* end = archetype.components.end();
    const ComponentId* found = std::lower_bound(begin, end, component);
    return found != end && *found == component ? static_cast<int>(found - begin) : -1;
}

// The most entities whose IDs and columns, each aligned for its type, fit
// in a chunk.
void layOut(Archetype& archetype)
{
    uint32_t perEntity = sizeof(Entity);
    for (const Column& column : archetype.columns)
    {
        perEntity += column.size;
    }
    for (uint32_t capacity = chunkBytes / perEntity; capacity > 0; --capacity)
    {
        uint32_t offset = capacity * static_cast<uint32_t>(sizeof(Entity));
        bool fits = true;
        for (Column& column : archetype.columns)
        {
            if (column.size == 0)
            {
                continue;
            }
            offset = alignUp(offset, componentInfo(column.component).alignment);
            column.offset = offset;
            offset += column.size * capacity;
            fits = fits && offset <= chunkBytes;
        }
        if (fits)
        {
            archetype.capacity = capacity;
            return;
        }
    }
    fatal("one entity's components are larger than a chunk (16 KB); store large data elsewhere");
}

// `components` sorted and without duplicates.
Archetype* findArchetype(World& world, std::span<const ComponentId> components)
{
    const uint64_t hash = hashComponents(components);
    auto [candidates, inserted] = world.archetypeLookup.tryEmplace(hash, MemoryTag::Ecs);
    for (Archetype* candidate : *candidates)
    {
        if (sameComponents(*candidate, components))
        {
            return candidate;
        }
    }

    Archetype* archetype = create<Archetype>(MemoryTag::Ecs);
    archetype->components.assign(components);
    for (ComponentId component : components)
    {
        const ComponentInfo info = componentInfo(component);
        archetype->columns.pushBack({component, info.size, 0, info.storage == ComponentStorage::Managed});
    }
    layOut(*archetype);
    candidates->pushBack(archetype);
    world.archetypes.pushBack(archetype);
    return archetype;
}

Archetype* archetypeWith(World& world, Archetype& from, ComponentId component)
{
    if (Archetype** cached = from.addEdges.find(component))
    {
        return *cached;
    }
    SmallVector<ComponentId, 16> components(MemoryTag::Ecs);
    components.assign(from.components.span());
    components.pushBack(component);
    std::sort(components.begin(), components.end());
    Archetype* to = findArchetype(world, components.span());
    from.addEdges.insertOrAssign(component, to);
    return to;
}

Archetype* archetypeWithout(World& world, Archetype& from, ComponentId component)
{
    if (Archetype** cached = from.removeEdges.find(component))
    {
        return *cached;
    }
    SmallVector<ComponentId, 16> components(MemoryTag::Ecs);
    for (ComponentId existing : from.components)
    {
        if (existing != component)
        {
            components.pushBack(existing);
        }
    }
    Archetype* to = findArchetype(world, components.span());
    from.removeEdges.insertOrAssign(component, to);
    return to;
}

// ---------------------------------------------------------------------------
// Rows
// ---------------------------------------------------------------------------

std::byte* valueAt(const Chunk& chunk, const Column& column, uint32_t row)
{
    return chunk.data + column.offset + static_cast<size_t>(row) * column.size;
}

Entity* entityIds(const Chunk& chunk)
{
    return reinterpret_cast<Entity*>(chunk.data);
}

// A new entity counts as a change to every column (ADR 0034).
void stampAll(World& world, Chunk& chunk)
{
    chunk.structuralVersion = world.version;
    for (uint32_t& version : chunk.versions)
    {
        version = world.version;
    }
}

Chunk* newChunk(World& world, Archetype& archetype)
{
    std::byte* block = nullptr;
    if (!world.freeBlocks.empty())
    {
        block = world.freeBlocks.back();
        world.freeBlocks.popBack();
    }
    else
    {
        // 64-byte aligned: a cache line, and enough for any SIMD load.
        block = static_cast<std::byte*>(allocate(chunkBytes, 64, MemoryTag::Ecs));
    }
    Chunk* chunk = create<Chunk>(MemoryTag::Ecs);
    chunk->archetype = &archetype;
    chunk->data = block;
    chunk->count = 0;
    chunk->versions.resize(archetype.columns.size());
    archetype.chunks.pushBack(chunk);
    return chunk;
}

// Appends `entity` to the archetype's last chunk; its values are the
// caller's to fill.
void addRow(World& world, Archetype& archetype, Entity entity, Chunk*& chunk, uint32_t& row)
{
    chunk = archetype.chunks.empty() || archetype.chunks.back()->count == archetype.capacity
                ? newChunk(world, archetype)
                : archetype.chunks.back();
    row = chunk->count++;
    entityIds(*chunk)[row] = entity;
    stampAll(world, *chunk);
}

void zeroRow(Chunk& chunk, uint32_t row)
{
    for (const Column& column : chunk.archetype->columns)
    {
        if (column.size != 0)
        {
            std::memset(valueAt(chunk, column, row), 0, column.size);
        }
    }
}

// Removes a row, keeping the archetype compact: the archetype's last entity
// moves into the hole, and an emptied last chunk is freed.
void removeRow(World& world, Chunk& chunk, uint32_t row)
{
    Archetype& archetype = *chunk.archetype;
    Chunk& last = *archetype.chunks.back();
    const uint32_t lastRow = last.count - 1;
    if (&last != &chunk || lastRow != row)
    {
        for (const Column& column : archetype.columns)
        {
            if (column.size != 0)
            {
                std::memcpy(valueAt(chunk, column, row), valueAt(last, column, lastRow), column.size);
            }
        }
        const Entity moved = entityIds(last)[lastRow];
        entityIds(chunk)[row] = moved;
        EntityRecord& record = world.records[indexOf(moved)];
        record.chunk = &chunk;
        record.row = row;
        stampAll(world, chunk);
    }
    --last.count;
    last.structuralVersion = world.version;
    if (last.count == 0)
    {
        world.freeBlocks.pushBack(last.data);
        archetype.chunks.popBack();
        destroy(MemoryTag::Ecs, &last);
    }
}

using ManagedRelease = void (*)(ComponentId component, int32_t slot);

std::atomic<ManagedRelease> managedRelease{nullptr};

// Reports the managed slots in a row to C# (ADR 0035), except columns that
// `keep` also has (the entity is moving there with them).
void releaseManaged(const Chunk& chunk, uint32_t row, const Archetype* keep = nullptr)
{
    const ManagedRelease release = managedRelease.load(std::memory_order_acquire);
    for (const Column& column : chunk.archetype->columns)
    {
        if (!column.managed || (keep != nullptr && columnIndex(*keep, column.component) >= 0))
        {
            continue;
        }
        int32_t slot;
        std::memcpy(&slot, valueAt(chunk, column, row), sizeof(slot));
        if (slot != 0 && release != nullptr)
        {
            release(column.component, slot);
        }
    }
}

// Moves an entity to `to`, keeping the values of the components both
// archetypes have and zeroing the ones only `to` has.
void moveEntity(World& world, Entity entity, Archetype& to)
{
    EntityRecord& record = world.records[indexOf(entity)];
    Chunk& from = *record.chunk;
    const uint32_t fromRow = record.row;

    Chunk* chunk = nullptr;
    uint32_t row = 0;
    addRow(world, to, entity, chunk, row);
    for (const Column& column : to.columns)
    {
        if (column.size == 0)
        {
            continue;
        }
        const int source = columnIndex(*from.archetype, column.component);
        if (source >= 0)
        {
            std::memcpy(valueAt(*chunk, column, row), valueAt(from, from.archetype->columns[source], fromRow),
                        column.size);
        }
        else
        {
            std::memset(valueAt(*chunk, column, row), 0, column.size);
        }
    }
    releaseManaged(from, fromRow, &to);
    removeRow(world, from, fromRow);
    // removeRow may have moved another entity, never this one: it's gone from
    // `from` only now.
    record.chunk = chunk;
    record.row = row;
}

const EntityRecord* liveRecord(const World& world, Entity entity)
{
    const uint32_t index = indexOf(entity);
    if (index >= world.records.size())
    {
        return nullptr;
    }
    const EntityRecord& record = world.records[index];
    return record.alive && record.generation == generationOf(entity) ? &record : nullptr;
}

// A destroyed entity's index if one is free, else a fresh one. Lock-free,
// so command buffers can reserve entities from every worker at once; never
// at the same time as releaseIndex (direct structural changes and playback
// don't run while other threads record).
uint32_t takeIndex(World& world)
{
    uint32_t available = world.freeAvailable.load(std::memory_order_acquire);
    while (available > 0 &&
           !world.freeAvailable.compare_exchange_weak(available, available - 1, std::memory_order_acq_rel))
    {
    }
    return available > 0 ? world.freeIndices[available - 1] : world.nextFreshIndex.fetch_add(1);
}

// Frees an index for reuse; the caller has bumped its generation.
void releaseIndex(World& world, uint32_t index)
{
    world.freeIndices.resize(world.freeAvailable.load(std::memory_order_acquire)); // drop taken entries
    world.freeIndices.pushBack(index);
    world.freeAvailable.store(static_cast<uint32_t>(world.freeIndices.size()), std::memory_order_release);
}

void checkNotIterating(const World& world)
{
    validate(world.iterating.load() == 0,
             "structural change while a query iterates this world; record it in a command buffer");
}

void updateMatches(Query& query)
{
    const World& world = *query.world;
    for (; query.archetypesSeen < world.archetypes.size(); ++query.archetypesSeen)
    {
        Archetype* archetype = world.archetypes[query.archetypesSeen];
        const bool hasAll = std::includes(archetype->components.begin(), archetype->components.end(),
                                          query.required.begin(), query.required.end());
        const bool hasExcluded = std::any_of(query.excluded.begin(), query.excluded.end(),
                                             [&](ComponentId c) { return columnIndex(*archetype, c) >= 0; });
        if (hasAll && !hasExcluded)
        {
            query.matched.pushBack(archetype);
        }
    }
}

// ---------------------------------------------------------------------------
// Sparse sets
// ---------------------------------------------------------------------------

SparseSet* sparseSetFor(World& world, ComponentId component)
{
    if (SparseSet** existing = world.sparseSets.find(component))
    {
        return *existing;
    }
    const ComponentInfo info = componentInfo(component);
    SparseSet* set = create<SparseSet>(MemoryTag::Ecs);
    set->size = info.size;
    set->alignment = info.alignment;
    world.sparseSets.insertOrAssign(component, set);
    return set;
}

void destroySparseSet(SparseSet* set)
{
    if (set->values != nullptr)
    {
        free(set->values, static_cast<size_t>(set->capacity) * set->size, MemoryTag::Ecs);
    }
    destroy(MemoryTag::Ecs, set);
}

// The entity's value, or null if it lacks the component or it's a tag.
void* sparseValue(const SparseSet& set, Entity entity)
{
    const uint32_t* position = set.positions.find(indexOf(entity));
    if (position == nullptr || set.size == 0)
    {
        return nullptr;
    }
    return set.values + static_cast<size_t>(*position) * set.size;
}

// Adds the entity with a zeroed value, unless it's there already.
void sparseAdd(SparseSet& set, Entity entity)
{
    if (set.positions.contains(indexOf(entity)))
    {
        return;
    }
    const auto position = static_cast<uint32_t>(set.entities.size());
    if (set.size != 0 && position == set.capacity)
    {
        const uint32_t capacity = set.capacity < 16 ? 16 : set.capacity * 2;
        auto* grown =
            static_cast<std::byte*>(allocate(static_cast<size_t>(capacity) * set.size, set.alignment, MemoryTag::Ecs));
        if (set.values != nullptr)
        {
            std::memcpy(grown, set.values, static_cast<size_t>(position) * set.size);
            free(set.values, static_cast<size_t>(set.capacity) * set.size, MemoryTag::Ecs);
        }
        set.values = grown;
        set.capacity = capacity;
    }
    if (set.size != 0)
    {
        std::memset(set.values + static_cast<size_t>(position) * set.size, 0, set.size);
    }
    set.entities.pushBack(entity);
    set.positions.insertOrAssign(indexOf(entity), position);
}

// Removes the entity if present: the last value moves into its place.
void sparseRemove(SparseSet& set, Entity entity)
{
    const uint32_t* found = set.positions.find(indexOf(entity));
    if (found == nullptr || !(set.entities[*found] == entity))
    {
        return;
    }
    const uint32_t position = *found;
    const auto last = static_cast<uint32_t>(set.entities.size() - 1);
    if (position != last)
    {
        if (set.size != 0)
        {
            std::memcpy(set.values + static_cast<size_t>(position) * set.size,
                        set.values + static_cast<size_t>(last) * set.size, set.size);
        }
        const Entity moved = set.entities[last];
        set.entities[position] = moved;
        set.positions.insertOrAssign(indexOf(moved), position);
    }
    set.entities.popBack();
    set.positions.erase(indexOf(entity));
}

// ---------------------------------------------------------------------------
// Creation and the hierarchy
// ---------------------------------------------------------------------------

// Creates an entity at a free or reserved index, with the archetype for its
// archetype components and its sparse ones added after.
Entity createAt(World& world, uint32_t index, std::span<const ComponentId> components)
{
    SmallVector<ComponentId, 16> sorted(MemoryTag::Ecs);
    for (ComponentId component : components)
    {
        if (!isSparse(component))
        {
            sorted.pushBack(component);
        }
    }
    std::sort(sorted.begin(), sorted.end());
    sorted.resize(static_cast<size_t>(std::unique(sorted.begin(), sorted.end()) - sorted.begin()));
    Archetype* archetype = findArchetype(world, sorted.span());

    if (index >= world.records.size())
    {
        world.records.resize(index + 1);
    }
    EntityRecord& record = world.records[index];
    record.alive = true;
    const Entity entity{(static_cast<uint64_t>(record.generation) << 32) | index};
    addRow(world, *archetype, entity, record.chunk, record.row);
    zeroRow(*record.chunk, record.row);
    ++world.aliveCount;

    for (ComponentId component : components)
    {
        if (isSparse(component))
        {
            sparseAdd(*sparseSetFor(world, component), entity);
        }
    }
    return entity;
}

// Takes the entity out of its parent's ordered children list, keeping the
// others' order.
void detachFromParent(World& world, Entity child)
{
    const auto* parent = static_cast<const Parent*>(readComponent(world, child, parentComponent()));
    if (parent == nullptr)
    {
        return;
    }
    Vector<Entity>* siblings = world.children.find(indexOf(parent->entity));
    if (siblings == nullptr)
    {
        return;
    }
    Entity* found = std::find(siblings->begin(), siblings->end(), child);
    if (found != siblings->end())
    {
        std::rotate(found, found + 1, siblings->end());
        siblings->popBack();
    }
    if (siblings->empty())
    {
        world.children.erase(indexOf(parent->entity));
    }
}

} // namespace

// ---------------------------------------------------------------------------
// Components
// ---------------------------------------------------------------------------

ComponentId registerComponent(std::string_view name, uint32_t size, uint32_t alignment, ComponentStorage storage)
{
    if (alignment == 0 || (alignment & (alignment - 1)) != 0)
    {
        fatal("component alignment must be a power of two");
    }
    Registry& components = registry();
    std::lock_guard lock(components.mutex);
    for (size_t id = 0; id < components.components.size(); ++id)
    {
        const ComponentRecord& existing = components.components[id];
        if (!existing.retired && std::string_view(existing.name.data(), existing.name.size()) == name)
        {
            if (existing.size != size || existing.alignment != alignment || existing.storage != storage)
            {
                fatal("a component was registered again with a different size, alignment, or storage");
            }
            return static_cast<ComponentId>(id) | (storage == ComponentStorage::Sparse ? sparseBit : 0);
        }
    }
    ComponentRecord& record = components.components.emplaceBack();
    record.name = Vector<char>(MemoryTag::Ecs);
    record.name.assign(std::span<const char>(name.data(), name.size()));
    record.size = size;
    record.alignment = alignment;
    record.storage = storage;
    const auto id = static_cast<ComponentId>(components.components.size() - 1);
    return id | (storage == ComponentStorage::Sparse ? sparseBit : 0);
}

ComponentInfo componentInfo(ComponentId component)
{
    Registry& components = registry();
    std::lock_guard lock(components.mutex);
    const ComponentRecord& record = components.components[component & ~sparseBit];
    return {std::string_view(record.name.data(), record.name.size()), record.size, record.alignment, record.storage};
}

// ---------------------------------------------------------------------------
// Hot reload (ADR 0035)
// ---------------------------------------------------------------------------

namespace {

// Taken under the registry lock and used without it: migration calls
// componentInfo, which takes the lock again.
Vector<World*> liveWorlds()
{
    Registry& components = registry();
    std::lock_guard lock(components.mutex);
    return components.worlds.copy();
}

// The entities whose archetype has `component`, gathered before any moves:
// moving them changes the archetypes being walked.
Vector<Entity> entitiesWith(const World& world, ComponentId component)
{
    Vector<Entity> entities(MemoryTag::Ecs);
    for (const Archetype* archetype : world.archetypes)
    {
        if (columnIndex(*archetype, component) < 0)
        {
            continue;
        }
        for (const Chunk* chunk : archetype->chunks)
        {
            for (uint32_t row = 0; row < chunk->count; ++row)
            {
                entities.pushBack(entityIds(*chunk)[row]);
            }
        }
    }
    return entities;
}

void copyFields(std::byte* to, const std::byte* from, std::span<const uint32_t> copies)
{
    for (size_t i = 0; i + 2 < copies.size(); i += 3)
    {
        std::memcpy(to + copies[i + 1], from + copies[i], copies[i + 2]);
    }
}

} // namespace

void retireComponent(ComponentId component)
{
    Registry& components = registry();
    std::lock_guard lock(components.mutex);
    components.components[component & ~sparseBit].retired = true;
}

void migrateComponent(ComponentId from, ComponentId to, std::span<const uint32_t> copies)
{
    const ComponentInfo source = componentInfo(from);
    const ComponentInfo target = componentInfo(to);
    validate(source.storage == target.storage && source.storage != ComponentStorage::Managed,
             "migrateComponent needs two blittable components with the same storage");
    validate(copies.size() % 3 == 0, "migrateComponent takes (from offset, to offset, size) triples");
    for (size_t i = 0; i + 2 < copies.size(); i += 3)
    {
        validate(copies[i] + copies[i + 2] <= source.size && copies[i + 1] + copies[i + 2] <= target.size,
                 "a migrated field lies outside its component");
    }

    for (World* world : liveWorlds())
    {
        checkNotIterating(*world);
        if (isSparse(from))
        {
            SparseSet** found = world->sparseSets.find(from);
            if (found == nullptr)
            {
                continue;
            }
            SparseSet* old = *found;
            SparseSet& set = *sparseSetFor(*world, to);
            for (size_t i = 0; i < old->entities.size(); ++i)
            {
                sparseAdd(set, old->entities[i]);
                if (set.size != 0 && old->size != 0)
                {
                    copyFields(static_cast<std::byte*>(sparseValue(set, old->entities[i])), old->values + i * old->size,
                               copies);
                }
            }
            world->sparseSets.erase(from);
            destroySparseSet(old);
            continue;
        }
        // Two moves per entity, through the archetype holding both: simple,
        // and a reload is rare enough not to need one direct move.
        for (const Entity entity : entitiesWith(*world, from))
        {
            const EntityRecord& record = world->records[indexOf(entity)];
            moveEntity(*world, entity, *archetypeWith(*world, *record.chunk->archetype, to));
            Chunk& chunk = *record.chunk;
            Archetype& both = *chunk.archetype;
            const Column& oldColumn = both.columns[columnIndex(both, from)];
            const Column& newColumn = both.columns[columnIndex(both, to)];
            if (oldColumn.size != 0 && newColumn.size != 0)
            {
                copyFields(valueAt(chunk, newColumn, record.row), valueAt(chunk, oldColumn, record.row), copies);
            }
            moveEntity(*world, entity, *archetypeWithout(*world, both, from));
        }
    }
}

void removeComponentEverywhere(ComponentId component)
{
    for (World* world : liveWorlds())
    {
        checkNotIterating(*world);
        if (isSparse(component))
        {
            if (SparseSet** found = world->sparseSets.find(component))
            {
                SparseSet* set = *found;
                world->sparseSets.erase(component);
                destroySparseSet(set);
            }
            continue;
        }
        for (const Entity entity : entitiesWith(*world, component))
        {
            Archetype& from = *world->records[indexOf(entity)].chunk->archetype;
            moveEntity(*world, entity, *archetypeWithout(*world, from, component)); // releases managed slots
        }
    }
}

// ---------------------------------------------------------------------------
// Worlds and entities
// ---------------------------------------------------------------------------

World* createWorld()
{
    World* world = create<World>(MemoryTag::Ecs);
    Registry& components = registry();
    std::lock_guard lock(components.mutex);
    components.worlds.pushBack(world);
    return world;
}

void destroyWorld(World* world)
{
    if (world == nullptr)
    {
        return;
    }
    {
        Registry& components = registry();
        std::lock_guard lock(components.mutex);
        World** found = std::find(components.worlds.begin(), components.worlds.end(), world);
        *found = components.worlds.back();
        components.worlds.popBack();
    }
    for (Archetype* archetype : world->archetypes)
    {
        for (Chunk* chunk : archetype->chunks)
        {
            for (uint32_t row = 0; row < chunk->count; ++row)
            {
                releaseManaged(*chunk, row);
            }
            free(chunk->data, chunkBytes, MemoryTag::Ecs);
            destroy(MemoryTag::Ecs, chunk);
        }
        destroy(MemoryTag::Ecs, archetype);
    }
    for (std::byte* block : world->freeBlocks)
    {
        free(block, chunkBytes, MemoryTag::Ecs);
    }
    for (auto& entry : world->sparseSets)
    {
        destroySparseSet(entry.value);
    }
    destroy(MemoryTag::Ecs, world);
}

Entity createEntity(World& world, std::span<const ComponentId> components)
{
    checkNotIterating(world);
    return createAt(world, takeIndex(world), components);
}

void destroyEntity(World& world, Entity entity)
{
    checkNotIterating(world);
    if (liveRecord(world, entity) == nullptr)
    {
        return;
    }

    // Descendants first (ADR 0033). The list is copied: destroying a child
    // takes it out of this one.
    if (Vector<Entity>* children = world.children.find(indexOf(entity)))
    {
        Vector<Entity> doomed = children->copy();
        for (const Entity child : doomed)
        {
            destroyEntity(world, child);
        }
        world.children.erase(indexOf(entity));
    }
    detachFromParent(world, entity);
    for (auto& entry : world.sparseSets)
    {
        sparseRemove(*entry.value, entity);
    }

    EntityRecord& record = world.records[indexOf(entity)];
    releaseManaged(*record.chunk, record.row);
    removeRow(world, *record.chunk, record.row);
    record.alive = false;
    record.chunk = nullptr;
    // The next entity at this index gets a new generation, so this one's
    // references go stale. 0 is skipped, so no live entity is ever 0.
    record.generation = record.generation + 1 == 0 ? 1 : record.generation + 1;
    releaseIndex(world, indexOf(entity));
    --world.aliveCount;
}

bool isAlive(const World& world, Entity entity)
{
    return liveRecord(world, entity) != nullptr;
}

uint32_t entityCount(const World& world)
{
    return world.aliveCount;
}

void addComponent(World& world, Entity entity, ComponentId component)
{
    checkNotIterating(world);
    const EntityRecord* record = liveRecord(world, entity);
    if (record == nullptr)
    {
        return;
    }
    if (isSparse(component))
    {
        sparseAdd(*sparseSetFor(world, component), entity);
        return;
    }
    if (columnIndex(*record->chunk->archetype, component) >= 0)
    {
        return;
    }
    moveEntity(world, entity, *archetypeWith(world, *record->chunk->archetype, component));
}

void removeComponent(World& world, Entity entity, ComponentId component)
{
    checkNotIterating(world);
    const EntityRecord* record = liveRecord(world, entity);
    if (record == nullptr)
    {
        return;
    }
    if (isSparse(component))
    {
        if (SparseSet** set = world.sparseSets.find(component))
        {
            sparseRemove(**set, entity);
        }
        return;
    }
    if (columnIndex(*record->chunk->archetype, component) < 0)
    {
        return;
    }
    moveEntity(world, entity, *archetypeWithout(world, *record->chunk->archetype, component));
}

bool hasComponent(const World& world, Entity entity, ComponentId component)
{
    const EntityRecord* record = liveRecord(world, entity);
    if (record == nullptr)
    {
        return false;
    }
    if (isSparse(component))
    {
        SparseSet* const* set = world.sparseSets.find(component);
        return set != nullptr && (*set)->positions.contains(indexOf(entity));
    }
    return columnIndex(*record->chunk->archetype, component) >= 0;
}

void* writeComponent(World& world, Entity entity, ComponentId component)
{
    const EntityRecord* record = liveRecord(world, entity);
    if (record == nullptr)
    {
        return nullptr;
    }
    if (isSparse(component))
    {
        SparseSet** set = world.sparseSets.find(component);
        return set != nullptr ? sparseValue(**set, entity) : nullptr;
    }
    Chunk& chunk = *record->chunk;
    const int index = columnIndex(*chunk.archetype, component);
    if (index < 0 || chunk.archetype->columns[index].size == 0)
    {
        return nullptr;
    }
    chunk.versions[index] = world.version;
    return valueAt(chunk, chunk.archetype->columns[index], record->row);
}

const void* readComponent(const World& world, Entity entity, ComponentId component)
{
    const EntityRecord* record = liveRecord(world, entity);
    if (record == nullptr)
    {
        return nullptr;
    }
    if (isSparse(component))
    {
        SparseSet* const* set = world.sparseSets.find(component);
        return set != nullptr ? sparseValue(**set, entity) : nullptr;
    }
    const Chunk& chunk = *record->chunk;
    const int index = columnIndex(*chunk.archetype, component);
    if (index < 0 || chunk.archetype->columns[index].size == 0)
    {
        return nullptr;
    }
    return valueAt(chunk, chunk.archetype->columns[index], record->row);
}

uint32_t advanceVersion(World& world)
{
    return ++world.version;
}

uint32_t currentVersion(const World& world)
{
    return world.version;
}

// ---------------------------------------------------------------------------
// Queries
// ---------------------------------------------------------------------------

Query* createQuery(World& world, const QueryDesc& desc)
{
    for (ComponentId component : desc.required)
    {
        validate(!isSparse(component), "queries can't require sparse components; read them per entity");
    }
    for (ComponentId component : desc.excluded)
    {
        validate(!isSparse(component), "queries can't exclude sparse components; check them per entity");
    }
    Query* query = create<Query>(MemoryTag::Ecs);
    query->world = &world;
    query->required.assign(desc.required);
    std::sort(query->required.begin(), query->required.end());
    query->excluded.assign(desc.excluded);
    return query;
}

void destroyQuery(Query* query)
{
    destroy(MemoryTag::Ecs, query);
}

uint32_t entityCountOf(const ChunkView& view)
{
    return view.chunk->count;
}

const Entity* entitiesOf(const ChunkView& view)
{
    return entityIds(*view.chunk);
}

const void* readColumn(const ChunkView& view, ComponentId component)
{
    const Chunk& chunk = *view.chunk;
    const int index = columnIndex(*chunk.archetype, component);
    if (index < 0 || chunk.archetype->columns[index].size == 0)
    {
        return nullptr;
    }
    return chunk.data + chunk.archetype->columns[index].offset;
}

void* writeColumn(const ChunkView& view, ComponentId component)
{
    Chunk& chunk = *view.chunk;
    const int index = columnIndex(*chunk.archetype, component);
    if (index < 0 || chunk.archetype->columns[index].size == 0)
    {
        return nullptr;
    }
    chunk.versions[index] = view.world->version;
    return chunk.data + chunk.archetype->columns[index].offset;
}

bool changedSince(const ChunkView& view, ComponentId component, uint32_t version)
{
    const Chunk& chunk = *view.chunk;
    const int index = columnIndex(*chunk.archetype, component);
    return index >= 0 && chunk.versions[index] > version;
}

void forEachChunk(Query& query, ChunkFunction function, void* data)
{
    updateMatches(query);
    World& world = *query.world;
    world.iterating.fetch_add(1);
    for (Archetype* archetype : query.matched)
    {
        for (Chunk* chunk : archetype->chunks)
        {
            function(ChunkView{&world, chunk}, data);
        }
    }
    world.iterating.fetch_sub(1);
}

namespace {

struct ParallelChunks
{
    World* world;
    std::span<Chunk* const> chunks;
    ChunkFunction function;
    void* data;
};

void runChunks(void* data, uint32_t begin, uint32_t end)
{
    const ParallelChunks& work = *static_cast<const ParallelChunks*>(data);
    for (uint32_t i = begin; i < end; ++i)
    {
        work.function(ChunkView{work.world, work.chunks[i]}, work.data);
    }
}

} // namespace

void parallelForEachChunk(Query& query, ChunkFunction function, void* data)
{
    updateMatches(query);
    World& world = *query.world;
    SmallVector<Chunk*, 64> chunks(MemoryTag::Ecs);
    for (Archetype* archetype : query.matched)
    {
        for (Chunk* chunk : archetype->chunks)
        {
            chunks.pushBack(chunk);
        }
    }
    world.iterating.fetch_add(1);
    ParallelChunks work{&world, chunks.span(), function, data};
    jobs::parallelFor(static_cast<uint32_t>(chunks.size()), 1, runChunks, &work);
    world.iterating.fetch_sub(1);
}

uint32_t chunkCapacityFor(World& world, std::span<const ComponentId> components)
{
    SmallVector<ComponentId, 16> sorted(MemoryTag::Ecs);
    sorted.assign(components);
    std::sort(sorted.begin(), sorted.end());
    return findArchetype(world, sorted.span())->capacity;
}

void forEachSparse(World& world, ComponentId component, SparseFunction function, void* data)
{
    SparseSet** set = world.sparseSets.find(component);
    if (set == nullptr)
    {
        return;
    }
    world.iterating.fetch_add(1);
    SparseSet& sparse = **set;
    for (size_t i = 0; i < sparse.entities.size(); ++i)
    {
        function(sparse.entities[i], sparse.size != 0 ? sparse.values + i * sparse.size : nullptr, data);
    }
    world.iterating.fetch_sub(1);
}

// ---------------------------------------------------------------------------
// Hierarchy (ADR 0033)
// ---------------------------------------------------------------------------

ComponentId parentComponent()
{
    static const ComponentId id = registerComponent<Parent>("crucible.Parent");
    return id;
}

Entity parentOf(const World& world, Entity child)
{
    const auto* parent = static_cast<const Parent*>(readComponent(world, child, parentComponent()));
    return parent != nullptr ? parent->entity : nullEntity;
}

std::span<const Entity> childrenOf(const World& world, Entity parent)
{
    if (!isAlive(world, parent))
    {
        return {};
    }
    const Vector<Entity>* children = world.children.find(indexOf(parent));
    return children != nullptr ? children->span() : std::span<const Entity>();
}

Result<void> setParent(World& world, Entity child, Entity parent, uint32_t index)
{
    checkNotIterating(world);
    if (!isAlive(world, child) || (!(parent == nullEntity) && !isAlive(world, parent)) || child == parent)
    {
        return Status::InvalidData;
    }
    // Walking up from the new parent must not reach the child: that would
    // make the child its own ancestor.
    for (Entity ancestor = parent; !(ancestor == nullEntity); ancestor = parentOf(world, ancestor))
    {
        if (ancestor == child)
        {
            return Status::InvalidData;
        }
    }

    detachFromParent(world, child);
    if (parent == nullEntity)
    {
        removeComponent(world, child, parentComponent());
        return {};
    }
    addComponent(world, child, parentComponent());
    static_cast<Parent*>(writeComponent(world, child, parentComponent()))->entity = parent;
    auto [siblings, inserted] = world.children.tryEmplace(indexOf(parent), MemoryTag::Ecs);
    siblings->pushBack(child);
    const size_t position = index < siblings->size() ? index : siblings->size() - 1;
    std::rotate(siblings->begin() + position, siblings->end() - 1, siblings->end());
    return {};
}

// ---------------------------------------------------------------------------
// Command buffers
// ---------------------------------------------------------------------------

namespace {

enum class CommandType : uint32_t
{
    Create,
    Destroy,
    Add,
    Remove,
    Set,
    SetParent,
};

// Followed by `payloadBytes` of payload (component IDs for Create, the value
// for Set), padded to 8 bytes.
struct CommandHeader
{
    CommandType type;
    ComponentId component;
    Entity entity;
    Entity other; // SetParent's parent
    uint32_t payloadBytes;
    uint32_t index; // SetParent's position
};

uint32_t paddedTo8(uint32_t bytes)
{
    return (bytes + 7u) & ~7u;
}

} // namespace

struct CommandSegment
{
    Vector<std::byte> bytes{MemoryTag::Ecs};
    // Indices reserved by recordCreate, released if the buffer is cleared
    // unapplied.
    Vector<uint32_t> reserved{MemoryTag::Ecs};
};

struct CommandBuffer
{
    World* world;
    // Segment 0 is shared by threads that aren't workers, behind a lock;
    // worker w records into segment w + 1 without one.
    Vector<CommandSegment*> segments{MemoryTag::Ecs};
    std::mutex sharedLock;
};

namespace {

// The calling thread's segment, and the lock to hold while writing it (none
// on a worker).
CommandSegment& segmentFor(CommandBuffer& buffer, std::mutex*& lock)
{
    const int32_t worker = jobs::currentWorker();
    if (worker >= 0 && static_cast<size_t>(worker) + 1 < buffer.segments.size())
    {
        lock = nullptr;
        return *buffer.segments[static_cast<size_t>(worker) + 1];
    }
    lock = &buffer.sharedLock;
    return *buffer.segments[0];
}

void record(CommandBuffer& buffer, const CommandHeader& header, const void* payload, uint32_t reservedIndex = ~0u)
{
    std::mutex* lock = nullptr;
    CommandSegment& segment = segmentFor(buffer, lock);
    if (lock != nullptr)
    {
        lock->lock();
    }
    const size_t at = segment.bytes.size();
    segment.bytes.resize(at + sizeof(CommandHeader) + paddedTo8(header.payloadBytes));
    std::memcpy(segment.bytes.data() + at, &header, sizeof(CommandHeader));
    if (header.payloadBytes != 0)
    {
        std::memcpy(segment.bytes.data() + at + sizeof(CommandHeader), payload, header.payloadBytes);
    }
    if (reservedIndex != ~0u)
    {
        segment.reserved.pushBack(reservedIndex);
    }
    if (lock != nullptr)
    {
        lock->unlock();
    }
}

void playBack(World& world, const CommandHeader& header, const std::byte* payload)
{
    switch (header.type)
    {
    case CommandType::Create:
    {
        SmallVector<ComponentId, 16> components(MemoryTag::Ecs);
        components.resize(header.payloadBytes / sizeof(ComponentId));
        std::memcpy(components.data(), payload, header.payloadBytes);
        createAt(world, indexOf(header.entity), components.span());
        break;
    }
    case CommandType::Destroy:
        destroyEntity(world, header.entity);
        break;
    case CommandType::Add:
        addComponent(world, header.entity, header.component);
        break;
    case CommandType::Remove:
        removeComponent(world, header.entity, header.component);
        break;
    case CommandType::Set:
        addComponent(world, header.entity, header.component);
        if (void* value = writeComponent(world, header.entity, header.component))
        {
            std::memcpy(value, payload, header.payloadBytes);
        }
        break;
    case CommandType::SetParent:
        // Moves that became invalid meanwhile (a dead entity, a cycle) are
        // ignored, like commands on dead entities.
        (void)setParent(world, header.entity, header.other, header.index);
        break;
    }
}

} // namespace

CommandBuffer* createCommandBuffer(World& world)
{
    CommandBuffer* buffer = create<CommandBuffer>(MemoryTag::Ecs);
    buffer->world = &world;
    const uint32_t segments = jobs::workerCount() + 1;
    for (uint32_t i = 0; i < segments; ++i)
    {
        buffer->segments.pushBack(create<CommandSegment>(MemoryTag::Ecs));
    }
    return buffer;
}

void destroyCommandBuffer(CommandBuffer* buffer)
{
    if (buffer == nullptr)
    {
        return;
    }
    clear(*buffer);
    for (CommandSegment* segment : buffer->segments)
    {
        destroy(MemoryTag::Ecs, segment);
    }
    destroy(MemoryTag::Ecs, buffer);
}

Entity recordCreate(CommandBuffer& buffer, std::span<const ComponentId> components)
{
    // A free index (its generation was bumped when it was freed) or a fresh
    // one (generation 1): either way the entity is final now.
    World& world = *buffer.world;
    const uint32_t index = takeIndex(world);
    const uint32_t generation = index < world.records.size() ? world.records[index].generation : 1;
    const Entity entity{(uint64_t{generation} << 32) | index};
    const CommandHeader header{
        CommandType::Create, invalidComponent, entity, nullEntity, static_cast<uint32_t>(components.size_bytes()), 0};
    record(buffer, header, components.data(), index);
    return entity;
}

void recordDestroy(CommandBuffer& buffer, Entity entity)
{
    record(buffer, {CommandType::Destroy, invalidComponent, entity, nullEntity, 0, 0}, nullptr);
}

void recordAdd(CommandBuffer& buffer, Entity entity, ComponentId component)
{
    record(buffer, {CommandType::Add, component, entity, nullEntity, 0, 0}, nullptr);
}

void recordRemove(CommandBuffer& buffer, Entity entity, ComponentId component)
{
    record(buffer, {CommandType::Remove, component, entity, nullEntity, 0, 0}, nullptr);
}

void recordSet(CommandBuffer& buffer, Entity entity, ComponentId component, const void* value)
{
    record(buffer, {CommandType::Set, component, entity, nullEntity, componentInfo(component).size, 0}, value);
}

void recordSetParent(CommandBuffer& buffer, Entity child, Entity parent, uint32_t index)
{
    record(buffer, {CommandType::SetParent, invalidComponent, child, parent, 0, index}, nullptr);
}

void apply(CommandBuffer& buffer)
{
    World& world = *buffer.world;
    checkNotIterating(world);
    for (CommandSegment* segment : buffer.segments)
    {
        size_t at = 0;
        while (at < segment->bytes.size())
        {
            CommandHeader header;
            std::memcpy(&header, segment->bytes.data() + at, sizeof(CommandHeader));
            const std::byte* payload = segment->bytes.data() + at + sizeof(CommandHeader);
            playBack(world, header, payload);
            at += sizeof(CommandHeader) + paddedTo8(header.payloadBytes);
        }
        segment->bytes.clear();
        segment->reserved.clear(); // all created
    }
}

void clear(CommandBuffer& buffer)
{
    World& world = *buffer.world;
    for (CommandSegment* segment : buffer.segments)
    {
        // Reserved entities never created: their indices become free, with
        // the generation bumped as if destroyed, since recordCreate already
        // handed out the old one.
        for (uint32_t index : segment->reserved)
        {
            if (index >= world.records.size())
            {
                world.records.resize(index + 1);
            }
            EntityRecord& released = world.records[index];
            released.generation = released.generation + 1 == 0 ? 1 : released.generation + 1;
            releaseIndex(world, index);
        }
        segment->reserved.clear();
        segment->bytes.clear();
    }
}

// ---------------------------------------------------------------------------
// C-shaped forms for C# (ADR 0035)
// ---------------------------------------------------------------------------

Query* createQueryFrom(World& world, std::span<const ComponentId> required, std::span<const ComponentId> excluded)
{
    return createQuery(world, {required, excluded});
}

uint32_t gatherChunks(Query& query, std::span<uint64_t> chunks)
{
    updateMatches(query);
    uint32_t total = 0;
    for (Archetype* archetype : query.matched)
    {
        for (Chunk* chunk : archetype->chunks)
        {
            if (total < chunks.size())
            {
                chunks[total] = reinterpret_cast<uintptr_t>(chunk);
            }
            ++total;
        }
    }
    return total;
}

uint32_t chunkEntityCount(const Chunk* chunk)
{
    return chunk->count;
}

const void* chunkEntities(const Chunk* chunk)
{
    return entityIds(*chunk);
}

void* chunkColumn(World& world, Chunk* chunk, ComponentId component, bool write)
{
    const ChunkView view{&world, chunk};
    return write ? writeColumn(view, component) : const_cast<void*>(readColumn(view, component));
}

bool chunkChangedSince(const Chunk* chunk, ComponentId component, uint32_t version)
{
    const int index = columnIndex(*chunk->archetype, component);
    return index >= 0 && chunk->versions[index] > version;
}

void beginIteration(World& world)
{
    world.iterating.fetch_add(1);
}

void endIteration(World& world)
{
    world.iterating.fetch_sub(1);
}

uint32_t childrenInto(const World& world, Entity parent, std::span<Entity> children)
{
    const std::span<const Entity> all = childrenOf(world, parent);
    std::copy_n(all.begin(), std::min(all.size(), children.size()), children.begin());
    return static_cast<uint32_t>(all.size());
}

void setManagedReleaseCallback(uint64_t callbackAddress)
{
    managedRelease.store(reinterpret_cast<ManagedRelease>(static_cast<uintptr_t>(callbackAddress)),
                         std::memory_order_release);
}

} // namespace crucible::ecs
