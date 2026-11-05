// The default render steps (ADR 0048, 0051 to 0055): DepthPrepass draws the
// draw list's depth into SceneDepth; LightClustering lists each cluster's
// lights; Shadows draws the sun's cascades and the atlas tiles that changed;
// Opaque clears SceneColor to renderFrame's color and draws each submesh with
// its material, testing against the prepass's depth; Outlines draws outlined
// materials' inverted hulls; Sky fills what's left;
// Transparent blends what's see-through, farthest first; Bloom filters a glow
// from SceneColor; Tonemap finishes each sample (exposure, bloom,
// tonemapping, grading) and averages them into the backbuffer. SceneColor
// and SceneDepth are MSAA transients at msaaSamples().

#include "RenderInternal.h"

#include <crucible/Lights.h>
#include <crucible/Render.h>
#include <crucible/Shader.h>

#include <algorithm>
#include <array>
#include <cstdio>
#include <cstring>

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
    MaterialOutline outline;
    uint32_t instance = UINT32_MAX; // one instance (sorted transparency); else the whole batch
    float distance = 0;             // sorted transparency: the instance's view depth
};

enum class Pass : uint8_t
{
    Depth,
    Forward,
    Shadow,
    Transparent,
    Outline,
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
            slag::Pipeline* pipeline = kind == Pass::Depth         ? material.pipelines->depth
                                       : kind == Pass::Forward     ? material.pipelines->forward
                                       : kind == Pass::Shadow      ? material.pipelines->shadow
                                       : kind == Pass::Transparent ? material.pipelines->transparent
                                                                   : material.pipelines->outline;
            if (pipeline != nullptr)
            {
                out.pushBack({&batch, submesh, pipeline, material.offset, material.alphaCutoff, material.outline});
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
        constants.outlineNormals = static_cast<uint32_t>(model.outlineNormals.offset + first * 4);
        constants.outlineWidth = draw.outline.width;
        constants.outlineColor = draw.outline.color;
        constants.outlineTint = draw.outline.tint;
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
// Outlines
// ---------------------------------------------------------------------------

Vector<Draw> outlineDraws{MemoryTag::Render};

// Inverted hulls (ADR 0055): every outlined material's submeshes again,
// pushed out and front faces culled, after Opaque and before Sky.
void addOutlines(RenderStepContext& context, void*)
{
    gatherDraws(context.draws, Pass::Outline, outlineDraws);
    if (outlineDraws.empty())
    {
        return;
    }
    const slag::rendergraph::RenderGraphTexture color = context.texture(sceneColorKey());
    const slag::rendergraph::RenderGraphTexture depth = context.texture(sceneDepthKey());
    const slag::rendergraph::RenderGraphBuffer view = context.buffer(viewKey());
    const DrawList* draws = &context.draws;
    context.graph.addPass(
        "Outlines", slag::CommandQueueType::Graphics,
        [&](slag::rendergraph::PassBuilder& pass) {
            pass.use(view, slag::rendergraph::Usage::ConstantBuffer);
            pass.renderTarget(color, slag::LoadOp::Load, slag::StoreOp::Store);
            pass.depthStencil(depth, slag::LoadOp::Load, slag::StoreOp::Store);
        },
        [draws, view](slag::rendergraph::PassContext& pass, slag::CommandBuffer* commands) {
            commands->setConstantBuffer(pass.buffer(view)->gpuAddress(), viewSize());
            recordDraws(commands, *draws, outlineDraws);
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
// Bloom
// ---------------------------------------------------------------------------

struct BloomPipelines
{
    slag::Pipeline* prefilter = nullptr;
    slag::Pipeline* downsample = nullptr;
    slag::Pipeline* upsample = nullptr; // added onto the level
} bloomPipelines;
slag::PixelFormat bloomFormat = slag::PixelFormat::R16G16B16A16Float;

Result<void> prepareBloom(slag::GraphicsDevice& device, void*)
{
    // R11G11B10Float where it can be drawn, filtered, and blended: half the
    // bandwidth, and the glow needs no alpha.
    constexpr slag::FormatSupport needed = slag::FormatSupport::Sampled | slag::FormatSupport::LinearFilter |
                                           slag::FormatSupport::RenderTarget | slag::FormatSupport::Blend;
    bloomFormat = (device.formatSupport(slag::PixelFormat::R11G11B10Float) & needed) == needed
                      ? slag::PixelFormat::R11G11B10Float
                      : slag::PixelFormat::R16G16B16A16Float;
    const slag::PixelFormat formats[] = {bloomFormat};
    slag::GraphicsPipelineDesc desc{
        .rasterizer = {.cullMode = slag::CullMode::None}, .colorFormats = formats, .debugName = "Bloom"};
    const char* entries[3] = {"bloomPrefilterPixel", "bloomDownsamplePixel", "bloomUpsamplePixel"};
    slag::Pipeline** out[3] = {&bloomPipelines.prefilter, &bloomPipelines.downsample, &bloomPipelines.upsample};
    for (int i = 0; i < 3; ++i)
    {
        if (i == 2)
        {
            desc.blend.targets[0] = {.blendEnable = true,
                                     .srcColorFactor = slag::BlendFactor::One,
                                     .destColorFactor = slag::BlendFactor::One,
                                     .srcAlphaFactor = slag::BlendFactor::One,
                                     .destAlphaFactor = slag::BlendFactor::One};
        }
        Result<slag::Pipeline*> pipeline = createPipeline(device, "bloom", "bloomVertex", entries[i], desc);
        if (!pipeline)
        {
            return pipeline.status();
        }
        *out[i] = pipeline.value();
    }
    return {};
}

void releaseBloom(slag::GraphicsDevice& device, void*)
{
    destroyPipeline(device, &bloomPipelines.prefilter);
    destroyPipeline(device, &bloomPipelines.downsample);
    destroyPipeline(device, &bloomPipelines.upsample);
}

struct BloomPass
{
    slag::Pipeline* pipeline;
    uint32_t source; // slot
    uint32_t samples;
    float sourceTexel[2];
};

void addBloom(RenderStepContext& context, void*)
{
    const uint32_t levels = context.view.bloomLevels;
    if (levels == 0)
    {
        return;
    }
    const uint32_t width = std::max(context.width / 2, 1u);
    const uint32_t height = std::max(context.height / 2, 1u);
    const slag::rendergraph::RenderGraphTexture chain =
        context.graph.createTransientTexture2D({.format = bloomFormat,
                                                .width = width,
                                                .height = height,
                                                .mipCount = levels,
                                                .usage = slag::TextureUsage::RenderTarget,
                                                .debugName = "Bloom"});
    context.publish(bloomKey(), chain);
    const slag::rendergraph::RenderGraphTexture scene = context.texture(sceneColorKey());
    const slag::rendergraph::RenderGraphBuffer view = context.buffer(viewKey());
    const uint32_t sourceSlot = surfaceDescriptor(context, bloomSourceDescriptor);
    const uint32_t levelSlots = surfaceDescriptor(context, bloomLevelsDescriptor);
    slag::GraphicsDevice* device = &context.device;
    auto levelSize = [&](uint32_t level) {
        return std::array<float, 2>{static_cast<float>(std::max(width >> level, 1u)),
                                    static_cast<float>(std::max(height >> level, 1u))};
    };

    // Level 0 from SceneColor; the views of SceneColor and every level are
    // written here (transients differ from frame to frame).
    context.graph.addPass(
        "BloomPrefilter", slag::CommandQueueType::Graphics,
        [&](slag::rendergraph::PassBuilder& pass) {
            pass.use(view, slag::rendergraph::Usage::ConstantBuffer);
            pass.use(scene, slag::rendergraph::Usage::ShaderRead);
            pass.renderTarget(chain, slag::LoadOp::Discard, slag::StoreOp::Store, {}, 0);
        },
        [=](slag::rendergraph::PassContext& pass, slag::CommandBuffer* commands) {
            const uint32_t slot = pass.frameSlot();
            device->writeTexture(sourceSlot + slot, {.texture = pass.texture(scene),
                                                     .viewType = samples > 1 ? slag::TextureViewType::Texture2DMS
                                                                             : slag::TextureViewType::Texture2D});
            for (uint32_t level = 0; level < levels; ++level)
            {
                device->writeTexture(levelSlots + level * framesInFlight + slot,
                                     {.texture = pass.texture(chain), .baseMip = level, .mipCount = 1});
            }
            const BloomPass constants{nullptr, sourceSlot + slot, samples, {0, 0}};
            commands->bindPipeline(bloomPipelines.prefilter);
            commands->setConstantBuffer(pass.buffer(view)->gpuAddress(), viewSize());
            commands->pushConstants(&constants.source, 16);
            commands->draw(3, 1);
        });

    // Down the chain, then back up it, each level reading its neighbour.
    for (uint32_t step = 0; step + 1 < levels * 2 - 1; ++step)
    {
        const bool down = step + 1 < levels;
        const uint32_t target = down ? step + 1 : levels * 2 - 3 - step;
        const uint32_t source = down ? target - 1 : target + 1;
        const std::array<float, 2> size = levelSize(source);
        const BloomPass constants{down ? bloomPipelines.downsample : bloomPipelines.upsample,
                                  levelSlots + source * framesInFlight,
                                  1,
                                  {1.0f / size[0], 1.0f / size[1]}};
        context.graph.addPass(
            down ? "BloomDownsample" : "BloomUpsample", slag::CommandQueueType::Graphics,
            [&](slag::rendergraph::PassBuilder& pass) {
                pass.use(chain, slag::rendergraph::Usage::ShaderRead, {.baseMip = source, .mipCount = 1});
                pass.renderTarget(chain, down ? slag::LoadOp::Discard : slag::LoadOp::Load, slag::StoreOp::Store, {},
                                  target);
            },
            [constants](slag::rendergraph::PassContext& pass, slag::CommandBuffer* commands) {
                BloomPass shader = constants;
                shader.source += pass.frameSlot();
                commands->bindPipeline(shader.pipeline);
                commands->pushConstants(&shader.source, 16);
                commands->draw(3, 1);
            });
    }
}

// ---------------------------------------------------------------------------
// Tonemap
// ---------------------------------------------------------------------------

slag::Pipeline* tonemapPipeline = nullptr;

Result<void> prepareTonemap(slag::GraphicsDevice& device, void*)
{
    const slag::PixelFormat formats[] = {backbufferFormat};
    Result<slag::Pipeline*> pipeline = createPipeline(
        device, "tonemap", "fullscreenVertex", "tonemapPixel",
        {.rasterizer = {.cullMode = slag::CullMode::None}, .colorFormats = formats, .debugName = "Tonemap"});
    if (!pipeline)
    {
        return pipeline.status();
    }
    tonemapPipeline = pipeline.value();
    return {};
}

void addTonemap(RenderStepContext& context, void*)
{
    const slag::rendergraph::RenderGraphTexture source = context.texture(sceneColorKey());
    const slag::rendergraph::RenderGraphTexture backbuffer = context.texture(backbufferKey());
    const slag::rendergraph::RenderGraphBuffer view = context.buffer(viewKey());
    slag::rendergraph::RenderGraphTexture bloom{};
    if (context.view.bloomLevels > 0)
    {
        bloom = context.texture(bloomKey());
    }
    // The surface's slot for this frame slot: the transient texture differs
    // from frame to frame, so it's written while recording.
    const uint32_t firstSlot = surfaceDescriptor(context, tonemapDescriptor);
    const uint32_t bloomSlot = surfaceDescriptor(context, bloomLevelsDescriptor);
    slag::GraphicsDevice* device = &context.device;
    context.graph.addPass(
        "Tonemap", slag::CommandQueueType::Graphics,
        [&](slag::rendergraph::PassBuilder& pass) {
            pass.use(view, slag::rendergraph::Usage::ConstantBuffer);
            pass.use(source, slag::rendergraph::Usage::ShaderRead);
            if (bloom.index != UINT32_MAX)
            {
                pass.use(bloom, slag::rendergraph::Usage::ShaderRead, {.baseMip = 0, .mipCount = 1});
            }
            pass.renderTarget(backbuffer, slag::LoadOp::Discard, slag::StoreOp::Store);
        },
        [=](slag::rendergraph::PassContext& pass, slag::CommandBuffer* commands) {
            const uint32_t slot = firstSlot + pass.frameSlot();
            device->writeTexture(slot, {.texture = pass.texture(source),
                                        .viewType = samples > 1 ? slag::TextureViewType::Texture2DMS
                                                                : slag::TextureViewType::Texture2D});
            const uint32_t constants[3] = {slot, samples, bloomSlot + pass.frameSlot()};
            commands->bindPipeline(tonemapPipeline);
            commands->setConstantBuffer(pass.buffer(view)->gpuAddress(), viewSize());
            commands->pushConstants(constants, sizeof(constants));
            commands->draw(3, 1);
        });
}

// A new schedule's first step (ADR 0066): the backbuffer cleared to
// renderFrame's color, for schedules without the scene's steps.
void addClear(RenderStepContext& context, void*)
{
    const slag::rendergraph::RenderGraphTexture backbuffer = context.texture(backbufferKey());
    slag::ClearValue clear{};
    for (int c = 0; c < 4; ++c)
    {
        clear.color[c] = context.clearColor[c];
    }
    context.graph.addPass(
        "Clear", slag::CommandQueueType::Graphics,
        [&](slag::rendergraph::PassBuilder& pass) {
            pass.renderTarget(backbuffer, slag::LoadOp::Clear, slag::StoreOp::Store, clear);
        },
        [](slag::rendergraph::PassContext&, slag::CommandBuffer*) {});
}

// ---------------------------------------------------------------------------
// The editor's steps (ADR 0067): Grid, Pick, SelectionOutline. They sit in
// the scene's schedule and draw only on surfaces that ask, each with its
// state here, by surface.
// ---------------------------------------------------------------------------

constexpr uint32_t editorSurfaces = 8; // as many as the renderer has
constexpr uint32_t readbackSize = 256; // a row's pitch

struct EditorView
{
    bool grid = false;
    std::array<uint32_t, maxOutlined> selected{}; // the outlined entities' indices plus one
    uint32_t selectedCount = 0;
    bool pickWanted = false;
    uint32_t pickX = 0;
    uint32_t pickY = 0;
    // Per frame slot: the pixel copied there, from which frame and world.
    struct Pending
    {
        uint64_t frame = 0;
        ecs::World* world = nullptr;
    };
    std::array<Pending, framesInFlight> pending{};
    std::array<slag::Buffer*, framesInFlight> readback{};
    PickResult result{};
};

std::array<EditorView, editorSurfaces> editorViews;

EditorView* editorView(uint32_t surface)
{
    return surface >= 1 && surface <= editorSurfaces ? &editorViews[surface - 1] : nullptr;
}

BlackboardKey entityIdsKey()
{
    static const BlackboardKey key = registerBlackboardKey("EntityIds");
    return key;
}

slag::Pipeline* gridPipeline = nullptr;
slag::Pipeline* pickPipeline = nullptr;
slag::Pipeline* outlinePipeline = nullptr;
Vector<Draw> pickDraws{MemoryTag::Render};

void destroyEditorPipeline(slag::GraphicsDevice& device, void* data)
{
    auto** pipeline = static_cast<slag::Pipeline**>(data);
    if (*pipeline != nullptr)
    {
        device.destroy(*pipeline);
        *pipeline = nullptr;
    }
}

Result<void> prepareGrid(slag::GraphicsDevice& device, void*)
{
    const slag::PixelFormat formats[] = {sceneColorFormat};
    slag::GraphicsPipelineDesc desc{
        .rasterizer = {.cullMode = slag::CullMode::None},
        // Its own depth (the ground's), tested against the scene, not written.
        .depthStencil = {.depthTest = true, .depthWrite = false, .depthCompareOp = slag::CompareOp::GreaterEqual},
        .colorFormats = formats,
        .depthStencilFormat = sceneDepthFormat,
        .sampleCount = samples,
        .debugName = "Grid"};
    desc.blend.targets[0] = {.blendEnable = true,
                             .srcColorFactor = slag::BlendFactor::SrcAlpha,
                             .destColorFactor = slag::BlendFactor::InvSrcAlpha,
                             .srcAlphaFactor = slag::BlendFactor::Zero,
                             .destAlphaFactor = slag::BlendFactor::One};
    Result<slag::Pipeline*> pipeline = createPipeline(device, "editorOverlay", "overlayVertex", "gridPixel", desc);
    if (!pipeline)
    {
        return pipeline.status();
    }
    gridPipeline = pipeline.value();
    return {};
}

void addGrid(RenderStepContext& context, void*)
{
    const EditorView* editor = editorView(context.surface);
    if (editor == nullptr || !editor->grid)
    {
        return;
    }
    const slag::rendergraph::RenderGraphTexture color = context.texture(sceneColorKey());
    const slag::rendergraph::RenderGraphTexture depth = context.texture(sceneDepthKey());
    const slag::rendergraph::RenderGraphBuffer view = context.buffer(viewKey());
    context.graph.addPass(
        "Grid", slag::CommandQueueType::Graphics,
        [&](slag::rendergraph::PassBuilder& pass) {
            pass.use(view, slag::rendergraph::Usage::ConstantBuffer);
            pass.renderTarget(color, slag::LoadOp::Load, slag::StoreOp::Store);
            pass.depthStencil(depth, slag::LoadOp::Load, slag::StoreOp::Store, {}, true);
        },
        [view](slag::rendergraph::PassContext& pass, slag::CommandBuffer* commands) {
            commands->bindPipeline(gridPipeline);
            commands->setConstantBuffer(pass.buffer(view)->gpuAddress(), viewSize());
            commands->draw(3, 1);
        });
}

Result<void> preparePick(slag::GraphicsDevice& device, void*)
{
    const slag::PixelFormat formats[] = {slag::PixelFormat::R32Uint};
    Result<slag::Pipeline*> pipeline = createPipeline(
        device, "editor", "pickVertex", "pickPixel",
        {.depthStencil = {.depthTest = true, .depthWrite = true, .depthCompareOp = slag::CompareOp::GreaterEqual},
         .colorFormats = formats,
         .depthStencilFormat = sceneDepthFormat,
         .debugName = "Pick"});
    if (!pipeline)
    {
        return pipeline.status();
    }
    pickPipeline = pipeline.value();
    return {};
}

// The opaque instances' entity indices plus one, at one sample per pixel
// with a depth of their own, published as EntityIds; and, when a pick was
// asked for, that pixel copied into this frame slot's readback buffer.
void addPick(RenderStepContext& context, void*)
{
    EditorView* editor = editorView(context.surface);
    if (editor == nullptr || (!editor->pickWanted && editor->selectedCount == 0))
    {
        return;
    }
    const slag::rendergraph::RenderGraphTexture ids =
        context.graph.createTransientTexture2D({.format = slag::PixelFormat::R32Uint,
                                                .width = context.width,
                                                .height = context.height,
                                                .usage = slag::TextureUsage::RenderTarget,
                                                .debugName = "EntityIds"});
    const slag::rendergraph::RenderGraphTexture depth =
        context.graph.createTransientTexture2D({.format = sceneDepthFormat,
                                                .width = context.width,
                                                .height = context.height,
                                                .usage = slag::TextureUsage::DepthStencil,
                                                .debugName = "PickDepth"});
    context.publish(entityIdsKey(), ids);
    const slag::rendergraph::RenderGraphBuffer view = context.buffer(viewKey());
    gatherDraws(context.draws, Pass::Depth, pickDraws);
    for (Draw& draw : pickDraws)
    {
        draw.pipeline = pickPipeline;
    }
    const DrawList* draws = &context.draws;
    context.graph.addPass(
        "Pick", slag::CommandQueueType::Graphics,
        [&](slag::rendergraph::PassBuilder& pass) {
            pass.use(view, slag::rendergraph::Usage::ConstantBuffer);
            pass.renderTarget(ids, slag::LoadOp::Clear, slag::StoreOp::Store, {});
            pass.depthStencil(depth, slag::LoadOp::Clear, slag::StoreOp::Discard, {.depth = 0.0f});
        },
        [draws, view](slag::rendergraph::PassContext& pass, slag::CommandBuffer* commands) {
            commands->setConstantBuffer(pass.buffer(view)->gpuAddress(), viewSize());
            recordDraws(commands, *draws, pickDraws);
        });
    if (!editor->pickWanted)
    {
        return;
    }
    const uint32_t slot = frameSlotOf(context);
    if (editor->readback[slot] == nullptr)
    {
        slag::Result<slag::Buffer*> buffer =
            context.device.createBuffer({.size = readbackSize,
                                         .placement = slag::Placement::automatic(slag::MemoryKind::Readback),
                                         .debugName = "crucible pick readback"});
        if (!buffer)
        {
            return;
        }
        editor->readback[slot] = buffer.value();
    }
    const slag::rendergraph::RenderGraphBuffer readback = context.graph.importBuffer(editor->readback[slot]);
    const uint32_t x = std::min(editor->pickX, context.width - 1), y = std::min(editor->pickY, context.height - 1);
    context.graph.addPass(
        "PickCopy", slag::CommandQueueType::Graphics,
        [&](slag::rendergraph::PassBuilder& pass) {
            pass.use(ids, slag::rendergraph::Usage::CopySource);
            pass.use(readback, slag::rendergraph::Usage::CopyDest);
        },
        [ids, readback, x, y](slag::rendergraph::PassContext& pass, slag::CommandBuffer* commands) {
            commands->copyTextureToBuffer({pass.buffer(readback), 0, readbackSize},
                                          {.texture = pass.texture(ids), .x = x, .y = y}, 1, 1);
        });
    editor->pending[slot] = {context.frame, context.world};
    editor->pickWanted = false;
}

Result<void> prepareOutline(slag::GraphicsDevice& device, void*)
{
    const slag::PixelFormat formats[] = {backbufferFormat};
    Result<slag::Pipeline*> pipeline = createPipeline(
        device, "editorOverlay", "overlayVertex", "outlinePixel",
        {.rasterizer = {.cullMode = slag::CullMode::None}, .colorFormats = formats, .debugName = "SelectionOutline"});
    if (!pipeline)
    {
        return pipeline.status();
    }
    outlinePipeline = pipeline.value();
    return {};
}

// The selection's edges, from EntityIds, over the finished image.
void addSelectionOutline(RenderStepContext& context, void*)
{
    const EditorView* editor = editorView(context.surface);
    if (editor == nullptr || editor->selectedCount == 0)
    {
        return;
    }
    const slag::rendergraph::RenderGraphTexture ids = context.texture(entityIdsKey());
    const slag::rendergraph::RenderGraphTexture backbuffer = context.texture(backbufferKey());
    const uint32_t firstSlot = surfaceDescriptor(context, entityIdsDescriptor);
    struct OutlineConstants
    {
        uint32_t ids, count, pad0, pad1;
        float color[4];
        uint32_t selected[maxOutlined];
    };
    OutlineConstants outline{0, editor->selectedCount, 0, 0, {1.0f, 0.42f, 0.05f, 1.0f}, {}}; // orange, linear
    std::copy(editor->selected.begin(), editor->selected.end(), outline.selected);
    static_assert(sizeof(OutlineConstants) <= 128);
    slag::GraphicsDevice* device = &context.device;
    context.graph.addPass(
        "SelectionOutline", slag::CommandQueueType::Graphics,
        [&](slag::rendergraph::PassBuilder& pass) {
            pass.use(ids, slag::rendergraph::Usage::ShaderRead);
            pass.renderTarget(backbuffer, slag::LoadOp::Load, slag::StoreOp::Store);
        },
        [=](slag::rendergraph::PassContext& pass, slag::CommandBuffer* commands) {
            const uint32_t slot = firstSlot + pass.frameSlot();
            device->writeTexture(slot, {.texture = pass.texture(ids)});
            OutlineConstants constants = outline;
            constants.ids = slot;
            commands->bindPipeline(outlinePipeline);
            commands->pushConstants(&constants, sizeof(constants));
            commands->draw(3, 1);
        });
}

} // namespace

void releaseEditorView(uint32_t surface)
{
    if (EditorView* editor = editorView(surface))
    {
        for (slag::Buffer*& buffer : editor->readback)
        {
            if (buffer != nullptr)
            {
                renderDevice()->destroy(buffer);
            }
        }
        *editor = EditorView{};
    }
}

Result<void> addEditorStepsTo(RenderSchedule& schedule)
{
    if (Result<void> added = schedule.insertAfter("Sky", {.name = "Grid",
                                                          .addPasses = addGrid,
                                                          .userData = &gridPipeline,
                                                          .prepare = prepareGrid,
                                                          .release = destroyEditorPipeline});
        !added)
    {
        return added;
    }
    if (Result<void> added = schedule.insertAfter("Opaque", {.name = "Pick",
                                                             .addPasses = addPick,
                                                             .userData = &pickPipeline,
                                                             .prepare = preparePick,
                                                             .release = destroyEditorPipeline});
        !added)
    {
        return added;
    }
    return schedule.insertAfter("Tonemap", {.name = "SelectionOutline",
                                            .addPasses = addSelectionOutline,
                                            .userData = &outlinePipeline,
                                            .prepare = prepareOutline,
                                            .release = destroyEditorPipeline});
}

void setSurfaceGrid(SurfaceHandle surface, bool shown)
{
    if (EditorView* editor = editorView(surface.value))
    {
        editor->grid = shown;
    }
}

void setSurfaceSelection(SurfaceHandle surface, std::span<const ecs::Entity> entities)
{
    if (EditorView* editor = editorView(surface.value))
    {
        editor->selectedCount = 0;
        for (const ecs::Entity entity : entities)
        {
            if (entity != ecs::nullEntity && editor->selectedCount < maxOutlined)
            {
                editor->selected[editor->selectedCount++] = ecs::indexOf(entity) + 1;
            }
        }
    }
}

void requestPick(SurfaceHandle surface, uint32_t x, uint32_t y)
{
    if (EditorView* editor = editorView(surface.value))
    {
        editor->pickWanted = true;
        editor->pickX = x;
        editor->pickY = y;
    }
}

PickResult pickResult(SurfaceHandle surface)
{
    EditorView* editor = editorView(surface.value);
    if (editor == nullptr)
    {
        return {};
    }
    // Each finished frame's pixel, newest last; its slot is free again after.
    const uint64_t done = surfaceFramesDone(surface.value);
    for (uint32_t slot = 0; slot < framesInFlight; ++slot)
    {
        EditorView::Pending& pending = editor->pending[slot];
        if (pending.frame == 0 || pending.frame > done || editor->readback[slot] == nullptr)
        {
            continue;
        }
        if (pending.frame > editor->result.frame)
        {
            uint32_t id = 0;
            std::memcpy(&id, editor->readback[slot]->mappedData(), sizeof(id));
            const ecs::Entity entity =
                id != 0 && pending.world != nullptr ? ecs::entityAtIndex(*pending.world, id - 1) : ecs::nullEntity;
            editor->result = {pending.frame, entity, entity != ecs::nullEntity};
        }
        pending = {};
    }
    return editor->result;
}

void addClearStep(RenderSchedule& schedule)
{
    (void)schedule.add({.name = "Clear", .addPasses = addClear});
}

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
    (void)schedule.add({.name = "Outlines", .addPasses = addOutlines});
    (void)schedule.add({.name = "Sky",
                        .addPasses = addSky,
                        .userData = &skyPipeline,
                        .prepare = prepareSky,
                        .release = destroyPipeline});
    (void)schedule.add({.name = "Transparent", .addPasses = addTransparent});
    (void)schedule.add({.name = "Bloom", .addPasses = addBloom, .prepare = prepareBloom, .release = releaseBloom});
    (void)schedule.add({.name = "Tonemap",
                        .addPasses = addTonemap,
                        .userData = &tonemapPipeline,
                        .prepare = prepareTonemap,
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
