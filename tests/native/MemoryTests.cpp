// The allocator and containers (ADR 0032).

#include <crucible/HashMap.h>
#include <crucible/Memory.h>
#include <crucible/SmallVector.h>
#include <crucible/Vector.h>

#include <doctest/doctest.h>

#if defined(_WIN32)
#include <malloc.h>
#endif

#include <cstdint>
#include <cstdlib>
#include <random>
#include <unordered_map>

using namespace crucible;

namespace {

// A move-only element that counts how many are alive, so leaks and double
// destruction show up.
struct Tracked
{
    static inline int alive = 0;

    explicit Tracked(int value = 0)
        : value(value)
    {
        ++alive;
    }
    Tracked(Tracked&& other) noexcept
        : value(other.value)
    {
        other.value = -1;
        ++alive;
    }
    Tracked& operator=(Tracked&& other) noexcept
    {
        value = other.value;
        other.value = -1;
        return *this;
    }
    Tracked(const Tracked&) = delete;
    Tracked& operator=(const Tracked&) = delete;
    ~Tracked()
    {
        --alive;
    }

    int value;
};

void* alignedAllocate(size_t size, size_t alignment)
{
#if defined(_WIN32)
    return _aligned_malloc(size, alignment);
#else
    void* pointer = nullptr;
    return posix_memalign(&pointer, alignment < sizeof(void*) ? sizeof(void*) : alignment, size) == 0 ? pointer
                                                                                                      : nullptr;
#endif
}

void alignedFree(void* pointer)
{
#if defined(_WIN32)
    _aligned_free(pointer);
#else
    std::free(pointer);
#endif
}

struct CountingHooks
{
    int allocations = 0;
    int frees = 0;
    MemoryTag lastTag = MemoryTag::General;
};

} // namespace

TEST_CASE("Allocations are counted per tag and freed")
{
    const MemoryUsage before = memoryUsage(MemoryTag::Jobs);
    void* block = allocate(1000, 64, MemoryTag::Jobs);
    REQUIRE(block != nullptr);
    CHECK(reinterpret_cast<uintptr_t>(block) % 64 == 0);
    CHECK(memoryUsage(MemoryTag::Jobs).bytes == before.bytes + 1000);
    CHECK(memoryUsage(MemoryTag::Jobs).allocations == before.allocations + 1);
    free(block, 1000, MemoryTag::Jobs);
    CHECK(memoryUsage(MemoryTag::Jobs).bytes == before.bytes);
    CHECK(memoryUsage(MemoryTag::Jobs).allocations == before.allocations);

    CHECK(allocate(0, 8, MemoryTag::Jobs) == nullptr);
    free(nullptr, 0, MemoryTag::Jobs); // ignored
}

TEST_CASE("tryAllocate reports failure instead of terminating")
{
    // No machine has this much memory.
    CHECK(tryAllocate(size_t{1} << 62, 16, MemoryTag::General) == nullptr);
}

TEST_CASE("Replaced hooks receive every allocation with its tag")
{
    static CountingHooks counts;
    counts = {};
    AllocatorHooks hooks;
    hooks.allocate = [](size_t size, size_t alignment, MemoryTag tag, void* userData) -> void* {
        auto* data = static_cast<CountingHooks*>(userData);
        ++data->allocations;
        data->lastTag = tag;
        return alignedAllocate(size, alignment);
    };
    // free isn't told the alignment (nor is Slag's), so hooks use an
    // allocator that doesn't need it.
    hooks.free = [](void* pointer, size_t, MemoryTag, void* userData) {
        ++static_cast<CountingHooks*>(userData)->frees;
        alignedFree(pointer);
    };
    hooks.userData = &counts;
    setAllocatorHooks(hooks);
    {
        Vector<int> values(MemoryTag::Ecs);
        for (int i = 0; i < 100; ++i)
        {
            values.pushBack(i);
        }
        CHECK(counts.lastTag == MemoryTag::Ecs);
    }
    setAllocatorHooks({});
    CHECK(counts.allocations > 0);
    CHECK(counts.allocations == counts.frees);
}

TEST_CASE("create and destroy construct and free one object")
{
    const MemoryUsage before = memoryUsage(MemoryTag::General);
    Tracked* object = create<Tracked>(MemoryTag::General, 7);
    CHECK(object->value == 7);
    CHECK(Tracked::alive == 1);
    destroy(MemoryTag::General, object);
    CHECK(Tracked::alive == 0);
    CHECK(memoryUsage(MemoryTag::General).bytes == before.bytes);
}

TEST_CASE("Vector grows and keeps and destroys its elements")
{
    {
        Vector<Tracked> values(MemoryTag::Ecs);
        for (int i = 0; i < 1000; ++i)
        {
            values.emplaceBack(i);
        }
        CHECK(values.size() == 1000);
        CHECK(Tracked::alive == 1000);
        for (int i = 0; i < 1000; ++i)
        {
            CHECK(values[i].value == i);
        }

        values.removeSwap(10); // the last element takes its place
        CHECK(values.size() == 999);
        CHECK(values[10].value == 999);
        values.popBack();
        CHECK(values.back().value == 997); // 999 moved to slot 10, so 998 was last

        const size_t capacity = values.capacity();
        values.clear();
        CHECK(values.empty());
        CHECK(values.capacity() == capacity); // refilling won't allocate
        CHECK(Tracked::alive == 0);
    }
    CHECK(Tracked::alive == 0);
}

TEST_CASE("pushBack of one of its own elements survives the growth it causes")
{
    Vector<int> values;
    values.pushBack(42);
    for (int i = 0; i < 100; ++i)
    {
        values.pushBack(values[0]); // grows at 4, 8, 16, ...
    }
    for (int value : values)
    {
        CHECK(value == 42);
    }
}

TEST_CASE("Vector moves and resizes and copies only when asked")
{
    Vector<int> values(MemoryTag::Ecs);
    values.resize(5);
    CHECK(values.size() == 5);
    CHECK(values[4] == 0); // value-initialized
    values[2] = 7;

    Vector<int> moved = std::move(values);
    CHECK(moved.size() == 5);
    CHECK(moved[2] == 7);
    CHECK(values.empty());

    Vector<int> copied = moved.copy();
    copied[2] = 8;
    CHECK(moved[2] == 7);
    CHECK(copied.tag() == MemoryTag::Ecs);

    moved.resize(2);
    CHECK(moved.span().size() == 2);
}

TEST_CASE("SmallVector stays inline until it outgrows its capacity")
{
    const MemoryUsage before = memoryUsage(MemoryTag::Platform);
    SmallVector<int, 4> values(MemoryTag::Platform);
    for (int i = 0; i < 4; ++i)
    {
        values.pushBack(i);
    }
    CHECK(memoryUsage(MemoryTag::Platform).allocations == before.allocations); // no heap yet
    values.pushBack(4);
    CHECK(memoryUsage(MemoryTag::Platform).allocations == before.allocations + 1);
    for (int i = 0; i < 5; ++i)
    {
        CHECK(values[i] == i);
    }
}

TEST_CASE("Moving an inline SmallVector moves its elements")
{
    {
        SmallVector<Tracked, 4> source;
        source.emplaceBack(1);
        source.emplaceBack(2);
        SmallVector<Tracked, 4> target = std::move(source);
        CHECK(source.empty());
        REQUIRE(target.size() == 2);
        CHECK(target[0].value == 1);
        CHECK(target[1].value == 2);
        CHECK(Tracked::alive == 2);
    }
    CHECK(Tracked::alive == 0);
}

TEST_CASE("HashMap matches std::unordered_map through inserts and erases")
{
    HashMap<uint32_t, int> map(MemoryTag::Ecs);
    std::unordered_map<uint32_t, int> reference;
    std::minstd_rand random(7);
    // Small keys collide often in a small table; large ones exercise the hash.
    for (int step = 0; step < 20000; ++step)
    {
        const uint32_t key = step % 3 == 0 ? random() % 64 : random();
        if (random() % 4 == 0)
        {
            CHECK(map.erase(key) == (reference.erase(key) == 1));
        }
        else
        {
            map.insertOrAssign(key, step);
            reference[key] = step;
        }
    }

    CHECK(map.size() == reference.size());
    for (const auto& [key, value] : reference)
    {
        const int* found = map.find(key);
        REQUIRE(found != nullptr);
        CHECK(*found == value);
    }
    size_t visited = 0;
    for (const auto& entry : map)
    {
        CHECK(reference.at(entry.key) == entry.value);
        ++visited;
    }
    CHECK(visited == reference.size());
}

TEST_CASE("HashMap tryEmplace keeps the existing value and clear keeps the capacity")
{
    HashMap<int*, Tracked> map;
    int a = 0;
    int b = 0;
    CHECK(map.tryEmplace(&a, 1).second);
    CHECK_FALSE(map.tryEmplace(&a, 2).second);
    CHECK(map.find(&a)->value == 1);
    CHECK(map.contains(&a));
    CHECK_FALSE(map.contains(&b));

    HashMap<int*, Tracked> moved = std::move(map);
    CHECK(moved.size() == 1);
    CHECK(map.empty());
    CHECK(Tracked::alive == 1);

    const size_t capacity = moved.capacity();
    moved.clear();
    CHECK(moved.capacity() == capacity);
    CHECK(Tracked::alive == 0);
}
