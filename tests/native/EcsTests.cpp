// The ECS core (ADR 0023 and its amendment, ADR 0034).

#include <crucible/Ecs.h>
#include <crucible/Jobs.h>
#include <crucible/Memory.h>

#include <doctest/doctest.h>

#include <algorithm>
#include <chrono>
#include <cstdint>
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
