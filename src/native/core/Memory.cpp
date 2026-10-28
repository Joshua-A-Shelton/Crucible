#include <crucible/Memory.h>

#include <atomic>
#include <cstdio>
#include <cstdlib>

#if defined(_WIN32)
#include <malloc.h>
#endif

namespace crucible {

namespace {

constexpr size_t tagCount = static_cast<size_t>(MemoryTag::Count);

void* defaultAllocate(size_t size, size_t alignment, MemoryTag, void*)
{
#if defined(_WIN32)
    return _aligned_malloc(size, alignment);
#else
    // posix_memalign needs a power of two that's a multiple of sizeof(void*).
    void* pointer = nullptr;
    const size_t minimum = alignment < sizeof(void*) ? sizeof(void*) : alignment;
    return posix_memalign(&pointer, minimum, size) == 0 ? pointer : nullptr;
#endif
}

void defaultFree(void* pointer, size_t, MemoryTag, void*)
{
#if defined(_WIN32)
    _aligned_free(pointer);
#else
    std::free(pointer);
#endif
}

AllocatorHooks hooks = {defaultAllocate, defaultFree, nullptr};

// Counted here, not in the hooks, so replaced hooks are counted too.
// Relaxed: the numbers are statistics, not synchronization.
std::atomic<uint64_t> tagBytes[tagCount];
std::atomic<uint64_t> tagAllocations[tagCount];

size_t index(MemoryTag tag)
{
    const size_t value = static_cast<size_t>(tag);
    return value < tagCount ? value : 0;
}

} // namespace

void setAllocatorHooks(const AllocatorHooks& newHooks)
{
    if (newHooks.allocate != nullptr && newHooks.free != nullptr)
    {
        hooks = newHooks;
    }
    else
    {
        hooks = {defaultAllocate, defaultFree, nullptr};
    }
}

void* tryAllocate(size_t size, size_t alignment, MemoryTag tag)
{
    if (size == 0)
    {
        return nullptr;
    }
    void* pointer = hooks.allocate(size, alignment, tag, hooks.userData);
    if (pointer != nullptr)
    {
        tagBytes[index(tag)].fetch_add(size, std::memory_order_relaxed);
        tagAllocations[index(tag)].fetch_add(1, std::memory_order_relaxed);
    }
    return pointer;
}

void* allocate(size_t size, size_t alignment, MemoryTag tag)
{
    void* pointer = tryAllocate(size, alignment, tag);
    if (pointer == nullptr && size != 0)
    {
        // ADR 0032: fatal, with what failed. The crash path (ADR 0011) takes
        // over the report once it exists.
        std::fprintf(stderr, "crucible: out of memory allocating %zu bytes (alignment %zu) for %s\n", size, alignment,
                     memoryTagName(tag));
        std::fflush(stderr);
        std::abort();
    }
    return pointer;
}

void free(void* pointer, size_t size, MemoryTag tag)
{
    if (pointer == nullptr)
    {
        return;
    }
    hooks.free(pointer, size, tag, hooks.userData);
    tagBytes[index(tag)].fetch_sub(size, std::memory_order_relaxed);
    tagAllocations[index(tag)].fetch_sub(1, std::memory_order_relaxed);
}

MemoryUsage memoryUsage(MemoryTag tag)
{
    return {tagBytes[index(tag)].load(std::memory_order_relaxed),
            tagAllocations[index(tag)].load(std::memory_order_relaxed)};
}

const char* memoryTagName(MemoryTag tag)
{
    switch (tag)
    {
    case MemoryTag::Platform:
        return "Platform";
    case MemoryTag::Render:
        return "Render";
    case MemoryTag::Jobs:
        return "Jobs";
    case MemoryTag::Ecs:
        return "Ecs";
    case MemoryTag::Assets:
        return "Assets";
    default:
        return "General";
    }
}

} // namespace crucible
