// Shadows (ADR 0054): the sun's cascades, fitted to spheres around slices of
// the view and snapped to whole texels so they don't shimmer, drawn every
// frame; and point and spot lights' tiles in the surface's shadow atlas, kept
// from frame to frame and drawn again only when the light, or a caster in
// it, changed.

#include "RenderInternal.h"

#include <crucible/Lights.h>
#include <crucible/Render.h>

#include <slag/Slag.h>

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>

namespace crucible::render {

namespace {

// Splits between the cascades: this much of the logarithmic split (even
// detail on screen), the rest the uniform one (Zhang's practical split).
constexpr float logarithmicSplit = 0.75f;
// Each cascade blends into the next over its last tenth (shadows.slang).
constexpr float cascadeBlend = 0.1f;
// Texels around each view the filter may read past its edge: views are
// widened by them, and lookups kept inside them (shadows.slang).
constexpr float filterBorder = 4.0f;
// The atlas is cut into cells; a tile is 1, 2, or 4 cells square.
constexpr uint32_t atlasCells = 32;
constexpr uint32_t largestTileCells = 4;
constexpr uint32_t maxShadowViews = shadowCascadeCount + maxShadowedLights * 6;

ShadowStats stats{};
uint32_t mapSize = 2048;
uint32_t atlasSize = 4096;
slag::PixelFormat format = slag::PixelFormat::Undefined;

bool validSize(uint32_t size)
{
    return size >= 256 && size <= 8192 && (size & (size - 1)) == 0;
}

uint32_t viewConstantsStride()
{
    return (sizeof(ViewConstants) + slag::alignment::constantBuffer - 1) / slag::alignment::constantBuffer *
           slag::alignment::constantBuffer;
}

Vector3 upFor(Vector3 forward)
{
    return std::fabs(forward.y) > 0.99f ? Vector3{0, 0, 1} : Vector3{0, 1, 0};
}

// A point light's cube faces, in LightData::shadowView's order.
constexpr Vector3 cubeFaces[6] = {{1, 0, 0}, {-1, 0, 0}, {0, 1, 0}, {0, -1, 0}, {0, 0, 1}, {0, 0, -1}};

// A light given atlas tiles, kept while it stays shadowed.
struct CachedLight
{
    ecs::Entity entity{};
    bool point = false;
    Vector3 position{};
    Vector3 axis{};
    float range = 0;
    float outerConeAngle = 0;
    uint32_t cells = 0; // each tile's size
    uint32_t tileCount = 0;
    uint32_t tileX[6]{}; // in cells
    uint32_t tileY[6]{};
    // Whether each tile was drawn, and its casters' hash then.
    bool drawn[6]{};
    uint64_t hash[6]{};
    bool chosen = false; // this frame
};

// One shadow view this frame, beside its ShadowCullView.
struct ViewInfo
{
    bool cascade;
    uint32_t layer;
    uint32_t light; // atlas: into the cache
    uint32_t face;
    slag::Rect rect;
    Matrix4x4 viewProjection;
};

} // namespace

struct ShadowState
{
    uint32_t descriptorBase = 0;
    std::array<slag::Buffer*, framesInFlight> views{};     // StructuredBuffer<ShadowView>
    std::array<slag::Buffer*, framesInFlight> constants{}; // each pass's ViewConstants
    slag::Texture* atlas = nullptr;
    slag::AttachmentView* atlasView = nullptr;
    uint32_t atlasMade = 0; // its size
    slag::TextureLayout atlasLayout = slag::TextureLayout::Undefined;
    std::array<uint8_t, atlasCells * atlasCells> used{};
    Vector<CachedLight> lights{MemoryTag::Render};
    Vector<ShadowCullView> cullViews{MemoryTag::Render};
    Vector<ViewInfo> infos{MemoryTag::Render};
    Vector<ShadowPass> cascades{MemoryTag::Render};
    Vector<ShadowPass> tiles{MemoryTag::Render};
    ShadowFrame frame{};
    ShadowRequests requests;
    ecs::World* world = nullptr;
};

namespace {

void destroyAtlas(ShadowState& state)
{
    slag::GraphicsDevice& device = *renderDevice();
    if (state.atlasView != nullptr)
    {
        device.destroy(state.atlasView);
        state.atlasView = nullptr;
    }
    if (state.atlas != nullptr)
    {
        device.destroy(state.atlas);
        state.atlas = nullptr;
    }
    state.atlasMade = 0;
    state.atlasLayout = slag::TextureLayout::Undefined;
    state.used.fill(0);
    state.lights.clear();
}

bool ensureAtlas(ShadowState& state)
{
    if (state.atlas != nullptr && state.atlasMade == atlasSize)
    {
        return true;
    }
    // setShadowAtlasSize waited for the GPU.
    destroyAtlas(state);
    slag::GraphicsDevice& device = *renderDevice();
    slag::Result<slag::Texture*> texture =
        device.createTexture2D({.format = format,
                                .width = atlasSize,
                                .height = atlasSize,
                                .usage = slag::TextureUsage::DepthStencil,
                                .placement = slag::Placement::automatic(slag::MemoryKind::GpuOnly),
                                .debugName = "ShadowAtlas"});
    if (!texture)
    {
        return false;
    }
    slag::Result<slag::AttachmentView*> view =
        device.createAttachmentView({.texture = texture.value(), .debugName = "ShadowAtlas"});
    if (!view)
    {
        device.destroy(texture.value());
        return false;
    }
    state.atlas = texture.value();
    state.atlasView = view.value();
    state.atlasMade = atlasSize;
    device.writeTexture(state.descriptorBase + shadowAtlasDescriptor,
                        {.texture = state.atlas, .plane = slag::TexturePlane::Depth});
    return true;
}

void release(ShadowState& state, CachedLight& light)
{
    for (uint32_t t = 0; t < light.tileCount; ++t)
    {
        for (uint32_t y = 0; y < light.cells; ++y)
        {
            for (uint32_t x = 0; x < light.cells; ++x)
            {
                state.used[(light.tileY[t] + y) * atlasCells + light.tileX[t] + x] = 0;
            }
        }
    }
    light.tileCount = 0;
}

// A free square of `cells`, aligned to its size so tiles pack without
// fragmenting much.
bool allocateTile(ShadowState& state, uint32_t cells, uint32_t& outX, uint32_t& outY)
{
    for (uint32_t y = 0; y + cells <= atlasCells; y += cells)
    {
        for (uint32_t x = 0; x + cells <= atlasCells; x += cells)
        {
            bool free = true;
            for (uint32_t dy = 0; dy < cells && free; ++dy)
            {
                for (uint32_t dx = 0; dx < cells && free; ++dx)
                {
                    free = state.used[(y + dy) * atlasCells + x + dx] == 0;
                }
            }
            if (free)
            {
                for (uint32_t dy = 0; dy < cells; ++dy)
                {
                    for (uint32_t dx = 0; dx < cells; ++dx)
                    {
                        state.used[(y + dy) * atlasCells + x + dx] = 1;
                    }
                }
                outX = x;
                outY = y;
                return true;
            }
        }
    }
    return false;
}

bool allocateTiles(ShadowState& state, CachedLight& light, uint32_t cells)
{
    const uint32_t count = light.point ? 6 : 1;
    for (; cells >= 1; cells /= 2)
    {
        light.cells = cells;
        light.tileCount = 0;
        while (light.tileCount < count &&
               allocateTile(state, cells, light.tileX[light.tileCount], light.tileY[light.tileCount]))
        {
            ++light.tileCount;
        }
        if (light.tileCount == count)
        {
            for (bool& drawn : light.drawn)
            {
                drawn = false;
            }
            return true;
        }
        release(state, light);
    }
    return false;
}

bool same(Vector3 a, Vector3 b)
{
    return std::fabs(a.x - b.x) <= 1e-5f && std::fabs(a.y - b.y) <= 1e-5f && std::fabs(a.z - b.z) <= 1e-5f;
}

void prepareCascades(ShadowState& state, const ShadowRequests& requests, ViewConstants& view, ShadowView* views)
{
    fitCascades(view, requests.sunDirection, requests.sunDistance, mapSize, views);
    for (uint32_t i = 0; i < shadowCascadeCount; ++i)
    {
        // Casters between the light and the cascade still cast: the near
        // plane (z >= 0, conventional depth) is left out, and the shadow
        // pipelines clamp their depth.
        state.cullViews.pushBack({.planes = frustumPlanes(views[i].viewProjection),
                                  .planeCount = 5,
                                  .orthographic = true,
                                  .eye = {},
                                  .pixelsPerMeter = 1.0f / views[i].normalOffset});
        state.infos.pushBack({true, i, 0, 0, {}, views[i].viewProjection});
    }
}

// Point and spot lights: the most important (largest on screen) get tiles,
// sized by how large they are on screen.
void prepareLocalLights(ShadowState& state, const ShadowRequests& requests, LightData* lights,
                        const ViewConstants& view, ShadowView* views)
{
    struct Candidate
    {
        const ShadowLight* light;
        float pixels;
    };
    Candidate candidates[maxVisibleLights];
    uint32_t count = 0;
    const bool perspective = view.projection.rows[2].w == 1.0f;
    const float pixelsPerMeter = view.projection.rows[1].y * view.viewportSize.y * 0.5f;
    for (const ShadowLight& light : requests.lights)
    {
        float pixels = pixelsPerMeter * light.range * 2;
        if (perspective)
        {
            const float d = length(light.position - view.cameraPosition);
            pixels = d <= light.range ? 1e9f : pixels / d;
        }
        candidates[count++] = {&light, pixels};
    }
    std::sort(candidates, candidates + count, [](const Candidate& a, const Candidate& b) {
        return a.pixels != b.pixels ? a.pixels > b.pixels : a.light->entity.value < b.light->entity.value;
    });
    count = std::min(count, maxShadowedLights);

    for (CachedLight& cached : state.lights)
    {
        cached.chosen = false;
    }
    const uint32_t cellTexels = state.atlasMade / atlasCells;
    uint32_t wanted[maxShadowedLights];
    int32_t cacheOf[maxShadowedLights];
    for (uint32_t c = 0; c < count; ++c)
    {
        const ShadowLight& light = *candidates[c].light;
        uint32_t cells = 1;
        while (cells < largestTileCells && static_cast<float>(cells * cellTexels) < candidates[c].pixels)
        {
            cells *= 2;
        }
        wanted[c] = cells;
        cacheOf[c] = -1;
        for (size_t l = 0; l < state.lights.size(); ++l)
        {
            if (state.lights[l].entity == light.entity && state.lights[l].point == light.point)
            {
                cacheOf[c] = static_cast<int32_t>(l);
                state.lights[l].chosen = true;
            }
        }
    }
    // Lights no longer shadowed give their tiles back; then lights whose
    // size changed (growing, or shrinking to a quarter or less, so a light
    // near a boundary doesn't flip back and forth).
    for (size_t l = state.lights.size(); l-- > 0;)
    {
        if (!state.lights[l].chosen)
        {
            release(state, state.lights[l]);
            state.lights[l] = state.lights[state.lights.size() - 1];
            state.lights.popBack();
            for (uint32_t c = 0; c < count; ++c)
            {
                if (cacheOf[c] == static_cast<int32_t>(state.lights.size()))
                {
                    cacheOf[c] = static_cast<int32_t>(l);
                }
            }
        }
    }
    for (uint32_t c = 0; c < count; ++c)
    {
        if (cacheOf[c] >= 0)
        {
            CachedLight& cached = state.lights[static_cast<size_t>(cacheOf[c])];
            if (wanted[c] > cached.cells || wanted[c] * 2 < cached.cells)
            {
                release(state, cached);
            }
            else
            {
                wanted[c] = cached.cells;
            }
        }
    }
    // Then tiles for those without, most important first.
    uint32_t atlasViews = 0;
    for (uint32_t c = 0; c < count; ++c)
    {
        const ShadowLight& light = *candidates[c].light;
        if (cacheOf[c] < 0)
        {
            CachedLight added;
            added.entity = light.entity;
            added.point = light.point;
            added.chosen = true;
            state.lights.pushBack(added);
            cacheOf[c] = static_cast<int32_t>(state.lights.size() - 1);
        }
        CachedLight& cached = state.lights[static_cast<size_t>(cacheOf[c])];
        if (cached.tileCount == 0 && !allocateTiles(state, cached, wanted[c]))
        {
            continue; // the atlas is full: no shadow this frame
        }
        if (!same(cached.position, light.position) || !same(cached.axis, light.axis) || cached.range != light.range ||
            cached.outerConeAngle != light.outerConeAngle)
        {
            cached.position = light.position;
            cached.axis = light.axis;
            cached.range = light.range;
            cached.outerConeAngle = light.outerConeAngle;
            for (bool& drawn : cached.drawn)
            {
                drawn = false;
            }
        }

        const uint32_t texels = cached.cells * cellTexels;
        const float tileSize = static_cast<float>(texels);
        // Widened by the filter's border on each side.
        const float widen = tileSize / (tileSize - 2 * filterBorder);
        const float tangent =
            (light.point ? 1.0f : std::tan(std::clamp(light.outerConeAngle, 0.1f, 2.96f) * 0.5f)) * widen;
        const float nearPlane = std::max(light.range * 0.01f, 0.02f);
        const Matrix4x4 projection =
            Matrix4x4::perspectiveFieldOfView(2 * std::atan(tangent), 1, nearPlane, std::max(light.range, nearPlane * 2));
        // The cascades' ShadowViews always come first.
        lights[light.light].shadowView = shadowCascadeCount + atlasViews;
        for (uint32_t face = 0; face < cached.tileCount; ++face)
        {
            const Vector3 forward = light.point ? cubeFaces[face] : light.axis;
            const Matrix4x4 viewProjection =
                Matrix4x4::lookAt(light.position, light.position + forward, upFor(forward)) * projection;
            const uint32_t viewIndex = shadowCascadeCount + atlasViews++;
            const slag::Rect rect{static_cast<int32_t>(cached.tileX[face] * cellTexels),
                                  static_cast<int32_t>(cached.tileY[face] * cellTexels), texels, texels};
            const float atlas = static_cast<float>(state.atlasMade);
            views[viewIndex] = {.viewProjection = viewProjection,
                                .rect = {static_cast<float>(rect.x) / atlas, static_cast<float>(rect.y) / atlas,
                                         tileSize / atlas, tileSize / atlas},
                                .texelSize = 1.0f / atlas,
                                .normalOffset = 0,
                                .normalOffsetPerMeter = 2 * tangent / tileSize,
                                .layer = 0};
            state.cullViews.pushBack({.planes = frustumPlanes(viewProjection),
                                      .planeCount = 6,
                                      .orthographic = false,
                                      .eye = light.position,
                                      .pixelsPerMeter = projection.rows[1].y * tileSize * 0.5f});
            state.infos.pushBack({false, 0, static_cast<uint32_t>(cacheOf[c]), face, rect, viewProjection});
        }
    }
}

} // namespace

// The sun's cascades: view-depth splits, then a sphere around each slice,
// snapped to texels in the light's view.
void fitCascades(ViewConstants& view, Vector3 sunDirection, float sunDistance, uint32_t size, ShadowView* views)
{
    const Matrix4x4& projection = view.projection;
    const bool perspective = projection.rows[2].w == 1.0f;
    const float nearPlane =
        perspective ? projection.rows[3].z : (1.0f - projection.rows[3].z) / std::min(projection.rows[2].z, -1e-6f);
    // Perspective: tangents of the half angles; orthographic: half sizes.
    const float halfX = 1.0f / projection.rows[0].x;
    const float halfY = 1.0f / projection.rows[1].y;
    const float distance = std::max(sunDistance, nearPlane * 2);

    float splits[shadowCascadeCount];
    for (uint32_t i = 0; i < shadowCascadeCount; ++i)
    {
        const float t = static_cast<float>(i + 1) / shadowCascadeCount;
        const float logarithmic = nearPlane * std::pow(distance / nearPlane, t);
        const float uniform = nearPlane + (distance - nearPlane) * t;
        splits[i] = logarithmicSplit * logarithmic + (1 - logarithmicSplit) * uniform;
    }
    splits[shadowCascadeCount - 1] = distance;
    view.cascadeSplits = {splits[0], splits[1], splits[2], splits[3]};

    const Vector3 cameraPosition = view.cameraPosition;
    const Vector3 cameraForward{view.inverseView.rows[2].x, view.inverseView.rows[2].y, view.inverseView.rows[2].z};
    const Vector3 lightForward = -sunDirection;
    const Matrix4x4 rotation = Matrix4x4::lookAt({0, 0, 0}, lightForward, upFor(lightForward));
    const float texels = static_cast<float>(size);
    for (uint32_t i = 0; i < shadowCascadeCount; ++i)
    {
        // Each slice starts where the previous cascade's blend does, so both
        // cover the blend (measured as shadows.slang does, from 0 for the
        // first cascade).
        const float start =
            i == 0 ? nearPlane : splits[i - 1] - (splits[i - 1] - (i == 1 ? 0.0f : splits[i - 2])) * cascadeBlend;
        const float end = splits[i];
        float center, radius;
        if (perspective)
        {
            // The smallest sphere around the slice's corners; its size
            // depends only on the projection, never on where the camera
            // looks, so its texels keep their size.
            const float k2 = halfX * halfX + halfY * halfY;
            center = std::min(end, (start + end) * 0.5f * (1 + k2));
            radius = std::sqrt((end - center) * (end - center) + end * end * k2);
        }
        else
        {
            center = (start + end) * 0.5f;
            const float half = (end - start) * 0.5f;
            radius = std::sqrt(half * half + halfX * halfX + halfY * halfY);
        }
        // Widened so the filter's border stays inside the map, and rounded
        // up so float noise can't change the texel size.
        radius = std::ceil(radius * texels / (texels - 2 * (filterBorder + 1)) * 64.0f) / 64.0f;
        const float texel = 2 * radius / texels;

        const Vector3 lightCenter = transformPoint(cameraPosition + cameraForward * center, rotation);
        const Vector3 snapped{std::floor(lightCenter.x / texel) * texel, std::floor(lightCenter.y / texel) * texel,
                              lightCenter.z - radius};
        const Matrix4x4 viewMatrix = rotation * Matrix4x4::translation(-snapped);
        const Matrix4x4 viewProjection = viewMatrix * Matrix4x4::orthographic(2 * radius, 2 * radius, 0, 2 * radius);

        views[i] = {.viewProjection = viewProjection,
                    .rect = {0, 0, 1, 1},
                    .texelSize = 1.0f / texels,
                    .normalOffset = texel,
                    .normalOffsetPerMeter = 0,
                    .layer = i};
    }
}

void chooseShadowFormat(slag::GraphicsDevice& device)
{
    constexpr slag::FormatSupport needed =
        slag::FormatSupport::Sampled | slag::FormatSupport::LinearFilter | slag::FormatSupport::DepthStencil;
    format = slag::PixelFormat::Undefined;
    for (slag::PixelFormat candidate : {slag::PixelFormat::D16Unorm, slag::PixelFormat::D32Float})
    {
        if ((device.formatSupport(candidate) & needed) == needed)
        {
            format = candidate;
            return;
        }
    }
    std::fprintf(stderr, "crucible: no depth format can be filtered here; shadows are off\n");
}

slag::PixelFormat shadowFormat()
{
    return format;
}

ShadowStats shadowStats()
{
    return stats;
}

Result<void> setShadowMapSize(uint32_t size)
{
    if (!validSize(size))
    {
        return Status::Unsupported;
    }
    mapSize = size; // the cascades are made every frame
    return {};
}

uint32_t shadowMapSize()
{
    return mapSize;
}

Result<void> setShadowAtlasSize(uint32_t size)
{
    if (!validSize(size))
    {
        return Status::Unsupported;
    }
    if (size != atlasSize && renderQueue() != nullptr)
    {
        // Surfaces make their atlases again, with the GPU done with the
        // old ones.
        (void)renderQueue()->waitIdle();
    }
    atlasSize = size;
    return {};
}

uint32_t shadowAtlasSize()
{
    return atlasSize;
}

ShadowState* createShadowState(uint32_t descriptorBase)
{
    auto* state = create<ShadowState>(MemoryTag::Render);
    state->descriptorBase = descriptorBase;
    slag::GraphicsDevice& device = *renderDevice();
    for (uint32_t slot = 0; slot < framesInFlight; ++slot)
    {
        slag::Result<slag::Buffer*> views =
            device.createBuffer({.size = uint64_t{maxShadowViews} * sizeof(ShadowView),
                                 .placement = slag::Placement::automatic(slag::MemoryKind::Upload),
                                 .debugName = "crucible shadow views"});
        slag::Result<slag::Buffer*> constants =
            device.createBuffer({.size = uint64_t{maxShadowViews} * viewConstantsStride(),
                                 .placement = slag::Placement::automatic(slag::MemoryKind::Upload),
                                 .debugName = "crucible shadow view constants"});
        if (views)
        {
            state->views[slot] = views.value();
        }
        if (constants)
        {
            state->constants[slot] = constants.value();
        }
        if (!views || !constants)
        {
            destroyShadowState(state);
            return nullptr;
        }
        device.writeStructuredBuffer(descriptorBase + shadowViewsDescriptor + slot,
                                     {.buffer = views.value(), .count = maxShadowViews, .stride = sizeof(ShadowView)});
    }
    return state;
}

void destroyShadowState(ShadowState* state)
{
    if (state == nullptr)
    {
        return;
    }
    destroyAtlas(*state);
    for (uint32_t slot = 0; slot < framesInFlight; ++slot)
    {
        if (state->views[slot] != nullptr)
        {
            renderDevice()->destroy(state->views[slot]);
        }
        if (state->constants[slot] != nullptr)
        {
            renderDevice()->destroy(state->constants[slot]);
        }
    }
    destroy(MemoryTag::Render, state);
}

Result<void> prepareShadows(ShadowState& state, ecs::World* world, const ShadowRequests& requests,
                            LightData* lights, uint32_t frameSlot, ViewConstants& view)
{
    state.cullViews.clear();
    state.infos.clear();
    view.cascadeSplits = {};
    view.shadowViews = state.descriptorBase + shadowViewsDescriptor + frameSlot;
    view.cascadeMap = 0;
    view.shadowAtlas = 0;
    if (world != state.world)
    {
        // Another world: nothing drawn for the last one applies.
        for (CachedLight& light : state.lights)
        {
            release(state, light);
        }
        state.lights.clear();
        state.world = world;
    }
    if (format == slag::PixelFormat::Undefined)
    {
        return {};
    }
    auto* views = static_cast<ShadowView*>(state.views[frameSlot]->mappedData());
    if (requests.sun)
    {
        prepareCascades(state, requests, view, views);
        view.cascadeMap = state.descriptorBase + cascadeMapDescriptor + frameSlot;
    }
    if (!requests.lights.empty())
    {
        (void)ensureAtlas(state); // without one, no light has a tile
    }
    if (state.atlas != nullptr)
    {
        // With no light requesting, those holding tiles give them back.
        prepareLocalLights(state, requests, lights, view, views);
        if (!state.lights.empty())
        {
            view.shadowAtlas = state.descriptorBase + shadowAtlasDescriptor;
        }
    }
    return {};
}

ShadowRequests& shadowRequests(ShadowState& state)
{
    return state.requests;
}

std::span<const ShadowCullView> shadowCullViews(const ShadowState& state)
{
    return state.cullViews.span();
}

const ShadowFrame& finishShadows(ShadowState& state, const DrawState& draws, const ViewConstants& view,
                                 uint32_t frameSlot)
{
    state.cascades.clear();
    state.tiles.clear();
    stats = {};
    auto* constants = static_cast<uint8_t*>(state.constants[frameSlot]->mappedData());
    const uint64_t address = state.constants[frameSlot]->gpuAddress();
    uint32_t written = 0;
    for (size_t v = 0; v < state.infos.size(); ++v)
    {
        const ViewInfo& info = state.infos[v];
        if (!info.cascade)
        {
            CachedLight& light = state.lights[info.light];
            const uint64_t casters = shadowCasterHash(draws, static_cast<uint32_t>(v));
            if (light.drawn[info.face] && casters == light.hash[info.face])
            {
                ++stats.tilesKept;
                continue; // what the tile holds is still right
            }
            light.drawn[info.face] = true;
            light.hash[info.face] = casters;
        }
        // The view's constants with the light's view, which the vertex
        // shaders project with.
        ViewConstants shadow = view;
        shadow.viewProjection = info.viewProjection;
        std::memcpy(constants + written * viewConstantsStride(), &shadow, sizeof(shadow));
        const ShadowPass pass{.cullView = static_cast<uint32_t>(v),
                              .layer = info.layer,
                              .rect = info.rect,
                              .constants = address + uint64_t{written} * viewConstantsStride(),
                              .draws = shadowDrawList(draws, static_cast<uint32_t>(v))};
        ++written;
        (info.cascade ? state.cascades : state.tiles).pushBack(pass);
    }
    stats.cascades = static_cast<uint32_t>(state.cascades.size());
    stats.tilesDrawn = static_cast<uint32_t>(state.tiles.size());
    state.frame = {.cascades = state.cascades.span(),
                   .tiles = state.tiles.span(),
                   .cascadeSize = mapSize,
                   .cascadeSlot = view.cascadeMap,
                   .atlas = view.shadowAtlas != 0 ? state.atlas : nullptr,
                   .atlasView = state.atlasView,
                   .atlasLayout = state.atlasLayout};
    if (view.shadowAtlas != 0)
    {
        // The Shadows step leaves it ready to sample.
        state.atlasLayout = slag::TextureLayout::ShaderRead;
    }
    return state.frame;
}

} // namespace crucible::render
