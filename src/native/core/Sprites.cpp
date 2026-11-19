// Sprites (ADR 0104): the component, the sorting layers that y-sort, the
// textures sprites show (acquired on first use, released when unused), and
// the Sprites render step: each frame the world's sprites gathered, sorted
// by sorting layer, order in layer, and (where the layer y-sorts) height,
// and drawn in one instanced draw over the finished image. The editor's
// Pick step draws them again into its entity IDs.

#include "RenderInternal.h"

#include <crucible/AssetLoads.h>
#include <crucible/Ecs.h>
#include <crucible/HashMap.h>
#include <crucible/Log.h>
#include <crucible/Sprite.h>
#include <crucible/Transform.h>

#include <slag/Slag.h>

#include <algorithm>
#include <array>
#include <cstring>

namespace crucible::render2d {

namespace {

uint32_t ySorted = 0;

} // namespace

void setYSortLayers(uint32_t mask)
{
    ySorted = mask;
}

uint32_t ySortLayers()
{
    return ySorted;
}

} // namespace crucible::render2d

namespace crucible::render {

namespace {

using assets::AssetId;
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

// A gathered sprite, before sorting.
struct Gathered
{
    uint32_t layer;
    int32_t order;
    float height; // the pivot's y, for layers that y-sort
    uint32_t entity;
    SpriteInstance instance;
};

// Each surface's upload buffers, one per frame slot, grown as needed.
struct SurfaceBuffers
{
    std::array<slag::Buffer*, framesInFlight> buffers{};
    std::array<uint32_t, framesInFlight> capacity{};
};

// The view's constant buffer size, as RenderSteps.cpp binds it.
uint32_t viewSize()
{
    return (sizeof(ViewConstants) + slag::alignment::constantBuffer - 1) / slag::alignment::constantBuffer *
           slag::alignment::constantBuffer;
}

struct State
{
    HashMap<AssetId, CachedTexture, AssetIdHash> textures{MemoryTag::Render};
    uint64_t gathers = 0;
    Vector<Gathered> gathered{MemoryTag::Render};
    Vector<AssetId> expired{MemoryTag::Render};
    // The last gather's sorted sprites, by surface and frame (the Sprites
    // and Pick steps of one frame share them).
    std::array<Vector<SpriteInstance>, 8> sorted{
        Vector<SpriteInstance>(MemoryTag::Render), Vector<SpriteInstance>(MemoryTag::Render),
        Vector<SpriteInstance>(MemoryTag::Render), Vector<SpriteInstance>(MemoryTag::Render),
        Vector<SpriteInstance>(MemoryTag::Render), Vector<SpriteInstance>(MemoryTag::Render),
        Vector<SpriteInstance>(MemoryTag::Render), Vector<SpriteInstance>(MemoryTag::Render)};
    std::array<uint64_t, 8> sortedFrame{};
    std::array<SurfaceBuffers, 8> surfaces{};
    slag::Pipeline* pipeline = nullptr;
    slag::Pipeline* pickPipeline = nullptr;
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

// The surface's sprites this frame, gathered, sorted, and uploaded once for
// its steps, on the main thread (passes record on workers): how many there
// are, 0 if none (or their buffer couldn't be made).
uint32_t frameSprites(const RenderStepContext& context)
{
    const uint32_t surface = context.surface - 1;
    Vector<SpriteInstance>& sorted = state.sorted[surface];
    if (state.sortedFrame[surface] == context.frame)
    {
        return static_cast<uint32_t>(sorted.size());
    }
    state.sortedFrame[surface] = context.frame;
    sorted.clear();
    state.gathered.clear();
    if (context.world == nullptr || ecs::componentIdOf<Sprite> == ecs::invalidComponent)
    {
        return 0;
    }
    ++state.gathers;
    const ecs::ComponentId required[] = {ecs::componentIdOf<Sprite>, ecs::componentIdOf<WorldTransform>};
    ecs::Query* query = ecs::createQuery(*context.world, {.required = required});
    ecs::forEachChunk(
        *query,
        [](const ecs::ChunkView& view, void*) {
            const std::span<const Sprite> sprites = ecs::readColumn<Sprite>(view);
            const std::span<const WorldTransform> transforms = ecs::readColumn<WorldTransform>(view);
            const ecs::Entity* entities = ecs::entitiesOf(view);
            for (size_t i = 0; i < sprites.size(); ++i)
            {
                Gathered gathered{.layer = sprites[i].sortingLayer,
                                  .order = sprites[i].orderInLayer,
                                  .height = transforms[i].matrix.rows[3].y,
                                  .entity = ecs::indexOf(entities[i])};
                if (instanceOf(sprites[i], transforms[i], gathered.entity, gathered.instance))
                {
                    state.gathered.pushBack(gathered);
                }
            }
        },
        nullptr);
    ecs::destroyQuery(query);
    expireTextures();

    // Layer first, then order in layer, then (where the layer y-sorts)
    // higher first, so lower on screen draws on top; the entity breaks ties,
    // so equal sprites keep their order from frame to frame.
    const uint32_t ySort = render2d::ySortLayers();
    std::sort(state.gathered.begin(), state.gathered.end(), [ySort](const Gathered& a, const Gathered& b) {
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
        sorted.pushBack(gathered.instance);
    }
    if (!sorted.empty() &&
        uploadSprites(context.device, context.surface, frameSlotOf(context),
                      surfaceDescriptor(context, spritesDescriptor) + frameSlotOf(context), sorted) == nullptr)
    {
        sorted.clear();
    }
    return static_cast<uint32_t>(sorted.size());
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
    for (slag::Pipeline** pipeline : {&state.pipeline, &state.pickPipeline})
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
    state.sortedFrame = {};
}

void addSprites(RenderStepContext& context, void*)
{
    if (context.surface < 1 || context.surface > state.surfaces.size() || state.pipeline == nullptr)
    {
        return;
    }
    const uint32_t count = frameSprites(context);
    if (count == 0)
    {
        return;
    }
    const slag::rendergraph::RenderGraphTexture backbuffer = context.texture(backbufferKey());
    const slag::rendergraph::RenderGraphBuffer view = context.buffer(viewKey());
    const uint32_t slot = surfaceDescriptor(context, spritesDescriptor) + frameSlotOf(context);
    context.graph.addPass(
        "Sprites", slag::CommandQueueType::Graphics,
        [&](slag::rendergraph::PassBuilder& pass) {
            pass.use(view, slag::rendergraph::Usage::ConstantBuffer);
            pass.renderTarget(backbuffer, slag::LoadOp::Load, slag::StoreOp::Store);
        },
        [=](slag::rendergraph::PassContext& pass, slag::CommandBuffer* commands) {
            const uint32_t constants[4] = {slot, 0, 0, 0};
            commands->bindPipeline(state.pipeline);
            commands->setConstantBuffer(pass.buffer(view)->gpuAddress(), viewSize());
            commands->pushConstants(constants, sizeof(constants));
            commands->draw(6, count);
        });
}

void addSpritePicks(RenderStepContext& context, slag::rendergraph::RenderGraphTexture ids,
                    slag::rendergraph::RenderGraphTexture depths)
{
    if (context.surface < 1 || context.surface > state.surfaces.size() || state.pickPipeline == nullptr)
    {
        return;
    }
    const uint32_t count = frameSprites(context);
    if (count == 0)
    {
        return;
    }
    const slag::rendergraph::RenderGraphBuffer view = context.buffer(viewKey());
    const uint32_t slot = surfaceDescriptor(context, spritesDescriptor) + frameSlotOf(context);
    context.graph.addPass(
        "SpritePick", slag::CommandQueueType::Graphics,
        [&](slag::rendergraph::PassBuilder& pass) {
            pass.use(view, slag::rendergraph::Usage::ConstantBuffer);
            pass.renderTarget(ids, slag::LoadOp::Load, slag::StoreOp::Store);
            pass.renderTarget(depths, slag::LoadOp::Load, slag::StoreOp::Store);
        },
        [=](slag::rendergraph::PassContext& pass, slag::CommandBuffer* commands) {
            const uint32_t constants[4] = {slot, 0, 0, 0};
            commands->bindPipeline(state.pickPipeline);
            commands->setConstantBuffer(pass.buffer(view)->gpuAddress(), viewSize());
            commands->pushConstants(constants, sizeof(constants));
            commands->draw(6, count);
        });
}

// The load starter (ADR 0099): every sprite's texture acquired, counting
// those still loading.
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
    return waiting;
}

} // namespace crucible::render

namespace crucible::render2d {

void registerSprite()
{
    ecs::registerComponent<Sprite>("Crucible.Render2D.Sprite");
    static const bool starter = [] {
        assets::registerLoadStarter(render::startSpriteLoads);
        return true;
    }();
    (void)starter;
}

} // namespace crucible::render2d
