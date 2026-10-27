#pragma once

// Crucible's CPU memory (ADR 0032): every core allocation goes through here,
// tagged with the subsystem it belongs to, so the profiler can show memory
// per subsystem. Running out of memory is fatal; tryAllocate is for large
// requests that are expected to fail sometimes. Names are placeholders
// (docs/naming.md).

#include <crucible/Export.h>
#include <crucible/Expose.h>

#include <cstddef>
#include <cstdint>
#include <new>
#include <utility>

namespace crucible {

// Which subsystem an allocation belongs to. Count is the number of tags.
enum class CRUCIBLE_EXPOSE MemoryTag : uint8_t
{
    General,
    Platform,
    Render, // Slag's allocations are counted here too
    Jobs,
    Ecs,
    Count,
};

// Replaces where memory comes from (for example a game's own allocator).
// Both functions must be thread-safe. allocate returns null on failure;
// Crucible then reports it (or tryAllocate returns null). free receives the
// size and tag the memory was allocated with.
struct AllocatorHooks
{
    void* (*allocate)(size_t size, size_t alignment, MemoryTag tag, void* userData) = nullptr;
    void (*free)(void* pointer, size_t size, MemoryTag tag, void* userData) = nullptr;
    void* userData = nullptr;
};

// Called once by the launcher, before anything allocates: memory must be
// freed by the hooks that allocated it. Null functions restore the default,
// aligned malloc/free.
CRUCIBLE_EXPORT void setAllocatorHooks(const AllocatorHooks& hooks);

// Never returns null for a nonzero size: on failure it reports the size and
// tag and terminates (ADR 0032). A zero size returns null.
CRUCIBLE_EXPORT void* allocate(size_t size, size_t alignment, MemoryTag tag);

// Like allocate, but returns null on failure.
CRUCIBLE_EXPORT void* tryAllocate(size_t size, size_t alignment, MemoryTag tag);

// `size` and `tag` must match the allocation. Null is ignored.
CRUCIBLE_EXPORT void free(void* pointer, size_t size, MemoryTag tag);

// Allocates and constructs one object.
template <class T, class... Args>
T* create(MemoryTag tag, Args&&... args)
{
    return new (allocate(sizeof(T), alignof(T), tag)) T(std::forward<Args>(args)...);
}

// Destroys an object made by create. T must be its exact type, so the size
// given back matches. Null is ignored.
template <class T>
void destroy(MemoryTag tag, T* object)
{
    if (object != nullptr)
    {
        object->~T();
        free(object, sizeof(T), tag);
    }
}

// What one tag currently holds: bytes and number of live allocations.
struct CRUCIBLE_EXPOSE MemoryUsage
{
    uint64_t bytes;
    uint64_t allocations;
};

CRUCIBLE_EXPOSE MemoryUsage memoryUsage(MemoryTag tag);

// The tag's name, for messages ("Render").
CRUCIBLE_EXPORT const char* memoryTagName(MemoryTag tag);

} // namespace crucible
