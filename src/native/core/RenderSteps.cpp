// The default render steps (ADR 0048, 0051, 0052): DepthPrepass draws the
// draw list's depth into SceneDepth; Opaque clears SceneColor to
// renderFrame's color and draws each submesh with its material, testing
// against the prepass's depth; Resolve averages SceneColor's samples into
// the backbuffer. SceneColor and SceneDepth are MSAA transients at
// msaaSamples().

#include "RenderInternal.h"

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
};

// Every batch's submeshes, ordered by pipeline so the GPU switches state as
// little as possible; kept per step, reused frame to frame.
void gatherDraws(const DrawList& draws, bool depth, Vector<Draw>& out)
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
            out.pushBack(
                {&batch, submesh, depth ? material.pipelines->depth : material.pipelines->forward, material.offset});
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
        commands->pushConstants(&constants, sizeof(constants));
        commands->drawIndexed(range.indexCount, batch.instanceCount, range.indexByteOffset / mesh.indexSize, 0,
                              batch.firstInstance);
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
    gatherDraws(context.draws, true, depthDraws);
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
// Opaque
// ---------------------------------------------------------------------------

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
    slag::ClearValue clear{};
    for (int c = 0; c < 4; ++c)
    {
        clear.color[c] = context.clearColor[c];
    }
    gatherDraws(context.draws, false, opaqueDraws);
    const DrawList* draws = &context.draws;
    context.graph.addPass(
        "Opaque", slag::CommandQueueType::Graphics,
        [&](slag::rendergraph::PassBuilder& pass) {
            pass.use(view, slag::rendergraph::Usage::ConstantBuffer);
            pass.renderTarget(color, slag::LoadOp::Clear, slag::StoreOp::Store, clear);
            pass.depthStencil(depth, slag::LoadOp::Load, slag::StoreOp::Store, {}, true);
        },
        [draws, view](slag::rendergraph::PassContext& pass, slag::CommandBuffer* commands) {
            commands->setConstantBuffer(pass.buffer(view)->gpuAddress(), viewSize());
            recordDraws(commands, *draws, opaqueDraws);
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
    (void)schedule.add({.name = "Opaque", .addPasses = addOpaque});
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
