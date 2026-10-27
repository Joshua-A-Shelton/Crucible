#pragma once

// Growable arrays on Crucible's allocator (ADR 0032), started from Slag's
// Vector (Slag ADR 0066, zlib). Growth can't fail: running out of memory is
// fatal. Move-only; copy() when a copy is meant, so an accidental copy in hot
// code is a compile error instead of a hidden allocation. Names are
// placeholders (docs/naming.md).
//
//     Vector<int> values(MemoryTag::Ecs);
//     values.pushBack(1);
//     SmallVector<Barrier, 8> batch; // 8 inline, then the heap

#include <crucible/Memory.h>

#include <cstddef>
#include <new>
#include <span>
#include <type_traits>
#include <utility>

// MSVC accepts the standard attribute but ignores it.
#if defined(_MSC_VER) && !defined(__clang__)
#define CRUCIBLE_NO_UNIQUE_ADDRESS [[msvc::no_unique_address]]
#else
#define CRUCIBLE_NO_UNIQUE_ADDRESS [[no_unique_address]]
#endif

namespace crucible {

namespace detail {

template <class T, size_t InlineCount>
struct InlineStorage
{
    T* data()
    {
        return reinterpret_cast<T*>(bytes);
    }

    alignas(T) unsigned char bytes[InlineCount * sizeof(T)];
};

template <class T>
struct InlineStorage<T, 0>
{
    T* data()
    {
        return nullptr;
    }
};

// Vector and SmallVector in one: the first InlineCount elements live inside
// the object, the rest on the heap. Inline storage is used while `heap` is
// null; the elements' address is recomputed rather than stored, so moving
// the object can't leave a pointer into the old one.
template <class T, size_t InlineCount>
class VectorImpl
{
public:
    explicit VectorImpl(MemoryTag tag = MemoryTag::General)
        : memoryTag(tag)
    {}

    VectorImpl(const VectorImpl&) = delete;
    VectorImpl& operator=(const VectorImpl&) = delete;

    VectorImpl(VectorImpl&& other) noexcept
        : memoryTag(other.memoryTag)
    {
        take(other);
    }

    VectorImpl& operator=(VectorImpl&& other) noexcept
    {
        if (this != &other)
        {
            release();
            memoryTag = other.memoryTag;
            take(other);
        }
        return *this;
    }

    ~VectorImpl()
    {
        release();
    }

    // An explicit copy, with the same tag.
    VectorImpl copy() const
    {
        VectorImpl result(memoryTag);
        result.assign(span());
        return result;
    }

    // Replaces the contents with copies of `values`.
    void assign(std::span<const T> values)
    {
        clear();
        reserve(values.size());
        for (const T& value : values)
        {
            new (data() + count) T(value);
            ++count;
        }
    }

    void reserve(size_t newCapacity)
    {
        if (newCapacity > capacityCount)
        {
            reallocate(newCapacity, [](T*) {});
        }
    }

    // New elements are value-initialized (zero for plain data).
    void resize(size_t newCount)
    {
        reserve(newCount);
        while (count > newCount)
        {
            popBack();
        }
        for (T* element = data() + count; count < newCount; ++element)
        {
            new (element) T();
            ++count;
        }
    }

    void pushBack(const T& value)
    {
        emplaceBack(value);
    }

    void pushBack(T&& value)
    {
        emplaceBack(std::move(value));
    }

    // `args` may refer to an element of this vector: when it grows, the new
    // element is constructed before the old ones move.
    template <class... Args>
    T& emplaceBack(Args&&... args)
    {
        if (count == capacityCount)
        {
            const size_t at = count;
            reallocate(capacityCount < 4 ? 4 : capacityCount * 2,
                       [&](T* elements) { new (elements + at) T(std::forward<Args>(args)...); });
        }
        else
        {
            new (data() + count) T(std::forward<Args>(args)...);
        }
        return data()[count++];
    }

    void popBack()
    {
        --count;
        data()[count].~T();
    }

    // Removes element `index` by moving the last element into its place: O(1),
    // but the order changes.
    void removeSwap(size_t index)
    {
        T* elements = data();
        if (index != count - 1)
        {
            elements[index] = std::move(elements[count - 1]);
        }
        popBack();
    }

    // Keeps the capacity, so refilling doesn't allocate.
    void clear()
    {
        T* elements = data();
        for (size_t i = 0; i < count; ++i)
        {
            elements[i].~T();
        }
        count = 0;
    }

    size_t size() const
    {
        return count;
    }

    size_t capacity() const
    {
        return capacityCount;
    }

    bool empty() const
    {
        return count == 0;
    }

    MemoryTag tag() const
    {
        return memoryTag;
    }

    T* data()
    {
        return heap != nullptr ? heap : inlineStorage.data();
    }

    const T* data() const
    {
        return const_cast<VectorImpl*>(this)->data();
    }

    std::span<T> span()
    {
        return {data(), count};
    }

    std::span<const T> span() const
    {
        return {data(), count};
    }

    T& operator[](size_t index)
    {
        return data()[index];
    }

    const T& operator[](size_t index) const
    {
        return data()[index];
    }

    T& back()
    {
        return data()[count - 1];
    }

    const T& back() const
    {
        return data()[count - 1];
    }

    T* begin()
    {
        return data();
    }

    T* end()
    {
        return data() + count;
    }

    const T* begin() const
    {
        return data();
    }

    const T* end() const
    {
        return data() + count;
    }

private:
    // Moves the elements into a new heap block of `newCapacity`, after
    // `construct` has had the chance to build a new element there.
    template <class Construct>
    void reallocate(size_t newCapacity, Construct&& construct)
    {
        T* newElements = static_cast<T*>(allocate(newCapacity * sizeof(T), alignof(T), memoryTag));
        construct(newElements);
        T* oldElements = data();
        for (size_t i = 0; i < count; ++i)
        {
            new (newElements + i) T(std::move(oldElements[i]));
            oldElements[i].~T();
        }
        if (heap != nullptr)
        {
            free(heap, capacityCount * sizeof(T), memoryTag);
        }
        heap = newElements;
        capacityCount = newCapacity;
    }

    void take(VectorImpl& other)
    {
        if (other.heap != nullptr)
        {
            heap = other.heap;
            capacityCount = other.capacityCount;
            count = other.count;
        }
        else
        {
            // Inline elements can't change hands; they move one by one.
            T* source = other.data();
            for (size_t i = 0; i < other.count; ++i)
            {
                new (inlineStorage.data() + i) T(std::move(source[i]));
                source[i].~T();
            }
            count = other.count;
        }
        other.heap = nullptr;
        other.count = 0;
        other.capacityCount = InlineCount;
    }

    void release()
    {
        clear();
        if (heap != nullptr)
        {
            free(heap, capacityCount * sizeof(T), memoryTag);
            heap = nullptr;
        }
        capacityCount = InlineCount;
    }

    T* heap = nullptr;
    size_t count = 0;
    size_t capacityCount = InlineCount;
    MemoryTag memoryTag;
    CRUCIBLE_NO_UNIQUE_ADDRESS InlineStorage<T, InlineCount> inlineStorage;
};

} // namespace detail

template <class T>
using Vector = detail::VectorImpl<T, 0>;

} // namespace crucible
