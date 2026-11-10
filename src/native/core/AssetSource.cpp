// The runtime asset contract (ADR 0076): the installed source, and reading
// through it for C#.

#include <crucible/AssetSource.h>
#include <crucible/Memory.h>
#include <crucible/Vector.h>

#include <algorithm>
#include <cstring>

namespace crucible::assets {

namespace {

int32_t nothing(const AssetId*, void*, AssetDelivery)
{
    return -1;
}

AssetSource installed{nothing};
bool explicitlyInstalled = false;

// The last asset C# asked the size of, kept for copyAsset.
struct LastRead
{
    AssetId id{};
    bool found = false;
    Vector<uint8_t> bytes{MemoryTag::General};
};

LastRead& lastRead()
{
    static LastRead instance;
    return instance;
}

void readInto(const AssetId& id)
{
    LastRead& last = lastRead();
    last.id = id;
    last.bytes.clear();
    auto deliver = [](void* context, const uint8_t* data, uint64_t size) {
        static_cast<Vector<uint8_t>*>(context)->assign(std::span<const uint8_t>(data, static_cast<size_t>(size)));
    };
    last.found = installed.read(&id, &last.bytes, deliver) == 0;
}

} // namespace

void installAssetSource(const AssetSource& source)
{
    installed = source;
    explicitlyInstalled = true;
    lastRead().found = false;
    lastRead().id = {};
}

const AssetSource& assetSource()
{
    return installed;
}

bool assetSourceInstalled()
{
    return explicitlyInstalled;
}

void setAssetReader(uint64_t readerAddress)
{
    if (readerAddress == 0)
    {
        installed = {nothing};
        explicitlyInstalled = false;
        lastRead().found = false;
        return;
    }
    installAssetSource({reinterpret_cast<decltype(AssetSource::read)>(static_cast<uintptr_t>(readerAddress))});
}

uint64_t assetSize(AssetId id)
{
    readInto(id);
    return lastRead().found ? lastRead().bytes.size() : UINT64_MAX;
}

uint64_t copyAsset(AssetId id, std::span<uint8_t> out)
{
    LastRead& last = lastRead();
    if (!(last.id == id) || !last.found)
    {
        readInto(id);
    }
    if (!last.found)
    {
        return 0;
    }
    const size_t count = std::min(out.size(), last.bytes.size());
    std::memcpy(out.data(), last.bytes.data(), count);
    return count;
}

} // namespace crucible::assets
