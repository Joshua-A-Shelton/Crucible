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
};

struct Registry
{
    std::mutex mutex;
    Vector<ComponentRecord> components{MemoryTag::Ecs};
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

struct World
{
    Vector<EntityRecord> records{MemoryTag::Ecs};
    Vector<uint32_t> freeIndices{MemoryTag::Ecs};
    uint32_t aliveCount = 0;
    Vector<Archetype*> archetypes{MemoryTag::Ecs};
    // Archetypes by a hash of their sorted component IDs; a list per hash, in
    // case two sets collide.
    HashMap<uint64_t, SmallVector<Archetype*, 1>> archetypeLookup{MemoryTag::Ecs};
    // Chunk blocks no longer in use, reused before allocating more.
    Vector<std::byte*> freeBlocks{MemoryTag::Ecs};
    uint32_t version = 1;
    std::atomic<uint32_t> iterating{0};
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
        archetype->columns.pushBack({component, componentInfo(component).size, 0});
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

} // namespace

// ---------------------------------------------------------------------------
// Components
// ---------------------------------------------------------------------------

ComponentId registerComponent(std::string_view name, uint32_t size, uint32_t alignment)
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
        if (std::string_view(existing.name.data(), existing.name.size()) == name)
        {
            if (existing.size != size || existing.alignment != alignment)
            {
                fatal("a component was registered again with a different size or alignment");
            }
            return static_cast<ComponentId>(id);
        }
    }
    ComponentRecord& record = components.components.emplaceBack();
    record.name = Vector<char>(MemoryTag::Ecs);
    record.name.assign(std::span<const char>(name.data(), name.size()));
    record.size = size;
    record.alignment = alignment;
    return static_cast<ComponentId>(components.components.size() - 1);
}

ComponentInfo componentInfo(ComponentId component)
{
    Registry& components = registry();
    std::lock_guard lock(components.mutex);
    const ComponentRecord& record = components.components[component];
    return {std::string_view(record.name.data(), record.name.size()), record.size, record.alignment};
}

// ---------------------------------------------------------------------------
// Worlds and entities
// ---------------------------------------------------------------------------

World* createWorld()
{
    return create<World>(MemoryTag::Ecs);
}

void destroyWorld(World* world)
{
    if (world == nullptr)
    {
        return;
    }
    for (Archetype* archetype : world->archetypes)
    {
        for (Chunk* chunk : archetype->chunks)
        {
            free(chunk->data, chunkBytes, MemoryTag::Ecs);
            destroy(MemoryTag::Ecs, chunk);
        }
        destroy(MemoryTag::Ecs, archetype);
    }
    for (std::byte* block : world->freeBlocks)
    {
        free(block, chunkBytes, MemoryTag::Ecs);
    }
    destroy(MemoryTag::Ecs, world);
}

Entity createEntity(World& world, std::span<const ComponentId> components)
{
    checkNotIterating(world);
    SmallVector<ComponentId, 16> sorted(MemoryTag::Ecs);
    sorted.assign(components);
    std::sort(sorted.begin(), sorted.end());
    sorted.resize(static_cast<size_t>(std::unique(sorted.begin(), sorted.end()) - sorted.begin()));
    Archetype* archetype = findArchetype(world, sorted.span());

    uint32_t index;
    if (!world.freeIndices.empty())
    {
        index = world.freeIndices.back();
        world.freeIndices.popBack();
    }
    else
    {
        index = static_cast<uint32_t>(world.records.size());
        world.records.emplaceBack();
    }
    EntityRecord& record = world.records[index];
    record.alive = true;
    const Entity entity{(static_cast<uint64_t>(record.generation) << 32) | index};
    addRow(world, *archetype, entity, record.chunk, record.row);
    zeroRow(*record.chunk, record.row);
    ++world.aliveCount;
    return entity;
}

void destroyEntity(World& world, Entity entity)
{
    checkNotIterating(world);
    if (liveRecord(world, entity) == nullptr)
    {
        return;
    }
    EntityRecord& record = world.records[indexOf(entity)];
    removeRow(world, *record.chunk, record.row);
    record.alive = false;
    record.chunk = nullptr;
    // The next entity at this index gets a new generation, so this one's
    // references go stale. 0 is skipped, so no live entity is ever 0.
    record.generation = record.generation + 1 == 0 ? 1 : record.generation + 1;
    world.freeIndices.pushBack(indexOf(entity));
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
    if (record == nullptr || columnIndex(*record->chunk->archetype, component) >= 0)
    {
        return;
    }
    moveEntity(world, entity, *archetypeWith(world, *record->chunk->archetype, component));
}

void removeComponent(World& world, Entity entity, ComponentId component)
{
    checkNotIterating(world);
    const EntityRecord* record = liveRecord(world, entity);
    if (record == nullptr || columnIndex(*record->chunk->archetype, component) < 0)
    {
        return;
    }
    moveEntity(world, entity, *archetypeWithout(world, *record->chunk->archetype, component));
}

bool hasComponent(const World& world, Entity entity, ComponentId component)
{
    const EntityRecord* record = liveRecord(world, entity);
    return record != nullptr && columnIndex(*record->chunk->archetype, component) >= 0;
}

void* writeComponent(World& world, Entity entity, ComponentId component)
{
    const EntityRecord* record = liveRecord(world, entity);
    if (record == nullptr)
    {
        return nullptr;
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

} // namespace crucible::ecs
