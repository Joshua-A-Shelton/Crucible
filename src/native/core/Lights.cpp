// The frame's lights (ADR 0053): directional lights (brightest first, so
// the sun for shadows is the first), point and spot lights culled against
// the view by their range on the job system, and the ambient light; uploaded
// per frame slot for LightClustering and the lighting loop. Those that cast
// shadows are handed to Shadows.cpp (ADR 0054).

#include "RenderInternal.h"

#include <crucible/Ecs.h>
#include <crucible/Jobs.h>
#include <crucible/Lights.h>
#include <crucible/Transform.h>

#include <slag/Slag.h>

#include <algorithm>
#include <array>
#include <cmath>
#include <cstring>

namespace crucible::render {

void registerLights()
{
    ecs::registerComponent<DirectionalLight>("Crucible.Render.DirectionalLight");
    ecs::registerComponent<PointLight>("Crucible.Render.PointLight");
    ecs::registerComponent<SpotLight>("Crucible.Render.SpotLight");
    ecs::registerComponent<AmbientLight>("Crucible.Render.AmbientLight");
}

namespace {

using scenes::WorldTransform;

Vector3 axisOf(const Matrix4x4& world)
{
    return normalize(Vector3{world.rows[2].x, world.rows[2].y, world.rows[2].z});
}

Vector3 positionOf(const Matrix4x4& world)
{
    return {world.rows[3].x, world.rows[3].y, world.rows[3].z};
}

// Shadows reach this far when a directional light doesn't say.
constexpr float defaultShadowDistance = 100.0f;

struct Found
{
    uint32_t entity; // index, for a deterministic order
    LightData light;
    ecs::Entity handle;
    bool castsShadows;
    float shadowReach; // directional: shadowDistance; spot: the full outer angle
};

struct Cull
{
    std::array<Plane, 6> planes;
    Vector3 cameraPosition;
    Vector3 cameraForward;
    Vector<Vector<Found>*>* workers;
    bool spot;
};

void cullChunk(const ecs::ChunkView& view, void* data)
{
    const Cull& cull = *static_cast<const Cull*>(data);
    const int32_t worker = jobs::currentWorker();
    Vector<Found>& out = *(*cull.workers)[worker < 0 ? cull.workers->size() - 1 : static_cast<size_t>(worker)];
    const std::span<const WorldTransform> worlds = ecs::readColumn<WorldTransform>(view);
    const ecs::Entity* entities = ecs::entitiesOf(view);
    for (size_t i = 0; i < worlds.size(); ++i)
    {
        LightData light{};
        light.shadowView = noShadowView;
        bool casts = false;
        float outerAngle = 0;
        if (cull.spot)
        {
            const SpotLight& spot = ecs::readColumn<SpotLight>(view)[i];
            casts = spot.castsShadows;
            outerAngle = std::clamp(spot.outerConeAngle, 0.0f, 3.1f);
            const float cosInner = std::cos(std::clamp(spot.innerConeAngle, 0.0f, 3.1f) * 0.5f);
            const float cosOuter = std::cos(std::clamp(spot.outerConeAngle, 0.0f, 3.1f) * 0.5f);
            light.range = spot.range;
            light.color = spot.color * spot.intensity;
            light.spotScale = 1.0f / std::max(cosInner - cosOuter, 1e-4f);
            light.spotOffset = -cosOuter * light.spotScale;
        }
        else
        {
            const PointLight& point = ecs::readColumn<PointLight>(view)[i];
            casts = point.castsShadows;
            light.range = point.range;
            light.color = point.color * point.intensity;
            light.spotScale = 0;
            light.spotOffset = 1;
        }
        light.position = positionOf(worlds[i].matrix);
        light.direction = -axisOf(worlds[i].matrix);
        if (light.range <= 0)
        {
            continue;
        }
        bool inside = true;
        for (const Plane& plane : cull.planes)
        {
            if (dot(plane.normal, light.position) + plane.distance < -light.range)
            {
                inside = false;
                break;
            }
        }
        if (inside)
        {
            out.pushBack({ecs::indexOf(entities[i]), light, entities[i], casts, outerAngle});
        }
    }
}

} // namespace

struct LightState
{
    uint32_t descriptorBase = 0;
    std::array<slag::Buffer*, framesInFlight> buffers{};
    Vector<Vector<Found>*> workers{MemoryTag::Render};
    Vector<Found> all{MemoryTag::Render};
};

LightState* createLightState(uint32_t descriptorBase)
{
    auto* state = create<LightState>(MemoryTag::Render);
    state->descriptorBase = descriptorBase;
    slag::GraphicsDevice& device = *renderDevice();
    for (uint32_t slot = 0; slot < framesInFlight; ++slot)
    {
        constexpr uint32_t count = maxDirectionalLights + maxVisibleLights;
        slag::Result<slag::Buffer*> buffer =
            device.createBuffer({.size = uint64_t{count} * sizeof(LightData),
                                 .placement = slag::Placement::automatic(slag::MemoryKind::Upload),
                                 .debugName = "crucible lights"});
        if (!buffer)
        {
            destroyLightState(state);
            return nullptr;
        }
        state->buffers[slot] = buffer.value();
        device.writeStructuredBuffer(descriptorBase + lightsDescriptor + slot,
                                     {.buffer = buffer.value(), .count = count, .stride = sizeof(LightData)});
    }
    return state;
}

slag::Buffer* lightBuffer(const LightState& state, uint32_t frameSlot)
{
    return state.buffers[frameSlot];
}

void destroyLightState(LightState* state)
{
    if (state == nullptr)
    {
        return;
    }
    for (slag::Buffer* buffer : state->buffers)
    {
        if (buffer != nullptr)
        {
            renderDevice()->destroy(buffer);
        }
    }
    for (Vector<Found>* worker : state->workers)
    {
        destroy(MemoryTag::Render, worker);
    }
    destroy(MemoryTag::Render, state);
}

void gatherLights(LightState& state, ecs::World* world, uint32_t frameSlot, ViewConstants& view,
                  ShadowRequests& shadows)
{
    shadows.sun = false;
    shadows.lights.clear();
    // The view's near plane and the depth where its clusters end: past the
    // farthest visible light's reach, nothing needs a cluster.
    const Matrix4x4& projection = view.projection;
    const bool perspective = projection.rows[2].w == 1.0f;
    const float nearPlane =
        perspective ? projection.rows[3].z : (1.0f - projection.rows[3].z) / std::min(projection.rows[2].z, -1e-6f);
    float farthest = nearPlane * 2;

    auto* lights = static_cast<LightData*>(state.buffers[frameSlot]->mappedData());
    uint32_t directionalCount = 0;
    uint32_t lightCount = 0;
    Vector3 sky{}, ground{};
    const bool registered = ecs::componentIdOf<DirectionalLight> != ecs::invalidComponent &&
                            ecs::componentIdOf<WorldTransform> != ecs::invalidComponent;
    if (world != nullptr && registered)
    {
        // Directional lights, brightest first, at most maxDirectionalLights.
        const ecs::ComponentId directional[] = {ecs::componentIdOf<DirectionalLight>,
                                                ecs::componentIdOf<WorldTransform>};
        ecs::Query* query = ecs::createQuery(*world, {.required = directional});
        state.all.clear();
        ecs::forEachChunk(
            *query,
            [](const ecs::ChunkView& view, void* data) {
                auto& found = *static_cast<Vector<Found>*>(data);
                const std::span<const DirectionalLight> suns = ecs::readColumn<DirectionalLight>(view);
                const std::span<const WorldTransform> worlds = ecs::readColumn<WorldTransform>(view);
                const ecs::Entity* entities = ecs::entitiesOf(view);
                for (size_t i = 0; i < suns.size(); ++i)
                {
                    LightData light{};
                    light.direction = -axisOf(worlds[i].matrix);
                    light.color = suns[i].color * suns[i].intensity;
                    light.shadowView = noShadowView;
                    found.pushBack({ecs::indexOf(entities[i]), light, entities[i], suns[i].castsShadows,
                                    suns[i].shadowDistance});
                }
            },
            &state.all);
        ecs::destroyQuery(query);
        auto brightness = [](const LightData& l) {
            return std::max(l.color.x, std::max(l.color.y, l.color.z));
        };
        std::stable_sort(state.all.data(), state.all.data() + state.all.size(), [&](const Found& a, const Found& b) {
            return brightness(a.light) != brightness(b.light) ? brightness(a.light) > brightness(b.light)
                                                              : a.entity < b.entity;
        });
        directionalCount = std::min(static_cast<uint32_t>(state.all.size()), maxDirectionalLights);
        for (uint32_t i = 0; i < directionalCount; ++i)
        {
            lights[i] = state.all[i].light;
        }
        // The brightest that casts shadows comes first, so the lighting loop
        // knows the cascades are the first light's.
        for (uint32_t i = 0; i < directionalCount; ++i)
        {
            if (state.all[i].castsShadows)
            {
                std::swap(lights[0], lights[i]);
                shadows.sun = true;
                shadows.sunDirection = lights[0].direction;
                shadows.sunDistance =
                    state.all[i].shadowReach > 0 ? state.all[i].shadowReach : defaultShadowDistance;
                break;
            }
        }

        // Point and spot lights the view can see, in entity order.
        while (state.workers.size() <= jobs::workerCount())
        {
            state.workers.pushBack(create<Vector<Found>>(MemoryTag::Render, MemoryTag::Render));
        }
        for (Vector<Found>* worker : state.workers)
        {
            worker->clear();
        }
        Cull cull{frustumPlanes(view.viewProjection), view.cameraPosition, {}, &state.workers, false};
        for (bool spot : {false, true})
        {
            cull.spot = spot;
            const ecs::ComponentId set[] = {spot ? ecs::componentIdOf<SpotLight> : ecs::componentIdOf<PointLight>,
                                            ecs::componentIdOf<WorldTransform>};
            ecs::Query* lightsQuery = ecs::createQuery(*world, {.required = set});
            ecs::parallelForEachChunk(*lightsQuery, cullChunk, &cull);
            ecs::destroyQuery(lightsQuery);
        }
        state.all.clear();
        for (const Vector<Found>* worker : state.workers)
        {
            for (const Found& found : *worker)
            {
                state.all.pushBack(found);
            }
        }
        std::sort(state.all.data(), state.all.data() + state.all.size(),
                  [](const Found& a, const Found& b) { return a.entity < b.entity; });
        lightCount = std::min(static_cast<uint32_t>(state.all.size()), maxVisibleLights);
        for (uint32_t i = 0; i < lightCount; ++i)
        {
            const LightData& light = state.all[i].light;
            lights[directionalCount + i] = light;
            if (state.all[i].castsShadows)
            {
                const bool point = light.spotScale == 0;
                shadows.lights.pushBack({state.all[i].handle, directionalCount + i, point, light.position,
                                         -light.direction, light.range, state.all[i].shadowReach});
            }
            const Vector4 viewPosition =
                transform(Vector4{light.position.x, light.position.y, light.position.z, 1}, view.view);
            farthest = std::max(farthest, viewPosition.z + light.range);
        }

        // The ambient light: the one with the lowest entity index.
        if (ecs::componentIdOf<AmbientLight> != ecs::invalidComponent)
        {
            const ecs::ComponentId ambientSet[] = {ecs::componentIdOf<AmbientLight>};
            ecs::Query* ambientQuery = ecs::createQuery(*world, {.required = ambientSet});
            struct Ambient
            {
                uint32_t entity = UINT32_MAX;
                AmbientLight light{};
            } ambient;
            ecs::forEachChunk(
                *ambientQuery,
                [](const ecs::ChunkView& view, void* data) {
                    auto& best = *static_cast<Ambient*>(data);
                    const std::span<const AmbientLight> lights = ecs::readColumn<AmbientLight>(view);
                    const ecs::Entity* entities = ecs::entitiesOf(view);
                    for (size_t i = 0; i < lights.size(); ++i)
                    {
                        if (ecs::indexOf(entities[i]) < best.entity)
                        {
                            best = {ecs::indexOf(entities[i]), lights[i]};
                        }
                    }
                },
                &ambient);
            ecs::destroyQuery(ambientQuery);
            if (ambient.entity != UINT32_MAX)
            {
                sky = ambient.light.skyColor * ambient.light.intensity;
                ground = ambient.light.groundColor * ambient.light.intensity;
            }
        }
    }

    // The clusters: 16 x 9 screen tiles, and depth slices growing
    // exponentially from the near plane to the farthest light's reach.
    const float logRange = std::log(farthest / nearPlane);
    view.clusterTileSize = {view.viewportSize.x / clusterCountX, view.viewportSize.y / clusterCountY};
    constexpr auto slices = static_cast<float>(clusterCountZ);
    view.clusterSliceScale = slices / logRange;
    view.clusterSliceBias = -slices * std::log(nearPlane) / logRange;
    view.ambientSky = sky;
    view.ambientGround = ground;
    view.directionalLightCount = directionalCount;
    view.lightCount = lightCount;
    view.lights = state.descriptorBase + lightsDescriptor + frameSlot;
    view.clusterRanges = state.descriptorBase + clusterRangesDescriptor + frameSlot;
    view.clusterLights = state.descriptorBase + clusterLightsDescriptor + frameSlot;
}

} // namespace crucible::render
