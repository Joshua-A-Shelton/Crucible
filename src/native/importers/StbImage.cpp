// stb_image's implementation, with its allocations going through Crucible's
// allocator (ADR 0032) under the Assets tag. Only the decoders the texture
// importer offers are compiled.

#include <crucible/Memory.h>

#include <cstddef>
#include <cstring>

namespace {

// stb's free gives no size and Crucible's needs one, so each block keeps it.
constexpr size_t prefix = 16;

void* stbAllocate(size_t size)
{
    auto* block = static_cast<std::byte*>(crucible::tryAllocate(size + prefix, 16, crucible::MemoryTag::Assets));
    if (block == nullptr)
    {
        return nullptr;
    }
    std::memcpy(block, &size, sizeof(size));
    return block + prefix;
}

void stbFree(void* pointer)
{
    if (pointer == nullptr)
    {
        return;
    }
    std::byte* block = static_cast<std::byte*>(pointer) - prefix;
    size_t size;
    std::memcpy(&size, block, sizeof(size));
    crucible::free(block, size + prefix, crucible::MemoryTag::Assets);
}

void* stbReallocate(void* pointer, size_t newSize)
{
    void* moved = stbAllocate(newSize);
    if (moved != nullptr && pointer != nullptr)
    {
        size_t oldSize;
        std::memcpy(&oldSize, static_cast<std::byte*>(pointer) - prefix, sizeof(oldSize));
        std::memcpy(moved, pointer, oldSize < newSize ? oldSize : newSize);
        stbFree(pointer);
    }
    return moved;
}

} // namespace

#define STBI_MALLOC(size) stbAllocate(size)
#define STBI_REALLOC(pointer, size) stbReallocate(pointer, size)
#define STBI_FREE(pointer) stbFree(pointer)
#define STBI_ONLY_PNG
#define STBI_ONLY_JPEG
#define STBI_ONLY_TGA
#define STBI_ONLY_HDR
#define STBI_NO_STDIO
#define STBI_FAILURE_USERMSG
#define STB_IMAGE_IMPLEMENTATION
#if defined(_MSC_VER)
#pragma warning(push, 0)
#else
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-Wall"
#pragma GCC diagnostic ignored "-Wextra"
#pragma GCC diagnostic ignored "-Wpedantic"
#pragma GCC diagnostic ignored "-Wunused-function"
#endif
#include <stb_image.h>
#if defined(_MSC_VER)
#pragma warning(pop)
#else
#pragma GCC diagnostic pop
#endif
