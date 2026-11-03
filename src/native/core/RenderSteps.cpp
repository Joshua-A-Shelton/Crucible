// The default render steps (ADR 0048, 0051, 0052, 0053, 0054): DepthPrepass
// draws the draw list's depth into SceneDepth; LightClustering lists each
// cluster's lights; Shadows draws the sun's cascades and the atlas tiles that
// changed; Opaque clears SceneColor to renderFrame's color and draws each
// submesh with its material, testing against the prepass's depth; Resolve
// averages SceneColor's samples into the backbuffer. SceneColor and
// SceneDepth are MSAA transients at msaaSamples().

#include "RenderInternal.h"

#include <crucible/Lights.h>
#include <crucible/Render.h>
#include <crucible/Shader.h>

#include <algorithm>
#include <cstdio>

namespace crucible::render {

namespace {

uint32_t samples = 4;

// The pipeline built from an embedded shader's entry points.
Result<slag::Pipeline*> createPipeline(slag::GraphicsDevice& device, const char* shader, const char* vertexEntry,
                                       const char* pixelEntry, slag::GraphicsPipelineDesc desc)
{
    Result<ShaderFile*> file = loadShaderFile(engineShader(shader));
    if (!file)
    {
        std::fprintf(stderr, "crucible: the embedded %s shader can't be read\n", shader);
        return file.status();
    }
    const int32_t vertex = findShaderEntry(*file.value(), vertexEntry);
    const int32_t pixel = findShaderEntry(*file.value(), pixelEntry);
    if (vertex < 0 || pixel < 0)
    {
        destroyShaderFile(file.value());
        return Status::InvalidData;
    }
    shaderStage(*file.value(), static_cast<uint32_t>(vertex), desc.vertex);
    shaderStage(*file.value(), static_cast<uint32_t>(pixel), desc.pixel);
    slag::Result<slag::Pipeline*> pipeline = device.createGraphicsPipeline(desc);
    destroyShaderFile(file.value());
    if (!pipeline)
    {
        return Status::Unsupported;
    }
    return pipeline.value();
}

void destroyPipeline(slag::GraphicsDevice& device, void* userData)
{
    auto*& pipeline = *static_cast<slag::Pipeline**>(userData);
    if (pipeline != nullptr)
    {
        device.destroy(pipeline);
        pipeline = nullptr;
    }
}

// One submesh of one batch, with the pipeline it draws with.
struct Draw
{
    const DrawBatch* batch;
    uint32_t submesh;
    slag::Pipeline* pipeline;
    uint32_t material; // byte offset in the material buffer
    float alphaCutoff;
    uint32_t instance = UINT32_MAX; // one instance (sorted transparency); else the whole batch
    float distance = 0;             // sorted transparency: the instance's view depth
};

enum class Pass : uint8_t
{
    Depth,
    Forward,
    Shadow,
    Transparent,
};

// Every batch's submeshes, ordered by pipeline so the GPU switches state as
// little as possible; kept per step, reused frame to frame.
void gatherDraws(const DrawList& draws, Pass kind, Vector<Draw>& out)
{
    out.clear();
    for (const DrawBatch& batch : draws.batches)
    {
        const MeshData& model = *batch.model;
        const assets::MeshEntry& mesh = model.meshes[batch.mesh];
        const assets::MeshLod& lod = model.lods[mesh.lodOffset + batch.lod];
        for (uint32_t submesh = 0; submesh < mesh.submeshCount; ++submesh)
        {
            const assets::MeshRange& range = model.ranges[lod.rangeOffset + submesh];
            if (range.indexCount == 0)
            {
                continue;
            }
            const MaterialHandle handle =
                range.material >= 0 && static_cast<size_t>(range.material) < batch.materials.size()
                    ? batch.materials[static_cast<size_t>(range.material)]
                    : MaterialHandle{};
            const MaterialDraw material = materialDraw(handle);
            slag::Pipeline* pipeline = kind == Pass::Depth     ? material.pipelines->depth
                                       : kind == Pass::Forward ? material.pipelines->forward
                                       : kind == Pass::Shadow  ? material.pipelines->shadow
                                                               : material.pipelines->transparent;
            if (pipeline != nullptr)
            {
                out.pushBack({&batch, submesh, pipeline, material.offset, material.alphaCutoff});
            }
        }
    }
    std::stable_sort(out.data(), out.data() + out.size(),
                     [](const Draw& a, const Draw& b) { return a.pipeline < b.pipeline; });
}

void recordDraws(slag::CommandBuffer* commands, const DrawList& draws, const Vector<Draw>& list)
{
    slag::Pipeline* bound = nullptr;
    for (const Draw& draw : list)
    {
        if (draw.pipeline != bound)
        {
            commands->bindPipeline(draw.pipeline);
            bound = draw.pipeline;
        }
        const DrawBatch& batch = *draw.batch;
        const MeshData& model = *batch.model;
        const assets::MeshEntry& mesh = model.meshes[batch.mesh];
        const assets::MeshRange& range =
            model.ranges[model.lods[mesh.lodOffset + batch.lod].rangeOffset + draw.submesh];
        commands->bindIndexBuffer(model.buffer, model.indices.offset, model.indices.size,
                                  mesh.indexSize == 2 ? slag::IndexFormat::Uint16 : slag::IndexFormat::Uint32);
        const uint64_t first = mesh.vertexOffset;
        const bool floatUv0 = (model.info.floatUvs & 1) != 0;
        const bool floatUv1 = (model.info.floatUvs & 2) != 0;
        DrawConstants constants{};
        constants.model = model.descriptor;
        constants.positions = static_cast<uint32_t>(model.positions.offset + first * 12);
        constants.normals = static_cast<uint32_t>(model.normals.offset + first * 4);
        constants.tangents = static_cast<uint32_t>(model.tangents.offset + first * 8);
        constants.uv0 = static_cast<uint32_t>(model.uv0.offset + first * (floatUv0 ? 8 : 4));
        constants.uv1 = static_cast<uint32_t>(model.uv1.offset + first * (floatUv1 ? 8 : 4));
        constants.colors = static_cast<uint32_t>(model.colors.offset + first * 4);
        constants.attributes = mesh.attributes;
        constants.floatUvs = model.info.floatUvs;
        constants.instances = draws.instances;
        constants.transforms = draws.transforms;
        constants.materials = materialBufferDescriptor;
        constants.material = draw.material;
        constants.alphaCutoff = draw.alphaCutoff;
        commands->pushConstants(&constants, sizeof(constants));
        const bool one = draw.instance != UINT32_MAX;
        commands->drawIndexed(range.indexCount, one ? 1 : batch.instanceCount, range.indexByteOffset / mesh.indexSize,
                              0, one ? draw.instance : batch.firstInstance);
    }
}

uint32_t viewSize()
{
    return (sizeof(ViewConstants) + slag::alignment::constantBuffer - 1) / slag::alignment::constantBuffer *
           slag::alignment::constantBuffer;
}

// ---------------------------------------------------------------------------
// DepthPrepass
// ---------------------------------------------------------------------------

Vector<Draw> depthDraws{MemoryTag::Render};

void addDepthPrepass(RenderStepContext& context, void*)
{
    const slag::rendergraph::RenderGraphTexture depth =
        context.graph.createTransientTexture2D({.format = sceneDepthFormat,
                                                .width = context.width,
                                                .height = context.height,
                                                .sampleCount = samples,
                                                .usage = slag::TextureUsage::DepthStencil,
                                                .debugName = "SceneDepth"});
    context.publish(sceneDepthKey(), depth);
    const slag::rendergraph::RenderGraphBuffer view = context.buffer(viewKey());
    gatherDraws(context.draws, Pass::Depth, depthDraws);
    const DrawList* draws = &context.draws;
    context.graph.addPass(
        "DepthPrepass", slag::CommandQueueType::Graphics,
        [&](slag::rendergraph::PassBuilder& pass) {
            pass.use(view, slag::rendergraph::Usage::ConstantBuffer);
            // Reverse-Z (ADR 0050): far is 0.
            pass.depthStencil(depth, slag::LoadOp::Clear, slag::StoreOp::Store, {.depth = 0.0f});
        },
        [draws, view](slag::rendergraph::PassContext& pass, slag::CommandBuffer* commands) {
            commands->setConstantBuffer(pass.buffer(view)->gpuAddress(), viewSize());
            recordDraws(commands, *draws, depthDraws);
        });
}

// ---------------------------------------------------------------------------
// LightClustering
// ---------------------------------------------------------------------------

slag::Pipeline* clusterPipeline = nullptr;

Result<void> prepareLightClustering(slag::GraphicsDevice& device, void*)
{
    Result<ShaderFile*> file = loadShaderFile(engineShader("clusters"));
    if (!file)
    {
        return file.status();
    }
    const int32_t entry = findShaderEntry(*file.value(), "buildClusters");
    if (entry < 0)
    {
        destroyShaderFile(file.value());
        return Status::InvalidData;
    }
    slag::ShaderStage stage;
    shaderStage(*file.value(), static_cast<uint32_t>(entry), stage);
    slag::Result<slag::Pipeline*> pipeline =
        device.createComputePipeline({.compute = stage, .debugName = "LightClustering"});
    destroyShaderFile(file.value());
    if (!pipeline)
    {
        return Status::Unsupported;
    }
    clusterPipeline = pipeline.value();
    return {};
}

// One buffer: each cluster's light count, then each cluster's list.
constexpr uint64_t clusterListsOffset = uint64_t{clusterCount} * sizeof(uint32_t);
constexpr uint64_t clusterBufferSize = clusterListsOffset + uint64_t{clusterCount} * maxLightsPerCluster * 4;

void addLightClustering(RenderStepContext& context, void*)
{
    const slag::rendergraph::RenderGraphBuffer clusters =
        context.graph.createTransientBuffer({.size = clusterBufferSize, .debugName = "LightClusters"});
    context.publish(lightClustersKey(), clusters);
    const slag::rendergraph::RenderGraphBuffer view = context.buffer(viewKey());
    const slag::rendergraph::RenderGraphBuffer lights = context.buffer(lightsKey());
    // The surface's slots for this frame slot: the transient buffer differs
    // from frame to frame, so its views are written while recording.
    const uint32_t ranges = surfaceDescriptor(context, clusterRangesDescriptor);
    const uint32_t lists = surfaceDescriptor(context, clusterLightsDescriptor);
    const uint32_t rwRanges = surfaceDescriptor(context, rwClusterRangesDescriptor);
    const uint32_t rwLists = surfaceDescriptor(context, rwClusterLightsDescriptor);
    slag::GraphicsDevice* device = &context.device;
    context.graph.addPass(
        "LightClustering", slag::CommandQueueType::Graphics,
        [&](slag::rendergraph::PassBuilder& pass) {
            pass.use(view, slag::rendergraph::Usage::ConstantBuffer);
            pass.use(lights, slag::rendergraph::Usage::ShaderRead);
            pass.use(clusters, slag::rendergraph::Usage::ShaderWrite);
        },
        [=](slag::rendergraph::PassContext& pass, slag::CommandBuffer* commands) {
            const uint32_t slot = pass.frameSlot();
            slag::Buffer* buffer = pass.buffer(clusters);
            const slag::StructuredBufferDescriptor countsView{
                .buffer = buffer, .count = clusterCount, .stride = sizeof(uint32_t)};
            const slag::StructuredBufferDescriptor listsView{.buffer = buffer,
                                                             .offset = clusterListsOffset,
                                                             .count = clusterCount * maxLightsPerCluster,
                                                             .stride = sizeof(uint32_t)};
            device->writeStructuredBuffer(ranges + slot, countsView);
            device->writeStructuredBuffer(lists + slot, listsView);
            device->writeRWStructuredBuffer(rwRanges + slot, countsView);
            device->writeRWStructuredBuffer(rwLists + slot, listsView);
            const uint32_t constants[2] = {rwRanges + slot, rwLists + slot};
            commands->bindPipeline(clusterPipeline);
            commands->setConstantBuffer(pass.buffer(view)->gpuAddress(), viewSize());
            commands->pushConstants(constants, sizeof(constants));
            commands->dispatch((clusterCount + 63) / 64, 1, 1);
        });
}

// ---------------------------------------------------------------------------
// Shadows
// ---------------------------------------------------------------------------

// Clears an atlas tile before it's drawn again: the rest of the atlas keeps
// what it holds.
slag::Pipeline* clearTilePipeline = nullptr;

Result<void> prepareShadowStep(slag::GraphicsDevice& device, void*)
{
    const slag::PixelFormat format = shadowFormat();
    if (format == slag::PixelFormat::Undefined)
    {
        return {};
    }
    Result<ShaderFile*> file = loadShaderFile(engineShader("mesh"));
    if (!file)
    {
        return file.status();
    }
    const int32_t entry = findShaderEntry(*file.value(), "clearTileVertex");
    if (entry < 0)
    {
        destroyShaderFile(file.value());
        return Status::InvalidData;
    }
    slag::GraphicsPipelineDesc desc{
        .depthStencil = {.depthTest = true, .depthWrite = true, .depthCompareOp = slag::CompareOp::Always},
        .depthStencilFormat = format,
        .debugName = "Shadows (clear tile)"};
    shaderStage(*file.value(), static_cast<uint32_t>(entry), desc.vertex);
    slag::Result<slag::Pipeline*> pipeline = device.createGraphicsPipeline(desc);
    destroyShaderFile(file.value());
    if (!pipeline)
    {
        return Status::Unsupported;
    }
    clearTilePipeline = pipeline.value();
    return {};
}

// Each pass's draws, kept frame to frame (the passes record in parallel).
Vector<Vector<Draw>*> shadowDraws{MemoryTag::Render};

Vector<Draw>& shadowDrawsAt(size_t index)
{
    while (shadowDraws.size() <= index)
    {
        shadowDraws.pushBack(create<Vector<Draw>>(MemoryTag::Render, MemoryTag::Render));
    }
    return *shadowDraws[index];
}

void addShadows(RenderStepContext& context, void*)
{
    const ShadowFrame& frame = shadowFrame(context);
    const slag::PixelFormat format = shadowFormat();
    size_t used = 0;
    if (!frame.cascades.empty())
    {
        const slag::rendergraph::RenderGraphTexture cascades =
            context.graph.createTransientTexture2DArray({.format = format,
                                                         .width = frame.cascadeSize,
                                                         .height = frame.cascadeSize,
                                                         .layerCount = shadowCascadeCount,
                                                         .usage = slag::TextureUsage::DepthStencil,
                                                         .debugName = "ShadowCascades"});
        context.publish(shadowCascadesKey(), cascades);
        for (const ShadowPass& cascade : frame.cascades)
        {
            Vector<Draw>& draws = shadowDrawsAt(used++);
            gatherDraws(cascade.draws, Pass::Shadow, draws);
            const ShadowPass* pass = &cascade;
            const Vector<Draw>* list = &draws;
            context.graph.addPass(
                "ShadowCascade", slag::CommandQueueType::Graphics,
                [&](slag::rendergraph::PassBuilder& builder) {
                    // Conventional depth: far is 1.
                    builder.depthStencil(cascades, slag::LoadOp::Clear, slag::StoreOp::Store, {.depth = 1.0f}, false, 0,
                                         cascade.layer, 1);
                },
                [pass, list](slag::rendergraph::PassContext&, slag::CommandBuffer* commands) {
                    commands->setConstantBuffer(pass->constants, viewSize());
                    recordDraws(commands, pass->draws, *list);
                });
        }
    }
    if (frame.atlas != nullptr)
    {
        const slag::rendergraph::RenderGraphTexture atlas =
            context.graph.importTexture(frame.atlas, {.initialLayout = frame.atlasLayout,
                                                      .finalLayout = slag::TextureLayout::ShaderRead,
                                                      .attachmentView = frame.atlasView});
        context.publish(shadowAtlasKey(), atlas);
        if (!frame.tiles.empty())
        {
            const size_t first = used;
            for (const ShadowPass& tile : frame.tiles)
            {
                gatherDraws(tile.draws, Pass::Shadow, shadowDrawsAt(used++));
            }
            const std::span<const ShadowPass> tiles = frame.tiles;
            context.graph.addPass(
                "ShadowAtlas", slag::CommandQueueType::Graphics,
                [&](slag::rendergraph::PassBuilder& builder) {
                    builder.depthStencil(atlas, slag::LoadOp::Load, slag::StoreOp::Store);
                },
                [tiles, first](slag::rendergraph::PassContext&, slag::CommandBuffer* commands) {
                    for (size_t t = 0; t < tiles.size(); ++t)
                    {
                        const ShadowPass& tile = tiles[t];
                        const slag::Viewport viewport{static_cast<float>(tile.rect.x), static_cast<float>(tile.rect.y),
                                                      static_cast<float>(tile.rect.width),
                                                      static_cast<float>(tile.rect.height)};
                        commands->setViewports({&viewport, 1});
                        commands->setScissors({&tile.rect, 1});
                        commands->bindPipeline(clearTilePipeline);
                        commands->draw(3, 1);
                        commands->setConstantBuffer(tile.constants, viewSize());
                        recordDraws(commands, tile.draws, *shadowDraws[first + t]);
                    }
                });
        }
    }
}

void releaseShadows(slag::GraphicsDevice& device, void* userData)
{
    destroyPipeline(device, userData);
    for (Vector<Draw>* draws : shadowDraws)
    {
        destroy(MemoryTag::Render, draws);
    }
    shadowDraws.clear();
}

// ---------------------------------------------------------------------------
// Opaque
// ---------------------------------------------------------------------------

// What the lighting loop reads (ADR 0053, 0054), for Opaque and Transparent.
struct Lighting
{
    slag::rendergraph::RenderGraphBuffer view;
    slag::rendergraph::RenderGraphBuffer lights;
    slag::rendergraph::RenderGraphBuffer clusters;
    slag::rendergraph::RenderGraphTexture cascades; // when the view says there are any this frame
    slag::rendergraph::RenderGraphTexture atlas;

    void declare(slag::rendergraph::PassBuilder& pass) const
    {
        pass.use(view, slag::rendergraph::Usage::ConstantBuffer);
        pass.use(lights, slag::rendergraph::Usage::ShaderRead);
        pass.use(clusters, slag::rendergraph::Usage::ShaderRead);
        if (cascades.index != UINT32_MAX)
        {
            pass.use(cascades, slag::rendergraph::Usage::ShaderRead);
        }
        if (atlas.index != UINT32_MAX)
        {
            pass.use(atlas, slag::rendergraph::Usage::ShaderRead);
        }
    }
};

Lighting readLighting(RenderStepContext& context)
{
    Lighting lighting{
        context.buffer(viewKey()), context.buffer(lightsKey()), context.buffer(lightClustersKey()), {}, {}};
    if (context.view.cascadeMap != 0)
    {
        lighting.cascades = context.texture(shadowCascadesKey());
    }
    if (context.view.shadowAtlas != 0)
    {
        lighting.atlas = context.texture(shadowAtlasKey());
    }
    return lighting;
}

Vector<Draw> opaqueDraws{MemoryTag::Render};

void addOpaque(RenderStepContext& context, void*)
{
    const slag::rendergraph::RenderGraphTexture color =
        context.graph.createTransientTexture2D({.format = sceneColorFormat,
                                                .width = context.width,
                                                .height = context.height,
                                                .sampleCount = samples,
                                                .usage = slag::TextureUsage::RenderTarget,
                                                .debugName = "SceneColor"});
    context.publish(sceneColorKey(), color);
    const slag::rendergraph::RenderGraphTexture depth = context.texture(sceneDepthKey());
    const slag::rendergraph::RenderGraphBuffer view = context.buffer(viewKey());
    const Lighting lighting = readLighting(context);
    const slag::rendergraph::RenderGraphTexture cascades = lighting.cascades;
    const uint32_t cascadeSlot = context.view.cascadeMap;
    slag::GraphicsDevice* device = &context.device;
    slag::ClearValue clear{};
    for (int c = 0; c < 4; ++c)
    {
        clear.color[c] = context.clearColor[c];
    }
    gatherDraws(context.draws, Pass::Forward, opaqueDraws);
    const DrawList* draws = &context.draws;
    context.graph.addPass(
        "Opaque", slag::CommandQueueType::Graphics,
        [&](slag::rendergraph::PassBuilder& pass) {
            lighting.declare(pass);
            pass.renderTarget(color, slag::LoadOp::Clear, slag::StoreOp::Store, clear);
            pass.depthStencil(depth, slag::LoadOp::Load, slag::StoreOp::Store, {}, true);
        },
        [draws, view, cascades, cascadeSlot, device](slag::rendergraph::PassContext& pass,
                                                     slag::CommandBuffer* commands) {
            if (cascades.index != UINT32_MAX)
            {
                // A transient, different from frame to frame: its view is
                // written while recording, in this frame slot's slot.
                device->writeTexture(cascadeSlot, {.texture = pass.texture(cascades),
                                                   .viewType = slag::TextureViewType::Texture2DArray,
                                                   .plane = slag::TexturePlane::Depth});
            }
            commands->setConstantBuffer(pass.buffer(view)->gpuAddress(), viewSize());
            recordDraws(commands, *draws, opaqueDraws);
        });
}

// ---------------------------------------------------------------------------
// Sky
// ---------------------------------------------------------------------------

slag::Pipeline* skyPipeline = nullptr;

Result<void> prepareSky(slag::GraphicsDevice& device, void*)
{
    const slag::PixelFormat formats[] = {sceneColorFormat};
    // Reverse-Z: drawn at depth 0, it passes only where the depth is still
    // the cleared 0.
    Result<slag::Pipeline*> pipeline = createPipeline(
        device, "sky", "skyVertex", "skyPixel",
        {.rasterizer = {.cullMode = slag::CullMode::None},
         .depthStencil = {.depthTest = true, .depthWrite = false, .depthCompareOp = slag::CompareOp::GreaterEqual},
         .colorFormats = formats,
         .depthStencilFormat = sceneDepthFormat,
         .sampleCount = samples,
         .debugName = "Sky"});
    if (!pipeline)
    {
        return pipeline.status();
    }
    skyPipeline = pipeline.value();
    return {};
}

void addSky(RenderStepContext& context, void*)
{
    if (context.view.sky == 0)
    {
        return; // the clear color shows
    }
    const slag::rendergraph::RenderGraphTexture color = context.texture(sceneColorKey());
    const slag::rendergraph::RenderGraphTexture depth = context.texture(sceneDepthKey());
    const slag::rendergraph::RenderGraphBuffer view = context.buffer(viewKey());
    const slag::rendergraph::RenderGraphBuffer lights = context.buffer(lightsKey());
    context.graph.addPass(
        "Sky", slag::CommandQueueType::Graphics,
        [&](slag::rendergraph::PassBuilder& pass) {
            pass.use(view, slag::rendergraph::Usage::ConstantBuffer);
            pass.use(lights, slag::rendergraph::Usage::ShaderRead);
            pass.renderTarget(color, slag::LoadOp::Load, slag::StoreOp::Store);
            pass.depthStencil(depth, slag::LoadOp::Load, slag::StoreOp::Store, {}, true);
        },
        [view](slag::rendergraph::PassContext& pass, slag::CommandBuffer* commands) {
            commands->bindPipeline(skyPipeline);
            commands->setConstantBuffer(pass.buffer(view)->gpuAddress(), viewSize());
            commands->draw(3, 1);
        });
}

// ---------------------------------------------------------------------------
// Transparent
// ---------------------------------------------------------------------------

Vector<Draw> transparentDraws{MemoryTag::Render};

// Every blended submesh of every visible instance, farthest first, one
// draw each (ADR 0055).
void gatherSorted(const DrawList& draws, std::span<const float> distances, Vector<Draw>& out)
{
    gatherDraws(draws, Pass::Transparent, out);
    const size_t batchDraws = out.size();
    for (size_t d = 0; d < batchDraws; ++d)
    {
        const Draw draw = out[d];
        for (uint32_t i = 0; i < draw.batch->instanceCount; ++i)
        {
            Draw one = draw;
            one.instance = draw.batch->firstInstance + i;
            one.distance = one.instance < distances.size() ? distances[one.instance] : 0.0f;
            out.pushBack(one);
        }
    }
    // Keep only the per-instance draws, sorted; equal distances keep their
    // submeshes in order.
    for (size_t d = batchDraws; d < out.size(); ++d)
    {
        out[d - batchDraws] = out[d];
    }
    out.resize(out.size() - batchDraws);
    std::stable_sort(out.data(), out.data() + out.size(),
                     [](const Draw& a, const Draw& b) { return a.distance > b.distance; });
}

void addTransparent(RenderStepContext& context, void*)
{
    gatherSorted(context.draws, instanceDistances(frameDrawState(context)), transparentDraws);
    if (transparentDraws.empty())
    {
        return;
    }
    const slag::rendergraph::RenderGraphTexture color = context.texture(sceneColorKey());
    const slag::rendergraph::RenderGraphTexture depth = context.texture(sceneDepthKey());
    const Lighting lighting = readLighting(context);
    const slag::rendergraph::RenderGraphBuffer view = lighting.view;
    const DrawList* draws = &context.draws;
    context.graph.addPass(
        "Transparent", slag::CommandQueueType::Graphics,
        [&](slag::rendergraph::PassBuilder& pass) {
            lighting.declare(pass);
            pass.renderTarget(color, slag::LoadOp::Load, slag::StoreOp::Store);
            pass.depthStencil(depth, slag::LoadOp::Load, slag::StoreOp::Store, {}, true);
        },
        [draws, view](slag::rendergraph::PassContext& pass, slag::CommandBuffer* commands) {
            commands->setConstantBuffer(pass.buffer(view)->gpuAddress(), viewSize());
            recordDraws(commands, *draws, transparentDraws);
        });
}

// ---------------------------------------------------------------------------
// Resolve
// ---------------------------------------------------------------------------

slag::Pipeline* resolvePipeline = nullptr;

Result<void> prepareResolve(slag::GraphicsDevice& device, void*)
{
    const slag::PixelFormat formats[] = {backbufferFormat};
    Result<slag::Pipeline*> pipeline = createPipeline(
        device, "resolve", "fullscreenVertex", "resolvePixel",
        {.rasterizer = {.cullMode = slag::CullMode::None}, .colorFormats = formats, .debugName = "Resolve"});
    if (!pipeline)
    {
        return pipeline.status();
    }
    resolvePipeline = pipeline.value();
    return {};
}

void addResolve(RenderStepContext& context, void*)
{
    const slag::rendergraph::RenderGraphTexture source = context.texture(sceneColorKey());
    const slag::rendergraph::RenderGraphTexture backbuffer = context.texture(backbufferKey());
    // The surface's slot for this frame slot: the transient texture differs
    // from frame to frame, so it's written while recording.
    const uint32_t firstSlot = surfaceDescriptor(context, resolveDescriptor);
    slag::GraphicsDevice* device = &context.device;
    context.graph.addPass(
        "Resolve", slag::CommandQueueType::Graphics,
        [&](slag::rendergraph::PassBuilder& pass) {
            pass.use(source, slag::rendergraph::Usage::ShaderRead);
            pass.renderTarget(backbuffer, slag::LoadOp::Discard, slag::StoreOp::Store);
        },
        [source, firstSlot, device](slag::rendergraph::PassContext& pass, slag::CommandBuffer* commands) {
            const uint32_t slot = firstSlot + pass.frameSlot();
            device->writeTexture(slot, {.texture = pass.texture(source),
                                        .viewType = samples > 1 ? slag::TextureViewType::Texture2DMS
                                                                : slag::TextureViewType::Texture2D});
            const uint32_t constants[2] = {slot, samples};
            commands->bindPipeline(resolvePipeline);
            commands->pushConstants(constants, sizeof(constants));
            commands->draw(3, 1);
        });
}

} // namespace

void addDefaultSteps(RenderSchedule& schedule)
{
    // Their pipelines are the materials' (Materials.cpp).
    (void)schedule.add({.name = "DepthPrepass", .addPasses = addDepthPrepass});
    (void)schedule.add({.name = "LightClustering",
                        .addPasses = addLightClustering,
                        .userData = &clusterPipeline,
                        .prepare = prepareLightClustering,
                        .release = destroyPipeline});
    (void)schedule.add({.name = "Shadows",
                        .addPasses = addShadows,
                        .userData = &clearTilePipeline,
                        .prepare = prepareShadowStep,
                        .release = releaseShadows});
    (void)schedule.add({.name = "Opaque", .addPasses = addOpaque});
    (void)schedule.add({.name = "Sky",
                        .addPasses = addSky,
                        .userData = &skyPipeline,
                        .prepare = prepareSky,
                        .release = destroyPipeline});
    (void)schedule.add({.name = "Transparent", .addPasses = addTransparent});
    (void)schedule.add({.name = "Resolve",
                        .addPasses = addResolve,
                        .userData = &resolvePipeline,
                        .prepare = prepareResolve,
                        .release = destroyPipeline});
}

Result<void> setMsaaSamples(uint32_t count)
{
    if (count != 1 && count != 2 && count != 4 && count != 8)
    {
        return Status::Unsupported;
    }
    slag::GraphicsDevice* device = renderDevice();
    if (device != nullptr && count > 1)
    {
        const slag::FormatSupport needed = count == 2   ? slag::FormatSupport::Multisample2
                                           : count == 4 ? slag::FormatSupport::Multisample4
                                                        : slag::FormatSupport::Multisample8;
        if ((device->formatSupport(sceneColorFormat) & needed) != needed ||
            (device->formatSupport(sceneDepthFormat) & needed) != needed)
        {
            return Status::Unsupported;
        }
    }
    if (count == samples)
    {
        return {};
    }
    if (device == nullptr)
    {
        samples = count;
        return {};
    }
    // Every step's pipelines again, with the GPU done with the old ones.
    (void)renderQueue()->waitIdle();
    RenderSchedule& schedule = renderSchedule();
    schedule.releaseAll();
    samples = count;
    if (Result<void> rebuilt = rebuildMaterialPipelines(); !rebuilt)
    {
        return rebuilt;
    }
    return schedule.prepareAll(*device);
}

uint32_t msaaSamples()
{
    return samples;
}

} // namespace crucible::render
