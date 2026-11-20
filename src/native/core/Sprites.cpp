// Sprites (ADR 0104, 0106) and tilemaps (ADR 0108): the components, the
// sorting layers that y-sort
// and that draw behind the 3D, the textures sprites show (acquired on first
// use, released when unused), and the Sprites render step: each frame the
// world's sprites gathered and sorted by sorting layer, order in layer, and
// (where the layer y-sorts) height. Without a pixel camera they're drawn in
// one instanced draw over the finished image. Through one, they're drawn at
// its resolution, snapped to its grid, into two small images (behind the 3D
// and in front of it), which are scaled up over the finished image: the one
// behind only where the 3D drew nothing. The editor's Pick step draws them
// again into its entity IDs.

#include "RenderInternal.h"
#include "SpriteAnimation.h"
#include "StateMachine.h"

#include <crucible/AssetLoads.h>
#include <crucible/Ecs.h>
#include <crucible/HashMap.h>
#include <crucible/Log.h>
#include <crucible/Render.h>
#include <crucible/Sprite.h>
#include <crucible/Tilemap.h>
#include <crucible/TilemapFile.h>
#include <crucible/Transform.h>

#include <slag/Slag.h>

#include <algorithm>
#include <array>
#include <cmath>
#include <cstring>

namespace crucible::render2d {

namespace {

uint32_t ySorted = 0;
uint32_t behind3D = 0;

} // namespace

void setYSortLayers(uint32_t mask)
{
    ySorted = mask;
}

uint32_t ySortLayers()
{
    return ySorted;
}

void setBehind3DLayers(uint32_t mask)
{
    behind3D = mask;
}

uint32_t behind3DLayers()
{
    return behind3D;
}

} // namespace crucible::render2d

namespace crucible::render {

namespace {

using assets::AssetId;
using render2d::PixelCamera;
using render2d::Sprite;
using scenes::WorldTransform;

// A texture no sprite has shown for this many frames is released.
constexpr uint64_t unusedTextureFrames = 240;

struct AssetIdHash
{
    uint64_t operator()(const AssetId& id) const
    {
        uint64_t low, high;
        std::memcpy(&low, id.bytes, 8);
        std::memcpy(&high, id.bytes + 8, 8);
        return Hash<uint64_t>()(low ^ (high * 0x9e3779b97f4a7c15ull));
    }
};

bool isZero(const AssetId& id)
{
    for (uint8_t byte : id.bytes)
    {
        if (byte != 0)
        {
            return false;
        }
    }
    return true;
}

// A texture acquired for sprites.
struct CachedTexture
{
    TextureHandle handle{}; // generation 0: it failed to load, tried again once expired
    uint64_t lastUsed = 0;  // the gather that last showed it
};

// The GPU's view of a sprite: sprite.slang's SpriteInstance.
struct SpriteInstance
{
    Vector2 axisX;
    Vector2 axisY;
    Vector3 origin;
    float pad0;
    Vector2 localMin;
    Vector2 localMax;
    Vector2 uvMin;
    Vector2 uvMax;
    Vector4 tint;
    uint32_t texture;
    uint32_t entity; // index + 1
    uint32_t pad1;
    uint32_t pad2;
};
static_assert(sizeof(SpriteInstance) == 96);

// A gathered sprite, or a run of a tilemap's tiles, before sorting.
struct Gathered
{
    uint32_t layer;
    int32_t order;
    float height; // the pivot's y (a run's bottom), for layers that y-sort
    uint32_t entity;
    bool behind; // through a pixel camera, in a layer behind the 3D
    SpriteInstance instance;
    uint32_t run = UINT32_MAX; // into State::runs; the instance is its placing
};

// A row of a tilemap's chunk: its tiles' first and count, and its bottom in
// the map's space.
struct TileRow
{
    uint32_t first;
    uint32_t count;
    float bottom;
};

// A layer's tiles and rows, as ranges of its map's (ADR 0112).
struct TileLayerRange
{
    uint32_t id;
    uint32_t firstCell; // its cells, and once built its tiles, in the map's
    uint32_t cellCount;
    uint32_t firstRow;
    uint32_t rowCount;
};

// A tilemap read from its TMAP file, and its tiles as the GPU draws them in
// the map's space (built once all its textures are ready): layer by layer,
// chunk by chunk of chunkTiles squared, each row by row, so a row is a run
// to sort. A tile carries its own texture, so a layer mixing tilesets is
// drawn as one that doesn't.
struct TilemapCache
{
    uint64_t load = 0; // which load filled it; a reload starts another
    bool loading = true;
    bool built = false;
    assets::TilemapInfo info{};
    Vector<AssetId> textures{MemoryTag::Render};
    Vector<TileLayerRange> layers{MemoryTag::Render};
    Vector<assets::TilemapCell> cells{MemoryTag::Render};
    Vector<SpriteInstance> tiles{MemoryTag::Render};
    Vector<TileRow> rows{MemoryTag::Render};
};

constexpr int32_t chunkTiles = 16;

// A run to draw: a tilemap's tiles, placed by its entity.
struct TileRun
{
    const SpriteInstance* tiles;
    uint32_t count;
};

// sprite.slang's SpriteConstants and pixelComposite.slang's CompositeConstants.
struct SpriteConstants
{
    uint32_t sprites;
    uint32_t first;
    uint32_t pad0;
    uint32_t pad1;
    Matrix4x4 viewProjection;
};
static_assert(sizeof(SpriteConstants) == 80);

struct CompositeConstants
{
    uint32_t source;
    uint32_t depth;
    uint32_t depthSamples;
    uint32_t bars;
    Vector2 sourceSize;
    Vector2 offset;
    float scale;
    float pad0;
    float pad1;
    float pad2;
};
static_assert(sizeof(CompositeConstants) == 48);

// A surface's sprites this frame: the sorted instances (through a pixel
// camera, the ones behind the 3D first), and how they're drawn.
struct FrameSprites
{
    uint64_t frame = 0;
    uint32_t count = 0;
    uint32_t behind = 0; // the first `behind` instances are behind the 3D
    bool pixel = false;  // through a pixel camera
    PixelCamera camera{};
    Matrix4x4 viewProjection = Matrix4x4::identity();
    float scale = 1;  // window pixels a virtual pixel
    Vector2 offset{}; // where the scaled image starts, in window pixels
};

// Each surface's upload buffers, one per frame slot, grown as needed.
struct SurfaceBuffers
{
    std::array<slag::Buffer*, framesInFlight> buffers{};
    std::array<uint32_t, framesInFlight> capacity{};
};

struct State
{
    HashMap<AssetId, CachedTexture, AssetIdHash> textures{MemoryTag::Render};
    uint64_t gathers = 0;
    Vector<Gathered> gathered{MemoryTag::Render};
    Vector<AssetId> expired{MemoryTag::Render};
    // The last gather's sorted sprites, by surface (the Sprites and Pick
    // steps of one frame share them).
    std::array<FrameSprites, 8> frames{};
    std::array<Vector<SpriteInstance>, 8> sorted{
        Vector<SpriteInstance>(MemoryTag::Render), Vector<SpriteInstance>(MemoryTag::Render),
        Vector<SpriteInstance>(MemoryTag::Render), Vector<SpriteInstance>(MemoryTag::Render),
        Vector<SpriteInstance>(MemoryTag::Render), Vector<SpriteInstance>(MemoryTag::Render),
        Vector<SpriteInstance>(MemoryTag::Render), Vector<SpriteInstance>(MemoryTag::Render)};
    std::array<SurfaceBuffers, 8> surfaces{};
    HashMap<AssetId, TilemapCache*, AssetIdHash> tilemaps{MemoryTag::Render};
    uint64_t tilemapLoads = 0;
    Vector<TileRun> runs{MemoryTag::Render};
    slag::Pipeline* pipeline = nullptr;
    slag::Pipeline* pickPipeline = nullptr;
    slag::Pipeline* pixelPipeline = nullptr;     // into a pixel camera's small images
    slag::Pipeline* compositePipeline = nullptr; // scaling them up
};

State state;

// The texture's handle, acquired on first use.
TextureHandle resolve(const AssetId& id)
{
    if (isZero(id))
    {
        return {};
    }
    if (CachedTexture* found = state.textures.find(id))
    {
        found->lastUsed = state.gathers;
        return found->handle;
    }
    CachedTexture cached{.lastUsed = state.gathers};
    if (Result<TextureHandle> acquired = acquireTexture(id))
    {
        cached.handle = acquired.value();
    }
    else
    {
        log::warning("a sprite shows a texture that couldn't be loaded (status %d)",
                     static_cast<int>(acquired.status()));
    }
    state.textures.insertOrAssign(id, cached);
    return cached.handle;
}

void expireTextures()
{
    state.expired.clear();
    for (auto& entry : state.textures)
    {
        const CachedTexture& cached = entry.value;
        if (state.gathers - cached.lastUsed > unusedTextureFrames &&
            (cached.handle.generation == 0 || !isTextureLoading(cached.handle)))
        {
            state.expired.pushBack(entry.key);
        }
    }
    for (const AssetId& id : state.expired)
    {
        if (const CachedTexture* cached = state.textures.find(id); cached->handle.generation != 0)
        {
            releaseTexture(cached->handle);
        }
        state.textures.erase(id);
    }
}

// The sprite as the GPU draws it; false while its texture isn't ready.
bool instanceOf(const Sprite& sprite, const WorldTransform& transform, uint32_t entityIndex, SpriteInstance& out)
{
    const TextureHandle texture = resolve(sprite.texture);
    if (texture.generation == 0 || !isTextureReady(texture))
    {
        return false;
    }
    const TextureSize size = textureSize(texture);
    if (size.width == 0 || size.height == 0)
    {
        return false;
    }
    render2d::SpriteRegion region = sprite.region;
    if (region.width == 0 || region.height == 0)
    {
        region = {0, 0, size.width, size.height};
    }
    const float pixelsPerUnit = texturePixelsPerUnit(texture);
    const float width = static_cast<float>(region.width) / pixelsPerUnit;
    const float height = static_cast<float>(region.height) / pixelsPerUnit;
    const Matrix4x4& matrix = transform.matrix;
    out.axisX = {matrix.rows[0].x, matrix.rows[0].y};
    out.axisY = {matrix.rows[1].x, matrix.rows[1].y};
    out.origin = {matrix.rows[3].x, matrix.rows[3].y, matrix.rows[3].z};
    out.localMin = {-sprite.pivot.x * width, -sprite.pivot.y * height};
    out.localMax = {out.localMin.x + width, out.localMin.y + height};
    const float invWidth = 1.0f / static_cast<float>(size.width);
    const float invHeight = 1.0f / static_cast<float>(size.height);
    Vector2 uvMin{static_cast<float>(region.x) * invWidth, static_cast<float>(region.y) * invHeight};
    Vector2 uvMax{static_cast<float>(region.x + region.width) * invWidth,
                  static_cast<float>(region.y + region.height) * invHeight};
    if (sprite.flipX)
    {
        std::swap(uvMin.x, uvMax.x);
    }
    if (sprite.flipY)
    {
        std::swap(uvMin.y, uvMax.y);
    }
    out.uvMin = uvMin;
    out.uvMax = uvMax;
    out.tint = sprite.tint;
    out.texture = textureDescriptor(texture);
    out.entity = entityIndex + 1;
    return true;
}

slag::Buffer* uploadSprites(slag::GraphicsDevice& device, uint32_t surface, uint32_t frameSlot, uint32_t slot,
                            const Vector<SpriteInstance>& sprites);

// --- Tilemaps (ADR 0108, 0112) -------------------------------------------------

// Its textures, layers, and cells from the map file's bytes; false if
// they're wrong. Any thread (a load's job).
bool readTilemap(std::span<const uint8_t> bytes, TilemapCache& into)
{
    Result<binary::File*> opened = binary::parseFile(bytes, assets::tilemapFileKind, assets::tilemapFileVersion);
    if (!opened)
    {
        return false;
    }
    Vector<assets::TilemapInfo> info(MemoryTag::Render);
    Vector<assets::TilemapLayer> layers(MemoryTag::Render);
    bool good = animation::readTable(*opened.value(), "INFO", info) && info.size() == 1 &&
                animation::readTable(*opened.value(), "TXTR", into.textures) &&
                animation::readTable(*opened.value(), "LAYR", layers) &&
                animation::readTable(*opened.value(), "CELL", into.cells) && into.cells.size() == info[0].cellCount &&
                into.textures.size() == info[0].textureCount && layers.size() == info[0].layerCount;
    binary::closeFile(opened.value());
    into.layers.clear();
    for (size_t i = 0; good && i < layers.size(); ++i)
    {
        const assets::TilemapLayer& layer = layers[i];
        good = uint64_t{layer.firstCell} + layer.cellCount <= into.cells.size();
        into.layers.pushBack({layer.id, layer.firstCell, layer.cellCount, 0, 0});
    }
    for (size_t i = 0; good && i < into.cells.size(); ++i)
    {
        good = into.cells[i].texture < into.textures.size();
    }
    if (good)
    {
        into.info = info[0];
    }
    return good;
}

struct TilemapLoad
{
    AssetId id;
    uint64_t load;
    TilemapCache read;
    bool good = false;
};

// The tilemap, or null: it couldn't be read, or it's loading (a load
// started now if need be).
TilemapCache* tilemapOf(const AssetId& id)
{
    if (isZero(id))
    {
        return nullptr;
    }
    if (TilemapCache** found = state.tilemaps.find(id))
    {
        return (*found)->loading ? nullptr : *found;
    }
    auto* cache = create<TilemapCache>(MemoryTag::Render);
    cache->load = ++state.tilemapLoads;
    state.tilemaps.insertOrAssign(id, cache);
    auto* load = create<TilemapLoad>(MemoryTag::Render);
    load->id = id;
    load->load = cache->load;
    assets::startLoad({
        .id = id,
        .context = load,
        .prepare =
            [](void* context, std::span<const uint8_t> bytes, bool found) {
                auto& self = *static_cast<TilemapLoad*>(context);
                self.good = found && readTilemap(bytes, self.read);
            },
        .publish =
            [](void* context) {
                auto* self = static_cast<TilemapLoad*>(context);
                TilemapCache** found = state.tilemaps.find(self->id);
                if (found != nullptr && (*found)->load == self->load) // not reloaded meanwhile
                {
                    TilemapCache& cache = **found;
                    cache.loading = false;
                    cache.info = self->read.info;
                    cache.textures = static_cast<Vector<AssetId>&&>(self->read.textures);
                    cache.layers = static_cast<Vector<TileLayerRange>&&>(self->read.layers);
                    cache.cells = static_cast<Vector<assets::TilemapCell>&&>(self->read.cells);
                    if (!self->good)
                    {
                        cache.cells.clear();
                        cache.layers.clear();
                        cache.textures.clear();
                        log::warning("a tilemap's map can't be read (reimport it)");
                    }
                }
                destroy(MemoryTag::Render, self);
            },
    });
    TilemapCache** found = state.tilemaps.find(id);
    return found != nullptr && !(*found)->loading ? *found : nullptr;
}

bool tilemapLoading(const AssetId& id)
{
    TilemapCache** found = isZero(id) ? nullptr : state.tilemaps.find(id);
    return found != nullptr && (*found)->loading;
}

// The layer of an id; null if the map has none.
const TileLayerRange* layerOf(const TilemapCache& map, uint32_t id)
{
    for (const TileLayerRange& layer : map.layers)
    {
        if (layer.id == id)
        {
            return &layer;
        }
    }
    return nullptr;
}

// Whether every texture the map's tiles use is ready to draw.
bool texturesReady(const TilemapCache& map)
{
    for (const AssetId& id : map.textures)
    {
        const TextureHandle texture = resolve(id);
        if (texture.generation == 0 || !isTextureReady(texture))
        {
            return false;
        }
    }
    return true;
}

// Its tiles as quads in the map's space, once its textures are ready: each
// layer's chunk by chunk, row by row within each, so rows are runs.
void buildTiles(TilemapCache& map)
{
    struct Texture
    {
        uint32_t descriptor;
        float invWidth;
        float invHeight;
    };
    Vector<Texture> textures(MemoryTag::Render);
    for (const AssetId& id : map.textures)
    {
        const TextureHandle texture = resolve(id);
        const TextureSize size = textureSize(texture);
        if (size.width == 0 || size.height == 0)
        {
            return;
        }
        textures.pushBack({textureDescriptor(texture), 1.0f / static_cast<float>(size.width),
                           1.0f / static_cast<float>(size.height)});
    }
    auto chunkOf = [](int32_t v) {
        return v >= 0 ? v / chunkTiles : (v + 1) / chunkTiles - 1;
    };
    map.tiles.clear();
    map.rows.clear();
    const float w = map.info.cellWidth, h = map.info.cellHeight;
    for (TileLayerRange& layer : map.layers)
    {
        assets::TilemapCell* first = map.cells.data() + layer.firstCell;
        std::sort(first, first + layer.cellCount, [&](const assets::TilemapCell& a, const assets::TilemapCell& b) {
            const int32_t acy = chunkOf(a.y), bcy = chunkOf(b.y), acx = chunkOf(a.x), bcx = chunkOf(b.x);
            if (acy != bcy)
            {
                return acy < bcy;
            }
            if (acx != bcx)
            {
                return acx < bcx;
            }
            return a.y != b.y ? a.y < b.y : a.x < b.x;
        });
        layer.firstRow = static_cast<uint32_t>(map.rows.size());
        for (uint32_t i = layer.firstCell; i < layer.firstCell + layer.cellCount; ++i)
        {
            const assets::TilemapCell& cell = map.cells[i];
            const Texture& texture = textures[cell.texture];
            SpriteInstance tile{};
            tile.localMin = {static_cast<float>(cell.x) * w, static_cast<float>(cell.y) * h};
            tile.localMax = {tile.localMin.x + w, tile.localMin.y + h};
            Vector2 uvMin{static_cast<float>(cell.pixelX) * texture.invWidth,
                          static_cast<float>(cell.pixelY) * texture.invHeight};
            Vector2 uvMax{static_cast<float>(cell.pixelX + map.info.tileWidth) * texture.invWidth,
                          static_cast<float>(cell.pixelY + map.info.tileHeight) * texture.invHeight};
            if ((cell.flags & static_cast<uint32_t>(assets::TileFlip::X)) != 0)
            {
                std::swap(uvMin.x, uvMax.x);
            }
            if ((cell.flags & static_cast<uint32_t>(assets::TileFlip::Y)) != 0)
            {
                std::swap(uvMin.y, uvMax.y);
            }
            tile.uvMin = uvMin;
            tile.uvMax = uvMax;
            tile.texture = texture.descriptor;
            const uint32_t index = static_cast<uint32_t>(map.tiles.size());
            map.tiles.pushBack(tile);
            const bool sameRow = map.rows.size() > layer.firstRow && map.rows.back().bottom == tile.localMin.y &&
                                 chunkOf(map.cells[map.rows.back().first].x) == chunkOf(cell.x) &&
                                 chunkOf(map.cells[map.rows.back().first].y) == chunkOf(cell.y);
            if (sameRow)
            {
                ++map.rows.back().count;
            }
            else
            {
                map.rows.pushBack({index, 1, tile.localMin.y});
            }
        }
        layer.rowCount = static_cast<uint32_t>(map.rows.size()) - layer.firstRow;
    }
    map.built = true;
}

void freeTilemaps()
{
    for (auto& entry : state.tilemaps)
    {
        destroy(MemoryTag::Render, entry.value);
    }
    state.tilemaps.clear();
}

// The pixel camera drawing the world (the lowest entity with one), unless
// the surface has a camera of its own (the editor's scene view); false if
// there's none. Its position, snapped so its bottom-left corner is on the
// grid, is `center`.
bool pixelCameraOf(const RenderStepContext& context, PixelCamera& camera, Vector2& center)
{
    if (context.world == nullptr || surfaceHasOwnCamera(context.surface) ||
        ecs::componentIdOf<PixelCamera> == ecs::invalidComponent)
    {
        return false;
    }
    struct Found
    {
        bool any = false;
        uint32_t entity = UINT32_MAX;
        PixelCamera camera{};
        Vector2 position{};
    } found;
    const ecs::ComponentId required[] = {ecs::componentIdOf<PixelCamera>, ecs::componentIdOf<WorldTransform>};
    ecs::Query* query = ecs::createQuery(*context.world, {.required = required});
    ecs::forEachChunk(
        *query,
        [](const ecs::ChunkView& view, void* data) {
            auto& found = *static_cast<Found*>(data);
            const std::span<const PixelCamera> cameras = ecs::readColumn<PixelCamera>(view);
            const std::span<const WorldTransform> transforms = ecs::readColumn<WorldTransform>(view);
            const ecs::Entity* entities = ecs::entitiesOf(view);
            for (size_t i = 0; i < cameras.size(); ++i)
            {
                if (ecs::indexOf(entities[i]) < found.entity && cameras[i].width > 0 && cameras[i].height > 0 &&
                    cameras[i].pixelsPerUnit > 0)
                {
                    found = {true,
                             ecs::indexOf(entities[i]),
                             cameras[i],
                             {transforms[i].matrix.rows[3].x, transforms[i].matrix.rows[3].y}};
                }
            }
        },
        &found);
    ecs::destroyQuery(query);
    if (!found.any)
    {
        return false;
    }
    camera = found.camera;
    const float ppu = camera.pixelsPerUnit;
    const Vector2 half{static_cast<float>(camera.width) / (2 * ppu), static_cast<float>(camera.height) / (2 * ppu)};
    center = {std::round((found.position.x - half.x) * ppu) / ppu + half.x,
              std::round((found.position.y - half.y) * ppu) / ppu + half.y};
    return true;
}

slag::Buffer* uploadSprites(slag::GraphicsDevice& device, uint32_t surface, uint32_t frameSlot, uint32_t slot,
                            const Vector<SpriteInstance>& sprites);

// The world's tilemaps as runs to sort with the sprites: the whole map as
// one, or (in a layer that y-sorts) each chunk's rows.
void gatherTilemaps(ecs::World& world, bool pixel, float pixelsPerUnit, uint32_t behindLayers)
{
    state.runs.clear();
    if (ecs::componentIdOf<render2d::Tilemap> == ecs::invalidComponent)
    {
        return;
    }
    const uint32_t ySort = render2d::ySortLayers();
    const ecs::ComponentId required[] = {ecs::componentIdOf<render2d::Tilemap>, ecs::componentIdOf<WorldTransform>};
    ecs::Query* query = ecs::createQuery(world, {.required = required});
    struct Found
    {
        ecs::Entity entity;
        render2d::Tilemap tilemap;
        Matrix4x4 matrix;
    };
    Vector<Found> found(MemoryTag::Render);
    ecs::forEachChunk(
        *query,
        [](const ecs::ChunkView& view, void* data) {
            const std::span<const render2d::Tilemap> maps = ecs::readColumn<render2d::Tilemap>(view);
            const std::span<const WorldTransform> transforms = ecs::readColumn<WorldTransform>(view);
            const ecs::Entity* entities = ecs::entitiesOf(view);
            for (size_t i = 0; i < maps.size(); ++i)
            {
                static_cast<Vector<Found>*>(data)->pushBack({entities[i], maps[i], transforms[i].matrix});
            }
        },
        &found);
    ecs::destroyQuery(query);
    for (const Found& one : found)
    {
        TilemapCache* map = tilemapOf(one.tilemap.map);
        if (map == nullptr || map->cells.empty() || !texturesReady(*map))
        {
            continue;
        }
        if (!map->built)
        {
            buildTiles(*map);
            if (!map->built)
            {
                continue;
            }
        }
        const TileLayerRange* drawn = layerOf(*map, one.tilemap.layer);
        if (drawn == nullptr || drawn->cellCount == 0)
        {
            continue;
        }
        const uint32_t layer = one.tilemap.sortingLayer;
        Gathered gathered{.layer = layer,
                          .order = one.tilemap.orderInLayer,
                          .height = one.matrix.rows[3].y,
                          .entity = ecs::indexOf(one.entity),
                          .behind = pixel && layer < 32 && (behindLayers >> layer & 1) != 0,
                          .instance = {}};
        SpriteInstance& placing = gathered.instance;
        placing.axisX = {one.matrix.rows[0].x, one.matrix.rows[0].y};
        placing.axisY = {one.matrix.rows[1].x, one.matrix.rows[1].y};
        placing.origin = {one.matrix.rows[3].x, one.matrix.rows[3].y, one.matrix.rows[3].z};
        placing.tint = one.tilemap.tint;
        placing.entity = gathered.entity + 1;
        if (pixel)
        {
            // The map's origin on the grid, so every tile is.
            placing.origin.x = std::round(placing.origin.x * pixelsPerUnit) / pixelsPerUnit;
            placing.origin.y = std::round(placing.origin.y * pixelsPerUnit) / pixelsPerUnit;
        }
        if (layer < 32 && (ySort >> layer & 1) != 0)
        {
            for (uint32_t r = drawn->firstRow; r < drawn->firstRow + drawn->rowCount; ++r)
            {
                const TileRow& row = map->rows[r];
                gathered.height = placing.origin.y + row.bottom * placing.axisY.y;
                gathered.run = static_cast<uint32_t>(state.runs.size());
                state.runs.pushBack({map->tiles.data() + row.first, row.count});
                state.gathered.pushBack(gathered);
            }
        }
        else
        {
            gathered.run = static_cast<uint32_t>(state.runs.size());
            state.runs.pushBack({map->tiles.data() + drawn->firstCell, drawn->cellCount});
            state.gathered.pushBack(gathered);
        }
    }
}

// The surface's sprites this frame, gathered, sorted, and uploaded once for
// its steps, on the main thread (passes record on workers).
const FrameSprites& frameSprites(const RenderStepContext& context)
{
    const uint32_t surface = context.surface - 1;
    FrameSprites& frame = state.frames[surface];
    Vector<SpriteInstance>& sorted = state.sorted[surface];
    if (frame.frame == context.frame)
    {
        return frame;
    }
    frame = {.frame = context.frame, .viewProjection = context.view.viewProjection};
    sorted.clear();
    state.gathered.clear();
    Vector2 center{};
    frame.pixel = pixelCameraOf(context, frame.camera, center);
    if (frame.pixel)
    {
        // Looking along +Z from 1000 m before the camera's position, at its
        // resolution's size in meters.
        const float ppu = frame.camera.pixelsPerUnit;
        const Matrix4x4 view = Matrix4x4::translation({-center.x, -center.y, 1000});
        const Matrix4x4 projection = Matrix4x4::orthographicReverseZ(
            static_cast<float>(frame.camera.width) / ppu, static_cast<float>(frame.camera.height) / ppu, 1, 2000);
        frame.viewProjection = view * projection;
        // Filling the window: the largest scale that fits (whole numbers
        // only when Exact), centered.
        const float fits = std::min(static_cast<float>(context.width) / static_cast<float>(frame.camera.width),
                                    static_cast<float>(context.height) / static_cast<float>(frame.camera.height));
        frame.scale = frame.camera.fit == render2d::PixelFit::Exact && fits >= 1 ? std::floor(fits) : fits;
        frame.offset = {
            std::floor((static_cast<float>(context.width) - static_cast<float>(frame.camera.width) * frame.scale) / 2),
            std::floor((static_cast<float>(context.height) - static_cast<float>(frame.camera.height) * frame.scale) /
                       2)};
    }
    if (context.world == nullptr || ecs::componentIdOf<Sprite> == ecs::invalidComponent)
    {
        return frame;
    }
    ++state.gathers;
    struct Gathering
    {
        bool pixel;
        float pixelsPerUnit;
        uint32_t behind;
    } gathering{frame.pixel, frame.camera.pixelsPerUnit, render2d::behind3DLayers()};
    const ecs::ComponentId required[] = {ecs::componentIdOf<Sprite>, ecs::componentIdOf<WorldTransform>};
    ecs::Query* query = ecs::createQuery(*context.world, {.required = required});
    ecs::forEachChunk(
        *query,
        [](const ecs::ChunkView& view, void* data) {
            const auto& gathering = *static_cast<const Gathering*>(data);
            const std::span<const Sprite> sprites = ecs::readColumn<Sprite>(view);
            const std::span<const WorldTransform> transforms = ecs::readColumn<WorldTransform>(view);
            const ecs::Entity* entities = ecs::entitiesOf(view);
            for (size_t i = 0; i < sprites.size(); ++i)
            {
                const uint32_t layer = sprites[i].sortingLayer;
                Gathered gathered{.layer = layer,
                                  .order = sprites[i].orderInLayer,
                                  .height = transforms[i].matrix.rows[3].y,
                                  .entity = ecs::indexOf(entities[i]),
                                  .behind = gathering.pixel && layer < 32 && (gathering.behind >> layer & 1) != 0};
                if (!instanceOf(sprites[i], transforms[i], gathered.entity, gathered.instance))
                {
                    continue;
                }
                if (gathering.pixel)
                {
                    // On the grid: the quad's first corner on a whole pixel
                    // (exactly, when it isn't turned).
                    SpriteInstance& instance = gathered.instance;
                    const float ppu = gathering.pixelsPerUnit;
                    const Vector2 corner{instance.origin.x + instance.localMin.x * instance.axisX.x +
                                             instance.localMin.y * instance.axisY.x,
                                         instance.origin.y + instance.localMin.x * instance.axisX.y +
                                             instance.localMin.y * instance.axisY.y};
                    instance.origin.x += std::round(corner.x * ppu) / ppu - corner.x;
                    instance.origin.y += std::round(corner.y * ppu) / ppu - corner.y;
                }
                state.gathered.pushBack(gathered);
            }
        },
        &gathering);
    ecs::destroyQuery(query);
    gatherTilemaps(*context.world, gathering.pixel, gathering.pixelsPerUnit, gathering.behind);
    expireTextures();

    // Behind the 3D first, then layer, then order in layer, then (where the
    // layer y-sorts) higher first, so lower on screen draws on top; the
    // entity breaks ties, so equal sprites keep their order frame to frame.
    const uint32_t ySort = render2d::ySortLayers();
    std::sort(state.gathered.begin(), state.gathered.end(), [ySort](const Gathered& a, const Gathered& b) {
        if (a.behind != b.behind)
        {
            return a.behind;
        }
        if (a.layer != b.layer)
        {
            return a.layer < b.layer;
        }
        if (a.order != b.order)
        {
            return a.order < b.order;
        }
        if (a.layer < 32 && (ySort >> a.layer & 1) != 0 && a.height != b.height)
        {
            return a.height > b.height;
        }
        return a.entity < b.entity;
    });
    for (const Gathered& gathered : state.gathered)
    {
        if (gathered.run == UINT32_MAX)
        {
            sorted.pushBack(gathered.instance);
            frame.behind += gathered.behind ? 1 : 0;
            continue;
        }
        // A run of tiles, each placed as its tilemap is.
        const TileRun& run = state.runs[gathered.run];
        for (uint32_t i = 0; i < run.count; ++i)
        {
            SpriteInstance tile = run.tiles[i];
            tile.axisX = gathered.instance.axisX;
            tile.axisY = gathered.instance.axisY;
            tile.origin = gathered.instance.origin;
            tile.tint = gathered.instance.tint;
            tile.entity = gathered.instance.entity;
            sorted.pushBack(tile);
        }
        frame.behind += gathered.behind ? run.count : 0;
    }
    if (!sorted.empty() &&
        uploadSprites(context.device, context.surface, frameSlotOf(context),
                      surfaceDescriptor(context, spritesDescriptor) + frameSlotOf(context), sorted) == nullptr)
    {
        sorted.clear();
        frame.behind = 0;
    }
    frame.count = static_cast<uint32_t>(sorted.size());
    return frame;
}

// The frame slot's upload buffer, holding the sprites, written into the
// surface's slot; null if it couldn't be made. The slot's previous frame is
// done with it by now, so it can be replaced.
slag::Buffer* uploadSprites(slag::GraphicsDevice& device, uint32_t surface, uint32_t frameSlot, uint32_t slot,
                            const Vector<SpriteInstance>& sprites)
{
    SurfaceBuffers& buffers = state.surfaces[surface - 1];
    slag::Buffer*& buffer = buffers.buffers[frameSlot];
    uint32_t& capacity = buffers.capacity[frameSlot];
    if (buffer == nullptr || capacity < sprites.size())
    {
        const uint32_t wanted = std::max<uint32_t>(256, static_cast<uint32_t>(sprites.size()) * 3 / 2);
        if (buffer != nullptr)
        {
            device.destroy(buffer);
        }
        slag::Result<slag::Buffer*> created =
            device.createBuffer({.size = uint64_t{wanted} * sizeof(SpriteInstance),
                                 .placement = slag::Placement::automatic(slag::MemoryKind::Upload),
                                 .debugName = "crucible sprites"});
        buffer = created ? created.value() : nullptr;
        capacity = created ? wanted : 0;
        if (buffer == nullptr)
        {
            return nullptr;
        }
    }
    std::memcpy(buffer->mappedData(), sprites.data(), sprites.size() * sizeof(SpriteInstance));
    device.writeStructuredBuffer(slot, {.buffer = buffer, .count = capacity, .stride = sizeof(SpriteInstance)});
    return buffer;
}

void blendStraightAlpha(slag::GraphicsPipelineDesc& desc)
{
    desc.blend.targets[0] = {.blendEnable = true,
                             .srcColorFactor = slag::BlendFactor::SrcAlpha,
                             .destColorFactor = slag::BlendFactor::InvSrcAlpha,
                             .srcAlphaFactor = slag::BlendFactor::Zero,
                             .destAlphaFactor = slag::BlendFactor::One};
}

} // namespace

Result<void> prepareSprites(slag::GraphicsDevice& device, void*)
{
    const slag::PixelFormat formats[] = {backbufferFormat};
    slag::GraphicsPipelineDesc desc{
        .rasterizer = {.cullMode = slag::CullMode::None}, .colorFormats = formats, .debugName = "Sprites"};
    blendStraightAlpha(desc);
    Result<slag::Pipeline*> pipeline = createStepPipeline(device, "sprite", "spriteVertex", "spritePixel", desc);
    if (!pipeline)
    {
        return pipeline.status();
    }
    state.pipeline = pipeline.value();
    // The Pick step's targets: entity IDs and depths, without its depth
    // buffer (sprites draw over the meshes, as in the frame).
    const slag::PixelFormat pickFormats[] = {slag::PixelFormat::R32Uint, slag::PixelFormat::R32Float};
    Result<slag::Pipeline*> pick = createStepPipeline(
        device, "sprite", "spriteVertex", "spritePickPixel",
        {.rasterizer = {.cullMode = slag::CullMode::None}, .colorFormats = pickFormats, .debugName = "SpritePick"});
    if (!pick)
    {
        return pick.status();
    }
    state.pickPipeline = pick.value();
    // A pixel camera's small images: premultiplied, so their alpha is how
    // much the sprites covered and the composite blends them over as is.
    slag::GraphicsPipelineDesc pixel{
        .rasterizer = {.cullMode = slag::CullMode::None}, .colorFormats = formats, .debugName = "PixelSprites"};
    pixel.blend.targets[0] = {.blendEnable = true,
                              .srcColorFactor = slag::BlendFactor::SrcAlpha,
                              .destColorFactor = slag::BlendFactor::InvSrcAlpha,
                              .srcAlphaFactor = slag::BlendFactor::One,
                              .destAlphaFactor = slag::BlendFactor::InvSrcAlpha};
    Result<slag::Pipeline*> pixelPipeline = createStepPipeline(device, "sprite", "spriteVertex", "spritePixel", pixel);
    if (!pixelPipeline)
    {
        return pixelPipeline.status();
    }
    state.pixelPipeline = pixelPipeline.value();
    slag::GraphicsPipelineDesc composite{
        .rasterizer = {.cullMode = slag::CullMode::None}, .colorFormats = formats, .debugName = "PixelComposite"};
    composite.blend.targets[0] = {.blendEnable = true,
                                  .srcColorFactor = slag::BlendFactor::One,
                                  .destColorFactor = slag::BlendFactor::InvSrcAlpha,
                                  .srcAlphaFactor = slag::BlendFactor::Zero,
                                  .destAlphaFactor = slag::BlendFactor::One};
    Result<slag::Pipeline*> compositePipeline =
        createStepPipeline(device, "pixelComposite", "compositeVertex", "compositePixel", composite);
    if (!compositePipeline)
    {
        return compositePipeline.status();
    }
    state.compositePipeline = compositePipeline.value();
    return {};
}

void releaseSprites(slag::GraphicsDevice& device, void*)
{
    for (SurfaceBuffers& buffers : state.surfaces)
    {
        for (slag::Buffer*& buffer : buffers.buffers)
        {
            if (buffer != nullptr)
            {
                device.destroy(buffer);
                buffer = nullptr;
            }
        }
        buffers.capacity = {};
    }
    for (slag::Pipeline** pipeline :
         {&state.pipeline, &state.pickPipeline, &state.pixelPipeline, &state.compositePipeline})
    {
        if (*pipeline != nullptr)
        {
            device.destroy(*pipeline);
            *pipeline = nullptr;
        }
    }
    for (auto& entry : state.textures)
    {
        if (entry.value.handle.generation != 0)
        {
            releaseTexture(entry.value.handle);
        }
    }
    state.textures.clear();
    state.frames = {};
    freeTilemaps();
}

namespace {

// One pass drawing sprites [first, first + count) with `pipeline` and the
// frame's view into `target` (cleared first if `clear`).
void addSpritePass(RenderStepContext& context, const char* name, slag::rendergraph::RenderGraphTexture target,
                   bool clear, slag::Pipeline* pipeline, const FrameSprites& frame, uint32_t first, uint32_t count)
{
    SpriteConstants constants{.sprites = surfaceDescriptor(context, spritesDescriptor) + frameSlotOf(context),
                              .first = first,
                              .viewProjection = frame.viewProjection};
    context.graph.addPass(
        name, slag::CommandQueueType::Graphics,
        [&](slag::rendergraph::PassBuilder& pass) {
            pass.renderTarget(target, clear ? slag::LoadOp::Clear : slag::LoadOp::Load, slag::StoreOp::Store, {});
        },
        [constants, pipeline, count](slag::rendergraph::PassContext&, slag::CommandBuffer* commands) {
            if (count == 0)
            {
                return;
            }
            commands->bindPipeline(pipeline);
            commands->pushConstants(&constants, sizeof(constants));
            commands->draw(6, count);
        });
}

// A pixel camera's small image scaled up over the backbuffer; with `depth`,
// only where the 3D drew nothing; with `bars`, black around it.
void addComposite(RenderStepContext& context, const char* name, slag::rendergraph::RenderGraphTexture source,
                  uint32_t sourceSlot, slag::rendergraph::RenderGraphTexture depth, bool bars,
                  const FrameSprites& frame)
{
    const slag::rendergraph::RenderGraphTexture backbuffer = context.texture(backbufferKey());
    const uint32_t frameSlot = frameSlotOf(context);
    const uint32_t samples = msaaSamples();
    const bool masked = depth.index != UINT32_MAX;
    CompositeConstants constants{
        .source = surfaceDescriptor(context, sourceSlot) + frameSlot,
        .depth = masked ? surfaceDescriptor(context, pixelDepthDescriptor) + frameSlot : 0,
        .depthSamples = samples,
        .bars = bars ? 1u : 0u,
        .sourceSize = {static_cast<float>(frame.camera.width), static_cast<float>(frame.camera.height)},
        .offset = frame.offset,
        .scale = frame.scale,
    };
    slag::GraphicsDevice* device = &context.device;
    context.graph.addPass(
        name, slag::CommandQueueType::Graphics,
        [&](slag::rendergraph::PassBuilder& pass) {
            pass.use(source, slag::rendergraph::Usage::ShaderRead);
            if (masked)
            {
                pass.use(depth, slag::rendergraph::Usage::ShaderRead);
            }
            pass.renderTarget(backbuffer, slag::LoadOp::Load, slag::StoreOp::Store);
        },
        [=](slag::rendergraph::PassContext& pass, slag::CommandBuffer* commands) {
            // The transient images differ from frame to frame, so their
            // slots are written while recording.
            device->writeTexture(constants.source, {.texture = pass.texture(source)});
            if (masked)
            {
                device->writeTexture(constants.depth, {.texture = pass.texture(depth),
                                                       .viewType = samples > 1 ? slag::TextureViewType::Texture2DMS
                                                                               : slag::TextureViewType::Texture2D});
            }
            commands->bindPipeline(state.compositePipeline);
            commands->pushConstants(&constants, sizeof(constants));
            commands->draw(3, 1);
        });
}

} // namespace

void addSprites(RenderStepContext& context, void*)
{
    if (context.surface < 1 || context.surface > state.surfaces.size() || state.pipeline == nullptr)
    {
        return;
    }
    const FrameSprites& frame = frameSprites(context);
    if (!frame.pixel)
    {
        if (frame.count > 0)
        {
            addSpritePass(context, "Sprites", context.texture(backbufferKey()), false, state.pipeline, frame, 0,
                          frame.count);
        }
        return;
    }
    // Through a pixel camera: the sprites behind the 3D and those in front
    // of it each into an image at its resolution, scaled up over the
    // finished image (the front one always, for its bars).
    auto small = [&](const char* name) {
        return context.graph.createTransientTexture2D({.format = backbufferFormat,
                                                       .width = frame.camera.width,
                                                       .height = frame.camera.height,
                                                       .usage = slag::TextureUsage::RenderTarget,
                                                       .debugName = name});
    };
    if (frame.behind > 0)
    {
        const slag::rendergraph::RenderGraphTexture behind = small("PixelBehind");
        addSpritePass(context, "PixelSpritesBehind", behind, true, state.pixelPipeline, frame, 0, frame.behind);
        addComposite(context, "PixelCompositeBehind", behind, pixelBehindDescriptor, context.texture(sceneDepthKey()),
                     false, frame);
    }
    const slag::rendergraph::RenderGraphTexture front = small("PixelFront");
    addSpritePass(context, "PixelSprites", front, true, state.pixelPipeline, frame, frame.behind,
                  frame.count - frame.behind);
    addComposite(context, "PixelComposite", front, pixelFrontDescriptor, {},
                 frame.camera.fit == render2d::PixelFit::Exact, frame);
}

void addSpritePicks(RenderStepContext& context, slag::rendergraph::RenderGraphTexture ids,
                    slag::rendergraph::RenderGraphTexture depths)
{
    if (context.surface < 1 || context.surface > state.surfaces.size() || state.pickPipeline == nullptr)
    {
        return;
    }
    // Picks are the editor's scene view's, which draws sprites directly.
    const FrameSprites& frame = frameSprites(context);
    if (frame.count == 0 || frame.pixel)
    {
        return;
    }
    const SpriteConstants constants{.sprites = surfaceDescriptor(context, spritesDescriptor) + frameSlotOf(context),
                                    .viewProjection = frame.viewProjection};
    const uint32_t count = frame.count;
    context.graph.addPass(
        "SpritePick", slag::CommandQueueType::Graphics,
        [&](slag::rendergraph::PassBuilder& pass) {
            pass.renderTarget(ids, slag::LoadOp::Load, slag::StoreOp::Store);
            pass.renderTarget(depths, slag::LoadOp::Load, slag::StoreOp::Store);
        },
        [constants, count](slag::rendergraph::PassContext&, slag::CommandBuffer* commands) {
            commands->bindPipeline(state.pickPipeline);
            commands->pushConstants(&constants, sizeof(constants));
            commands->draw(6, count);
        });
}

// The load starter (ADR 0099): every sprite's texture, and every tilemap's
// map and texture, acquired, counting those still loading.
uint32_t startSpriteLoads(ecs::World& world)
{
    if (ecs::componentIdOf<Sprite> == ecs::invalidComponent)
    {
        return 0;
    }
    const ecs::ComponentId spriteOnly[] = {ecs::componentIdOf<Sprite>};
    ecs::Query* query = ecs::createQuery(world, {.required = spriteOnly});
    uint32_t waiting = 0;
    ecs::forEachChunk(
        *query,
        [](const ecs::ChunkView& view, void* data) {
            for (const Sprite& sprite : ecs::readColumn<Sprite>(view))
            {
                const TextureHandle handle = resolve(sprite.texture);
                *static_cast<uint32_t*>(data) += handle.generation != 0 && isTextureLoading(handle) ? 1 : 0;
            }
        },
        &waiting);
    ecs::destroyQuery(query);
    // Tilemaps: the map, then its tilesets' textures.
    if (ecs::componentIdOf<render2d::Tilemap> != ecs::invalidComponent)
    {
        const ecs::ComponentId mapOnly[] = {ecs::componentIdOf<render2d::Tilemap>};
        query = ecs::createQuery(world, {.required = mapOnly});
        ecs::forEachChunk(
            *query,
            [](const ecs::ChunkView& view, void* data) {
                for (const render2d::Tilemap& tilemap : ecs::readColumn<render2d::Tilemap>(view))
                {
                    const TilemapCache* map = tilemapOf(tilemap.map);
                    bool loads = tilemapLoading(tilemap.map);
                    for (size_t i = 0; map != nullptr && i < map->textures.size(); ++i)
                    {
                        const TextureHandle texture = resolve(map->textures[i]);
                        loads = loads || (texture.generation != 0 && isTextureLoading(texture));
                    }
                    *static_cast<uint32_t*>(data) += loads ? 1 : 0;
                }
            },
            &waiting);
        ecs::destroyQuery(query);
    }
    return waiting;
}

} // namespace crucible::render

namespace crucible::render2d {

void registerSprite()
{
    ecs::registerComponent<Sprite>("Crucible.Render2D.Sprite");
    ecs::registerComponent<PixelCamera>("Crucible.Render2D.PixelCamera");
    ecs::registerComponent<Tilemap>("Crucible.Render2D.Tilemap");
    ecs::registerComponent<TilemapLayers>("Crucible.Render2D.TilemapLayers");
    registerSpriteAnimator();
    static const bool starter = [] {
        assets::registerLoadStarter(render::startSpriteLoads);
        return true;
    }();
    (void)starter;
}

} // namespace crucible::render2d

namespace crucible::render2d {

bool reloadTilemap(assets::AssetId map)
{
    render::TilemapCache** found = render::state.tilemaps.find(map);
    if (found == nullptr)
    {
        return false;
    }
    const bool loaded = !(*found)->loading;
    destroy(MemoryTag::Render, *found);
    render::state.tilemaps.erase(map);
    return loaded;
}

} // namespace crucible::render2d
