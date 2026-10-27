#pragma once

// A hash map on Crucible's allocator (ADR 0032): open addressing with linear
// probing, a power-of-two capacity at most 7/8 full, and backward-shift
// deletion, so there are no tombstones and lookups don't slow down as entries
// come and go. Move-only; copy() when a copy is meant. Names are placeholders
// (docs/naming.md).
//
//     HashMap<uint32_t, Archetype*> archetypes(MemoryTag::Ecs);
//     archetypes.insertOrAssign(id, archetype);
//     if (Archetype** found = archetypes.find(id)) { ... }

#include <crucible/Memory.h>

#include <cstddef>
#include <cstdint>
#include <cstring>
#include <new>
#include <type_traits>
#include <utility>

namespace crucible {

// Hashes integers, enums, and pointers. Specialize it for other key types.
template <class K>
struct Hash
{
    uint64_t operator()(const K& key) const
    {
        static_assert(std::is_integral_v<K> || std::is_enum_v<K> || std::is_pointer_v<K>,
                      "specialize crucible::Hash for this key type");
        uint64_t x;
        if constexpr (std::is_pointer_v<K>)
        {
            x = static_cast<uint64_t>(reinterpret_cast<uintptr_t>(key));
        }
        else
        {
            x = static_cast<uint64_t>(key);
        }
        // splitmix64's finalizer: every input bit affects every output bit,
        // so sequential IDs spread over the table.
        x ^= x >> 30;
        x *= 0xbf58476d1ce4e5b9ull;
        x ^= x >> 27;
        x *= 0x94d049bb133111ebull;
        x ^= x >> 31;
        return x;
    }
};

template <class K, class V, class Hasher = Hash<K>>
class HashMap
{
public:
    struct Entry
    {
        K key;
        V value;
    };

    explicit HashMap(MemoryTag tag = MemoryTag::General)
        : memoryTag(tag)
    {}

    HashMap(const HashMap&) = delete;
    HashMap& operator=(const HashMap&) = delete;

    HashMap(HashMap&& other) noexcept
        : entries(other.entries)
        , occupied(other.occupied)
        , capacityCount(other.capacityCount)
        , count(other.count)
        , memoryTag(other.memoryTag)
    {
        other.forget();
    }

    HashMap& operator=(HashMap&& other) noexcept
    {
        if (this != &other)
        {
            release();
            entries = other.entries;
            occupied = other.occupied;
            capacityCount = other.capacityCount;
            count = other.count;
            memoryTag = other.memoryTag;
            other.forget();
        }
        return *this;
    }

    ~HashMap()
    {
        release();
    }

    // An explicit copy, with the same tag.
    HashMap copy() const
    {
        HashMap result(memoryTag);
        result.reserve(count);
        for (const Entry& entry : *this)
        {
            result.tryEmplace(entry.key, entry.value);
        }
        return result;
    }

    V* find(const K& key)
    {
        const size_t slot = findSlot(key);
        return slot == notFound ? nullptr : &entries[slot].value;
    }

    const V* find(const K& key) const
    {
        return const_cast<HashMap*>(this)->find(key);
    }

    bool contains(const K& key) const
    {
        return find(key) != nullptr;
    }

    // Inserts key with a value built from `args` if the key is absent.
    // Returns the value and whether it was inserted. `args` must not refer
    // into this map, which may grow first.
    template <class... Args>
    std::pair<V*, bool> tryEmplace(const K& key, Args&&... args)
    {
        if (V* existing = find(key))
        {
            return {existing, false};
        }
        if ((count + 1) * 8 > capacityCount * 7)
        {
            rehash(capacityCount == 0 ? 8 : capacityCount * 2);
        }
        size_t slot = home(key);
        while (occupied[slot])
        {
            slot = (slot + 1) & (capacityCount - 1);
        }
        new (&entries[slot]) Entry{key, V(std::forward<Args>(args)...)};
        occupied[slot] = 1;
        ++count;
        return {&entries[slot].value, true};
    }

    V& insertOrAssign(const K& key, V value)
    {
        auto [found, inserted] = tryEmplace(key, std::move(value));
        if (!inserted)
        {
            *found = std::move(value);
        }
        return *found;
    }

    // Returns whether the key was there.
    bool erase(const K& key)
    {
        size_t hole = findSlot(key);
        if (hole == notFound)
        {
            return false;
        }
        entries[hole].~Entry();
        occupied[hole] = 0;
        --count;

        // Backward shift: later entries of the same probe run move into the
        // hole, unless that would put them before their home slot.
        const size_t mask = capacityCount - 1;
        for (size_t next = (hole + 1) & mask; occupied[next]; next = (next + 1) & mask)
        {
            const size_t desired = home(entries[next].key);
            // Movable if `desired` is not cyclically within (hole, next].
            const bool movable =
                hole <= next ? (desired <= hole || desired > next) : (desired <= hole && desired > next);
            if (movable)
            {
                new (&entries[hole]) Entry(std::move(entries[next]));
                entries[next].~Entry();
                occupied[hole] = 1;
                occupied[next] = 0;
                hole = next;
            }
        }
        return true;
    }

    void reserve(size_t entryCount)
    {
        size_t needed = 8;
        while (needed * 7 < entryCount * 8)
        {
            needed *= 2;
        }
        if (needed > capacityCount)
        {
            rehash(needed);
        }
    }

    // Keeps the capacity.
    void clear()
    {
        for (size_t slot = 0; slot < capacityCount; ++slot)
        {
            if (occupied[slot])
            {
                entries[slot].~Entry();
                occupied[slot] = 0;
            }
        }
        count = 0;
    }

    size_t size() const
    {
        return count;
    }

    bool empty() const
    {
        return count == 0;
    }

    size_t capacity() const
    {
        return capacityCount;
    }

    template <class EntryType, class MapType>
    class IteratorBase
    {
    public:
        IteratorBase(MapType* map, size_t slot)
            : map(map)
            , slot(slot)
        {
            skipEmpty();
        }

        EntryType& operator*() const
        {
            return map->entries[slot];
        }

        EntryType* operator->() const
        {
            return &map->entries[slot];
        }

        IteratorBase& operator++()
        {
            ++slot;
            skipEmpty();
            return *this;
        }

        bool operator==(const IteratorBase& other) const
        {
            return slot == other.slot;
        }

    private:
        void skipEmpty()
        {
            while (slot < map->capacityCount && !map->occupied[slot])
            {
                ++slot;
            }
        }

        MapType* map;
        size_t slot;
    };

    using Iterator = IteratorBase<Entry, HashMap>;
    using ConstIterator = IteratorBase<const Entry, const HashMap>;

    // In slot order, which changes as the map grows.
    Iterator begin()
    {
        return {this, 0};
    }

    Iterator end()
    {
        return {this, capacityCount};
    }

    ConstIterator begin() const
    {
        return {this, 0};
    }

    ConstIterator end() const
    {
        return {this, capacityCount};
    }

private:
    static constexpr size_t notFound = ~size_t{0};

    size_t home(const K& key) const
    {
        return static_cast<size_t>(Hasher{}(key)) & (capacityCount - 1);
    }

    size_t findSlot(const K& key) const
    {
        if (count == 0)
        {
            return notFound;
        }
        for (size_t slot = home(key);; slot = (slot + 1) & (capacityCount - 1))
        {
            if (!occupied[slot])
            {
                return notFound;
            }
            if (entries[slot].key == key)
            {
                return slot;
            }
        }
    }

    // One block: the entries, then one occupied byte per slot.
    static size_t blockSize(size_t capacity)
    {
        return capacity * sizeof(Entry) + capacity;
    }

    void rehash(size_t newCapacity)
    {
        Entry* oldEntries = entries;
        uint8_t* oldOccupied = occupied;
        const size_t oldCapacity = capacityCount;

        void* block = allocate(blockSize(newCapacity), alignof(Entry), memoryTag);
        entries = static_cast<Entry*>(block);
        occupied = reinterpret_cast<uint8_t*>(entries + newCapacity);
        std::memset(occupied, 0, newCapacity);
        capacityCount = newCapacity;

        for (size_t slot = 0; slot < oldCapacity; ++slot)
        {
            if (oldOccupied[slot])
            {
                size_t target = home(oldEntries[slot].key);
                while (occupied[target])
                {
                    target = (target + 1) & (capacityCount - 1);
                }
                new (&entries[target]) Entry(std::move(oldEntries[slot]));
                oldEntries[slot].~Entry();
                occupied[target] = 1;
            }
        }
        if (oldEntries != nullptr)
        {
            free(oldEntries, blockSize(oldCapacity), memoryTag);
        }
    }

    void release()
    {
        if (entries != nullptr)
        {
            clear();
            free(entries, blockSize(capacityCount), memoryTag);
        }
        forget();
    }

    void forget()
    {
        entries = nullptr;
        occupied = nullptr;
        capacityCount = 0;
        count = 0;
    }

    Entry* entries = nullptr;
    uint8_t* occupied = nullptr;
    size_t capacityCount = 0;
    size_t count = 0;
    MemoryTag memoryTag;
};

} // namespace crucible
