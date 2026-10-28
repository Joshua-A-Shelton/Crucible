// The ECS core (ADR 0023 and its amendment, ADR 0034).

#include <crucible/Ecs.h>
#include <crucible/Jobs.h>
#include <crucible/Memory.h>

#include <doctest/doctest.h>

#include <algorithm>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <mutex>
#include <random>
#include <unordered_map>
#include <vector>

using namespace crucible;
using namespace crucible::ecs;

namespace {

struct Position
{
    float x, y, z;
};

struct Velocity
{
    float x, y, z;
};

struct Health
{
    int32_t value;
};

struct Big
{
    uint8_t bytes[3000];
};

struct Enemy // a tag: no data
{};

struct Components
{
    ComponentId position = registerComponent<Position>("Position");
    ComponentId velocity = registerComponent<Velocity>("Velocity");
    ComponentId health = registerComponent<Health>("Health");
    ComponentId big = registerComponent<Big>("Big");
    ComponentId enemy = registerComponent<Enemy>("Enemy");
};

// A world for one test, destroyed after it.
struct WorldScope
{
    World* world = createWorld();
    ~WorldScope()
    {
        destroyWorld(world);
    }
    World& operator*()
    {
        return *world;
    }
};

} // namespace

TEST_CASE("Components register once and tags take no space")
{
    const Components c;
    CHECK(registerComponent<Position>("Position") == c.position);
    CHECK(registerComponent("Position", sizeof(Position), alignof(Position)) == c.position);
    CHECK(componentInfo(c.position).name == "Position");
    CHECK(componentInfo(c.position).size == 12);
    CHECK(componentInfo(c.enemy).size == 0);
}

TEST_CASE("Entities are created with zeroed components and go stale when destroyed")
{
    const Components c;
    WorldScope world;
    const ComponentId set[] = {c.position, c.health};
    const Entity a = createEntity(*world, set);
    CHECK(a.value != 0);
    CHECK(isAlive(*world, a));
    CHECK(read<Position>(*world, a)->x == 0.0f);
    get<Health>(*world, a)->value = 42;
    CHECK(read<Health>(*world, a)->value == 42);
    CHECK(read<Velocity>(*world, a) == nullptr); // lacks it

    destroyEntity(*world, a);
    CHECK_FALSE(isAlive(*world, a));
    CHECK(read<Health>(*world, a) == nullptr);

    // The index is reused with a new generation; the old ID stays dead.
    const Entity b = createEntity(*world, set);
    CHECK(indexOf(b) == indexOf(a));
    CHECK(generationOf(b) == generationOf(a) + 1);
    CHECK_FALSE(isAlive(*world, a));
    CHECK(isAlive(*world, b));
    destroyEntity(*world, a); // a stale ID does nothing
    CHECK(isAlive(*world, b));
    CHECK(entityCount(*world) == 1);
}

TEST_CASE("Adding and removing components keeps the other values")
{
    const Components c;
    WorldScope world;
    const Entity e = createEntity(*world, {&c.position, 1});
    *get<Position>(*world, e) = {1, 2, 3};

    addComponent(*world, e, c.velocity);
    addComponent(*world, e, c.enemy);
    CHECK(hasComponent(*world, e, c.velocity));
    CHECK(hasComponent(*world, e, c.enemy));
    CHECK(read<Velocity>(*world, e)->x == 0.0f); // added zeroed
    CHECK(read<Position>(*world, e)->z == 3.0f);
    *get<Velocity>(*world, e) = {4, 5, 6};

    removeComponent(*world, e, c.position);
    CHECK_FALSE(hasComponent(*world, e, c.position));
    CHECK(read<Velocity>(*world, e)->y == 5.0f);
    removeComponent(*world, e, c.position); // already gone: nothing
    removeComponent(*world, e, c.velocity);
    removeComponent(*world, e, c.enemy);
    CHECK(isAlive(*world, e)); // no components left, still an entity
}

TEST_CASE("Chunks hold as many entities as fit in 16 KB")
{
    const Components c;
    WorldScope world;
    // 8-byte IDs plus 12 + 12 bytes of components: 512 per chunk.
    const ComponentId pair[] = {c.position, c.velocity};
    CHECK(chunkCapacityFor(*world, pair) == 512);
    CHECK(chunkCapacityFor(*world, {}) == chunkBytes / sizeof(Entity));
    CHECK(chunkCapacityFor(*world, {&c.big, 1}) == 5); // 3008 bytes each
    const ComponentId tagged[] = {c.position, c.enemy};
    CHECK(chunkCapacityFor(*world, tagged) == chunkBytes / 20); // the tag adds nothing
}

TEST_CASE("Random structural changes keep every value in place")
{
    const Components c;
    WorldScope world;
    std::minstd_rand random(99);
    std::unordered_map<uint64_t, Position> positions;
    std::unordered_map<uint64_t, Health> healths;
    std::vector<Entity> alive;

    for (int step = 0; step < 20'000; ++step)
    {
        const int action = static_cast<int>(random() % 10);
        if (alive.empty() || action < 4)
        {
            std::vector<ComponentId> set;
            if (random() % 2)
            {
                set.push_back(c.position);
            }
            if (random() % 2)
            {
                set.push_back(c.health);
            }
            if (random() % 3 == 0)
            {
                set.push_back(c.enemy);
            }
            const Entity e = createEntity(*world, set);
            alive.push_back(e);
            if (Position* p = get<Position>(*world, e))
            {
                *p = {static_cast<float>(step), 1, 2};
                positions[e.value] = *p;
            }
            if (Health* h = get<Health>(*world, e))
            {
                h->value = step;
                healths[e.value] = *h;
            }
            continue;
        }
        const size_t pick = random() % alive.size();
        const Entity e = alive[pick];
        if (action < 6)
        {
            destroyEntity(*world, e);
            positions.erase(e.value);
            healths.erase(e.value);
            alive[pick] = alive.back();
            alive.pop_back();
        }
        else if (action < 7)
        {
            addComponent(*world, e, c.position);
            if (!positions.contains(e.value))
            {
                positions[e.value] = {0, 0, 0};
            }
        }
        else if (action < 8)
        {
            removeComponent(*world, e, c.position);
            positions.erase(e.value);
        }
        else
        {
            removeComponent(*world, e, c.health);
            healths.erase(e.value);
        }
    }

    CHECK(entityCount(*world) == alive.size());
    for (const Entity e : alive)
    {
        REQUIRE(isAlive(*world, e));
        const Position* p = read<Position>(*world, e);
        CHECK((p != nullptr) == positions.contains(e.value));
        if (p != nullptr)
        {
            CHECK(p->x == positions[e.value].x);
        }
        const Health* h = read<Health>(*world, e);
        CHECK((h != nullptr) == healths.contains(e.value));
        if (h != nullptr)
        {
            CHECK(h->value == healths[e.value].value);
        }
    }
}

TEST_CASE("Queries match required and excluded components including later archetypes")
{
    const Components c;
    WorldScope world;
    const ComponentId moving[] = {c.position, c.velocity};
    const ComponentId movingEnemy[] = {c.position, c.velocity, c.enemy};
    for (int i = 0; i < 1000; ++i)
    {
        createEntity(*world, moving);
    }
    const ComponentId excluded[] = {c.enemy};
    Query* query = createQuery(*world, {moving, excluded});

    // An archetype that appears after the query was made.
    for (int i = 0; i < 300; ++i)
    {
        createEntity(*world, movingEnemy);
        const ComponentId withHealth[] = {c.position, c.velocity, c.health};
        createEntity(*world, withHealth);
    }

    struct Count
    {
        uint32_t entities = 0;
        uint32_t chunks = 0;
    } count;
    forEachChunk(
        *query,
        [](const ChunkView& view, void* data) {
            auto* counted = static_cast<Count*>(data);
            counted->entities += entityCountOf(view);
            ++counted->chunks;
            CHECK((readColumn(view, componentIdOf<Health>) == nullptr || entityCountOf(view) > 0));
        },
        &count);
    CHECK(count.entities == 1300); // 1000 + the 300 with Health, not the enemies
    CHECK(count.chunks == 3);      // 1000 / 512 per chunk, then 300
    destroyQuery(query);
}

TEST_CASE("Writes stamp change versions and reads don't")
{
    const Components c;
    WorldScope world;
    const ComponentId set[] = {c.position, c.velocity};
    const Entity e = createEntity(*world, set);
    Query* query = createQuery(*world, {set, {}});

    const uint32_t lastRun = advanceVersion(*world);
    advanceVersion(*world);
    struct Seen
    {
        uint32_t since;
        bool position = false;
        bool velocity = false;
    } seen{lastRun};
    auto check = [](const ChunkView& view, void* data) {
        auto* s = static_cast<Seen*>(data);
        s->position = changedSince(view, componentIdOf<Position>, s->since);
        s->velocity = changedSince(view, componentIdOf<Velocity>, s->since);
    };

    forEachChunk(*query, check, &seen);
    CHECK_FALSE(seen.position); // nothing written since lastRun
    CHECK_FALSE(seen.velocity);

    (void)read<Position>(*world, e);
    get<Velocity>(*world, e)->x = 1;
    forEachChunk(*query, check, &seen);
    CHECK_FALSE(seen.position); // read only
    CHECK(seen.velocity);       // asked to write

    // A structural change marks every column.
    seen.since = advanceVersion(*world);
    advanceVersion(*world);
    createEntity(*world, set);
    forEachChunk(*query, check, &seen);
    CHECK(seen.position);
    destroyQuery(query);
}

TEST_CASE("Parallel chunk iteration updates every entity once")
{
    const Components c;
    REQUIRE(jobs::initialize(3));
    {
        WorldScope world;
        const ComponentId set[] = {c.position, c.velocity};
        for (int i = 0; i < 10'000; ++i)
        {
            const Entity e = createEntity(*world, set);
            *get<Velocity>(*world, e) = {1, 2, 3};
        }
        Query* query = createQuery(*world, {set, {}});
        parallelForEachChunk(
            *query,
            [](const ChunkView& view, void*) {
                std::span<Position> positions = writeColumn<Position>(view);
                std::span<const Velocity> velocities = readColumn<Velocity>(view);
                for (size_t i = 0; i < positions.size(); ++i)
                {
                    positions[i].x += velocities[i].x;
                    positions[i].y += velocities[i].y;
                }
            },
            nullptr);

        uint32_t wrong = 0;
        forEachChunk(
            *query,
            [](const ChunkView& view, void* data) {
                for (const Position& p : readColumn<Position>(view))
                {
                    *static_cast<uint32_t*>(data) += (p.x != 1.0f || p.y != 2.0f) ? 1 : 0;
                }
            },
            &wrong);
        CHECK(wrong == 0);
        destroyQuery(query);
    }
    jobs::shutdown();
}

TEST_CASE("Destroying a world returns all its memory")
{
    const Components c;
    // The registry's list of live worlds keeps its capacity for the process,
    // like the component registry: grow it before measuring.
    destroyWorld(createWorld());
    const MemoryUsage before = memoryUsage(MemoryTag::Ecs);
    {
        WorldScope world;
        const ComponentId set[] = {c.position, c.big};
        for (int i = 0; i < 1000; ++i)
        {
            createEntity(*world, set);
        }
        CHECK(memoryUsage(MemoryTag::Ecs).bytes > before.bytes + 200 * chunkBytes);
    }
    CHECK(memoryUsage(MemoryTag::Ecs).bytes == before.bytes);
}

TEST_SUITE("benchmark" * doctest::skip())
{
    TEST_CASE("ECS: create 100k entities and integrate them")
    {
        const Components c;
        REQUIRE(jobs::initialize(0));
        {
            WorldScope world;
            const ComponentId set[] = {c.position, c.velocity};
            auto start = std::chrono::steady_clock::now();
            for (int i = 0; i < 100'000; ++i)
            {
                createEntity(*world, set);
            }
            const double create =
                std::chrono::duration<double, std::nano>(std::chrono::steady_clock::now() - start).count() / 1e5;

            Query* query = createQuery(*world, {set, {}});
            auto integrate = [](const ChunkView& view, void*) {
                std::span<Position> positions = writeColumn<Position>(view);
                std::span<const Velocity> velocities = readColumn<Velocity>(view);
                for (size_t i = 0; i < positions.size(); ++i)
                {
                    positions[i].x += velocities[i].x * 0.016f;
                    positions[i].y += velocities[i].y * 0.016f;
                    positions[i].z += velocities[i].z * 0.016f;
                }
            };
            auto time = [&](auto&& run) {
                double best = 1e30;
                for (int r = 0; r < 20; ++r)
                {
                    const auto t0 = std::chrono::steady_clock::now();
                    run();
                    best = std::min(
                        best,
                        std::chrono::duration<double, std::nano>(std::chrono::steady_clock::now() - t0).count() / 1e5);
                }
                return best;
            };
            const double sequential = time([&] { forEachChunk(*query, integrate, nullptr); });
            const double parallel = time([&] { parallelForEachChunk(*query, integrate, nullptr); });
            MESSAGE("create " << create << " ns/entity; integrate: sequential " << sequential << ", parallel "
                              << parallel << " ns/entity (" << jobs::workerCount() << " workers)");
            destroyQuery(query);
        }
        jobs::shutdown();
    }
}

// ---------------------------------------------------------------------------
// Step 6b: sparse storage, the hierarchy, command buffers
// ---------------------------------------------------------------------------

namespace {

struct Damaged // toggled constantly: sparse
{
    int32_t amount;
};

struct Selected // a sparse tag
{};

} // namespace

TEST_CASE("Sparse components come and go without moving the entity")
{
    const Components c;
    const ComponentId damaged = registerComponent<Damaged>("Damaged", ComponentStorage::Sparse);
    const ComponentId selected = registerComponent<Selected>("Selected", ComponentStorage::Sparse);
    CHECK(isSparse(damaged));
    CHECK_FALSE(isSparse(c.position));
    CHECK(componentInfo(damaged).name == "Damaged");

    WorldScope world;
    const Entity e = createEntity(*world, {&c.position, 1});
    const Position* before = read<Position>(*world, e);
    addComponent(*world, e, damaged);
    addComponent(*world, e, selected);
    get<Damaged>(*world, e)->amount = 7;
    CHECK(read<Position>(*world, e) == before); // same chunk, same row
    CHECK(hasComponent(*world, e, damaged));
    CHECK(hasComponent(*world, e, selected));
    CHECK(read<Selected>(*world, e) == nullptr); // a tag has no value
    CHECK(read<Damaged>(*world, e)->amount == 7);

    removeComponent(*world, e, damaged);
    CHECK_FALSE(hasComponent(*world, e, damaged));
    CHECK(read<Position>(*world, e) == before);

    // Created with a sparse component; destroyed entities leave the set.
    const ComponentId both[] = {c.position, damaged};
    const Entity f = createEntity(*world, both);
    CHECK(hasComponent(*world, f, damaged));
    destroyEntity(*world, e);
    int visited = 0;
    forEachSparse(*world, selected, [](Entity, void*, void* data) { ++*static_cast<int*>(data); }, &visited);
    CHECK(visited == 0); // e had the only Selected
}

TEST_CASE("Random sparse adds and removes match a reference")
{
    const ComponentId damaged = registerComponent<Damaged>("Damaged", ComponentStorage::Sparse);
    WorldScope world;
    std::minstd_rand random(5);
    std::vector<Entity> entities;
    for (int i = 0; i < 500; ++i)
    {
        entities.push_back(createEntity(*world));
    }
    std::unordered_map<uint64_t, int32_t> reference;
    for (int step = 0; step < 20'000; ++step)
    {
        const Entity e = entities[random() % entities.size()];
        if (random() % 2)
        {
            addComponent(*world, e, damaged);
            get<Damaged>(*world, e)->amount = step;
            reference[e.value] = step;
        }
        else
        {
            removeComponent(*world, e, damaged);
            reference.erase(e.value);
        }
    }
    struct Check
    {
        std::unordered_map<uint64_t, int32_t>* reference;
        int visited = 0;
        int wrong = 0;
    } check{&reference};
    forEachSparse(
        *world, damaged,
        [](Entity e, void* value, void* data) {
            auto* state = static_cast<Check*>(data);
            ++state->visited;
            auto found = state->reference->find(e.value);
            state->wrong +=
                (found == state->reference->end() || found->second != static_cast<Damaged*>(value)->amount) ? 1 : 0;
        },
        &check);
    CHECK(check.visited == static_cast<int>(reference.size()));
    CHECK(check.wrong == 0);
}

TEST_CASE("Children keep their order and can be moved within it")
{
    WorldScope world;
    const Entity parent = createEntity(*world);
    Entity kids[4];
    for (Entity& kid : kids)
    {
        kid = createEntity(*world);
        REQUIRE(setParent(*world, kid, parent));
    }
    CHECK(parentOf(*world, kids[2]) == parent);
    CHECK(hasComponent(*world, kids[2], parentComponent()));
    REQUIRE(childrenOf(*world, parent).size() == 4);
    CHECK(childrenOf(*world, parent)[3] == kids[3]);

    // Same parent, new position: kids[3] moves to the front.
    REQUIRE(setParent(*world, kids[3], parent, 0));
    std::span<const Entity> order = childrenOf(*world, parent);
    CHECK(order[0] == kids[3]);
    CHECK(order[1] == kids[0]);
    CHECK(order[3] == kids[2]);

    // Back to the root.
    REQUIRE(setParent(*world, kids[0], nullEntity));
    CHECK(parentOf(*world, kids[0]) == nullEntity);
    CHECK_FALSE(hasComponent(*world, kids[0], parentComponent()));
    CHECK(childrenOf(*world, parent).size() == 3);
}

TEST_CASE("Moves that would make a cycle are refused")
{
    WorldScope world;
    const Entity a = createEntity(*world);
    const Entity b = createEntity(*world);
    const Entity c = createEntity(*world);
    REQUIRE(setParent(*world, b, a));
    REQUIRE(setParent(*world, c, b));
    CHECK(setParent(*world, a, c).status() == Status::InvalidData); // a under its grandchild
    CHECK(setParent(*world, a, a).status() == Status::InvalidData);
    const Entity dead = createEntity(*world);
    destroyEntity(*world, dead);
    CHECK(setParent(*world, a, dead).status() == Status::InvalidData);
    CHECK(parentOf(*world, a) == nullEntity); // unchanged
}

TEST_CASE("Destroying an entity destroys its descendants and leaves its parent's list")
{
    WorldScope world;
    const Entity root = createEntity(*world);
    const Entity middle = createEntity(*world);
    const Entity sibling = createEntity(*world);
    REQUIRE(setParent(*world, middle, root));
    REQUIRE(setParent(*world, sibling, root));
    // A deep chain under middle.
    Entity chain = middle;
    std::vector<Entity> descendants;
    for (int i = 0; i < 200; ++i)
    {
        const Entity next = createEntity(*world);
        REQUIRE(setParent(*world, next, chain));
        descendants.push_back(next);
        chain = next;
    }

    destroyEntity(*world, middle);
    for (const Entity d : descendants)
    {
        CHECK_FALSE(isAlive(*world, d));
    }
    REQUIRE(childrenOf(*world, root).size() == 1);
    CHECK(childrenOf(*world, root)[0] == sibling);
    CHECK(entityCount(*world) == 2);

    destroyEntity(*world, root);
    CHECK(entityCount(*world) == 0);
    CHECK(childrenOf(*world, root).empty());
}

TEST_CASE("Commands recorded during iteration apply at the sync point")
{
    const Components c;
    WorldScope world;
    const ComponentId set[] = {c.position, c.health};
    for (int i = 0; i < 10; ++i)
    {
        const Entity e = createEntity(*world, set);
        get<Health>(*world, e)->value = i;
    }
    Query* query = createQuery(*world, {set, {}});
    CommandBuffer* buffer = createCommandBuffer(*world);

    struct State
    {
        CommandBuffer* buffer;
        Entity spawned = nullEntity;
    } state{buffer};
    forEachChunk(
        *query,
        [](const ChunkView& view, void* data) {
            auto* s = static_cast<State*>(data);
            std::span<const Health> healths = readColumn<Health>(view);
            for (uint32_t i = 0; i < entityCountOf(view); ++i)
            {
                const Entity e = entitiesOf(view)[i];
                if (healths[i].value % 2 == 1)
                {
                    recordDestroy(*s->buffer, e);
                }
                else if (healths[i].value == 4)
                {
                    // A projectile, its velocity, and a parent: all before it exists.
                    const ComponentId projectile[] = {componentIdOf<Position>};
                    s->spawned = recordCreate(*s->buffer, projectile);
                    recordSet(*s->buffer, s->spawned, Velocity{1, 2, 3});
                    recordSetParent(*s->buffer, s->spawned, e);
                    recordRemove(*s->buffer, e, componentIdOf<Position>);
                }
            }
        },
        &state);

    CHECK(entityCount(*world) == 10);            // nothing applied yet
    CHECK_FALSE(isAlive(*world, state.spawned)); // reserved, not created
    apply(*buffer);
    CHECK(entityCount(*world) == 6); // 5 destroyed, 1 created
    REQUIRE(isAlive(*world, state.spawned));
    CHECK(read<Velocity>(*world, state.spawned)->z == 3.0f); // Set added it
    const Entity parent = parentOf(*world, state.spawned);
    CHECK(read<Health>(*world, parent)->value == 4);
    CHECK_FALSE(hasComponent(*world, parent, c.position));

    // Commands on entities that died meanwhile are ignored.
    recordAdd(*buffer, state.spawned, c.velocity);
    destroyEntity(*world, state.spawned);
    apply(*buffer);
    CHECK(entityCount(*world) == 5);

    destroyCommandBuffer(buffer);
    destroyQuery(query);
}

TEST_CASE("A cleared buffer's reserved entities never come alive")
{
    WorldScope world;
    CommandBuffer* buffer = createCommandBuffer(*world);
    const Entity reserved = recordCreate(*buffer);
    clear(*buffer);
    apply(*buffer); // nothing left
    // The released index is reused, with a newer generation.
    const Entity reused = createEntity(*world);
    CHECK(indexOf(reused) == indexOf(reserved));
    CHECK_FALSE(isAlive(*world, reserved));
    CHECK(isAlive(*world, reused));
    destroyCommandBuffer(buffer);
}

TEST_CASE("Recorded creations reuse destroyed entities' indices")
{
    // Spawning only through command buffers must not grow the index space
    // forever (ADR 0023's amendment).
    WorldScope world;
    CommandBuffer* buffer = createCommandBuffer(*world);
    std::vector<Entity> first;
    for (int i = 0; i < 1000; ++i)
    {
        first.push_back(recordCreate(*buffer));
    }
    apply(*buffer);
    for (const Entity e : first)
    {
        recordDestroy(*buffer, e);
    }
    apply(*buffer);

    std::vector<Entity> second;
    for (int i = 0; i < 1000; ++i)
    {
        second.push_back(recordCreate(*buffer));
    }
    for (const Entity e : second)
    {
        CHECK(indexOf(e) < 1000);
        CHECK(generationOf(e) == 2);
    }
    apply(*buffer);
    CHECK(entityCount(*world) == 1000);
    CHECK(std::ranges::all_of(second, [&](Entity e) { return isAlive(*world, e); }));
    CHECK(std::ranges::none_of(first, [&](Entity e) { return isAlive(*world, e); }));
    // The free list is used up, so the next index is fresh.
    CHECK(indexOf(createEntity(*world)) == 1000);
    destroyCommandBuffer(buffer);
}

TEST_CASE("Parallel recording hands out each free index once")
{
    const Components c;
    REQUIRE(jobs::initialize(3));
    {
        WorldScope world;
        const ComponentId set[] = {c.health};
        std::vector<Entity> entities;
        for (int i = 0; i < 20'000; ++i)
        {
            entities.push_back(createEntity(*world, set));
            get<Health>(*world, entities.back())->value = i;
        }
        for (int i = 0; i < 20'000; i += 2)
        {
            destroyEntity(*world, entities[static_cast<size_t>(i)]);
        }

        // Every survivor records one creation, from every worker at once:
        // 10,000 creations for 10,000 free indices.
        struct State
        {
            CommandBuffer* buffer;
            std::mutex lock;
            std::vector<Entity> created;
        } state{createCommandBuffer(*world), {}, {}};
        Query* query = createQuery(*world, {set, {}});
        parallelForEachChunk(
            *query,
            [](const ChunkView& view, void* data) {
                auto& s = *static_cast<State*>(data);
                std::vector<Entity> mine;
                for (uint32_t i = 0; i < entityCountOf(view); ++i)
                {
                    mine.push_back(recordCreate(*s.buffer));
                }
                std::lock_guard guard(s.lock);
                s.created.insert(s.created.end(), mine.begin(), mine.end());
            },
            &state);
        apply(*state.buffer);

        std::vector<uint32_t> indices;
        for (const Entity e : state.created)
        {
            indices.push_back(indexOf(e));
            CHECK(isAlive(*world, e));
        }
        std::ranges::sort(indices);
        CHECK(indices.size() == 10'000);
        CHECK(std::ranges::adjacent_find(indices) == indices.end()); // no index twice
        CHECK(indices.back() < 20'000);                              // all reused
        CHECK(entityCount(*world) == 20'000);
        destroyCommandBuffer(state.buffer);
        destroyQuery(query);
    }
    jobs::shutdown();
}

TEST_CASE("Parallel chunk jobs can record into one buffer")
{
    const Components c;
    REQUIRE(jobs::initialize(3));
    {
        WorldScope world;
        const ComponentId set[] = {c.position, c.health};
        for (int i = 0; i < 20'000; ++i)
        {
            const Entity e = createEntity(*world, set);
            get<Health>(*world, e)->value = i;
        }
        Query* query = createQuery(*world, {set, {}});
        CommandBuffer* buffer = createCommandBuffer(*world);
        parallelForEachChunk(
            *query,
            [](const ChunkView& view, void* data) {
                auto* b = static_cast<CommandBuffer*>(data);
                std::span<const Health> healths = readColumn<Health>(view);
                for (uint32_t i = 0; i < entityCountOf(view); ++i)
                {
                    if (healths[i].value % 4 == 0)
                    {
                        recordDestroy(*b, entitiesOf(view)[i]);
                        recordCreate(*b, {&componentIdOf<Health>, 1});
                    }
                }
            },
            buffer);
        apply(*buffer);
        CHECK(entityCount(*world) == 20'000); // 5000 destroyed, 5000 created
        destroyCommandBuffer(buffer);
        destroyQuery(query);
    }
    jobs::shutdown();
}

namespace {

// One component's layout before and after a hot reload edit (ADR 0035):
// fields reordered, `health` removed, `armor` added.
struct StatsBefore
{
    int32_t health;
    float speed;
    int64_t score;
};

struct StatsAfter
{
    int64_t score;
    float speed;
    int32_t armor;
};

// The copies C# computes for those two: score and speed, where they moved.
const uint32_t statsCopies[] = {
    offsetof(StatsBefore, score), offsetof(StatsAfter, score), sizeof(int64_t),
    offsetof(StatsBefore, speed), offsetof(StatsAfter, speed), sizeof(float),
};

} // namespace

TEST_CASE("A changed layout moves to a new ID in every world field by field")
{
    const Components c;
    const ComponentId before = registerComponent("MigratedStats", sizeof(StatsBefore), alignof(StatsBefore));
    WorldScope first;
    WorldScope second;
    const ComponentId set[] = {c.position, before};
    std::vector<Entity> entities;
    for (World* world : {first.world, second.world})
    {
        for (int i = 0; i < 1000; ++i)
        {
            const Entity e = createEntity(*world, set);
            *static_cast<StatsBefore*>(writeComponent(*world, e, before)) = {i, 0.5f * i, 1000ll * i};
            get<Position>(*world, e)->x = static_cast<float>(i);
            entities.push_back(e);
        }
    }

    retireComponent(before);
    const ComponentId after = registerComponent("MigratedStats", sizeof(StatsAfter), alignof(StatsAfter));
    CHECK(after != before);
    migrateComponent(before, after, statsCopies);

    for (size_t n = 0; n < entities.size(); ++n)
    {
        World& world = *(n < 1000 ? first.world : second.world);
        const Entity e = entities[n];
        const auto i = static_cast<int>(n % 1000);
        CHECK_FALSE(hasComponent(world, e, before));
        const auto* stats = static_cast<const StatsAfter*>(readComponent(world, e, after));
        REQUIRE(stats != nullptr);
        CHECK(stats->score == 1000ll * i);
        CHECK(stats->speed == 0.5f * i);
        CHECK(stats->armor == 0);                                    // new: zeroed
        CHECK(read<Position>(world, e)->x == static_cast<float>(i)); // untouched
    }
    CHECK(entityCount(*first.world) == 1000);
    CHECK(registerComponent("MigratedStats", sizeof(StatsAfter), alignof(StatsAfter)) == after);
}

TEST_CASE("Sparse components migrate and removed types leave every entity")
{
    const Components c;
    const ComponentId before =
        registerComponent("MigratedSparseStats", sizeof(StatsBefore), alignof(StatsBefore), ComponentStorage::Sparse);
    const ComponentId gone = registerComponent("RemovedType", sizeof(Health), alignof(Health));
    const ComponentId goneSparse = registerComponent("RemovedSparseType", 0, 1, ComponentStorage::Sparse);
    WorldScope world;
    const ComponentId set[] = {c.position, before, gone, goneSparse};
    const Entity a = createEntity(*world, set);
    const Entity b = createEntity(*world, set);
    *static_cast<StatsBefore*>(writeComponent(*world, a, before)) = {1, 2.0f, 3};
    *static_cast<StatsBefore*>(writeComponent(*world, b, before)) = {4, 5.0f, 6};

    retireComponent(before);
    const ComponentId after =
        registerComponent("MigratedSparseStats", sizeof(StatsAfter), alignof(StatsAfter), ComponentStorage::Sparse);
    CHECK(isSparse(after));
    migrateComponent(before, after, statsCopies);
    CHECK_FALSE(hasComponent(*world, a, before));
    CHECK(static_cast<const StatsAfter*>(readComponent(*world, b, after))->score == 6);
    CHECK(static_cast<const StatsAfter*>(readComponent(*world, a, after))->speed == 2.0f);

    retireComponent(gone);
    removeComponentEverywhere(gone);
    retireComponent(goneSparse);
    removeComponentEverywhere(goneSparse);
    CHECK_FALSE(hasComponent(*world, a, gone));
    CHECK_FALSE(hasComponent(*world, b, goneSparse));
    CHECK(hasComponent(*world, b, c.position));
    CHECK(registerComponent("RemovedType", 8, 8) != gone); // the name is free for a new layout
}
