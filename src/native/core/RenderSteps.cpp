// The default render steps (ADR 0048, 0051): DepthPrepass draws the draw
// list's depth into SceneDepth; Opaque clears SceneColor to renderFrame's
// color and draws each submesh's unlit base color, testing against the
// prepass's depth; Resolve averages SceneColor's samples into the backbuffer.
// SceneColor and SceneDepth are MSAA transients at msaaSamples().

#include "RenderInternal.h"

#include <crucible/Render.h>
#include <crucible/Shader.h>

#include "mesh.g.h" // DrawConstants

#include <cstdio>

namespace crucible::render {

namespace {

using shaders::DrawConstants;

constexpr slag::PixelFormat sceneColorFormat = slag::PixelFormat::R16G16B16A16Float;
constexpr slag::PixelFormat sceneDepthFormat = slag::PixelFormat::D32Float;

uint32_t samples = 4;

// The pipeline built from an embedded shader's entry points; pixel may be
// null (depth only).
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
    const int32_t pixel = pixelEntry != nullptr ? findShaderEntry(*file.value(), pixelEntry) : -1;
    if (vertex < 0 || (pixelEntry != nullptr && pixel < 0))
    {
        destroyShaderFile(file.value());
        return Status::InvalidData;
    }
    shaderStage(*file.value(), static_cast<uint32_t>(vertex), desc.vertex);
    if (pixel >= 0)
    {
        shaderStage(*file.value(), static_cast<uint32_t>(pixel), desc.pixel);
    }
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

// Clockwise front faces (ADR 0024), back faces culled.
constexpr slag::RasterizerState meshRasterizer{.cullMode = slag::CullMode::Back, .frontCounterClockwise = false};

// Every batch's submeshes; with `material`, each one's base color too.
void drawBatches(slag::CommandBuffer* commands, const DrawList& draws, bool material)
{
    const uint32_t sampler = samplerDescriptor(Sampler::LinearRepeat);
    for (const DrawBatch& batch : draws.batches)
    {
        const MeshData& model = *batch.model;
        const assets::MeshEntry& mesh = model.meshes[batch.mesh];
        const assets::MeshLod& lod = model.lods[mesh.lodOffset + batch.lod];
        commands->bindIndexBuffer(model.buffer, model.indices.offset, model.indices.size,
                                  mesh.indexSize == 2 ? slag::IndexFormat::Uint16 : slag::IndexFormat::Uint32);
        const bool floatUvs = (model.info.floatUvs & 1) != 0;
        DrawConstants constants{};
        constants.model = model.descriptor;
        constants.positions = static_cast<uint32_t>(model.positions.offset + uint64_t{mesh.vertexOffset} * 12);
        constants.uv0 = static_cast<uint32_t>(model.uv0.offset + uint64_t{mesh.vertexOffset} * (floatUvs ? 8 : 4));
        constants.colors = static_cast<uint32_t>(model.colors.offset + uint64_t{mesh.vertexOffset} * 4);
        constants.attributes = mesh.attributes;
        constants.floatUvs = floatUvs ? 1 : 0;
        constants.instances = draws.instances;
        constants.transforms = draws.transforms;
        constants.baseColorSampler = sampler;
        for (uint32_t submesh = 0; submesh < mesh.submeshCount; ++submesh)
        {
            const assets::MeshRange& range = model.ranges[lod.rangeOffset + submesh];
            if (range.indexCount == 0)
            {
                continue;
            }
            constants.baseColor = {1, 1, 1, 1};
            constants.baseColorTexture = 0;
            if (material && range.material >= 0 && static_cast<size_t>(range.material) < model.materials.size())
            {
                const auto m = static_cast<size_t>(range.material);
                const float* color = model.materials[m].baseColor;
                constants.baseColor = {color[0], color[1], color[2], color[3]};
                constants.baseColorTexture = m < batch.baseColorTextures.size() ? batch.baseColorTextures[m] : 0;
            }
            commands->pushConstants(&constants, sizeof(constants));
            commands->drawIndexed(range.indexCount, batch.instanceCount, range.indexByteOffset / mesh.indexSize, 0,
                                  batch.firstInstance);
        }
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

slag::Pipeline* depthPipeline = nullptr;

Result<void> prepareDepthPrepass(slag::GraphicsDevice& device, void*)
{
    Result<slag::Pipeline*> pipeline = createPipeline(
        device, "mesh", "depthVertex", nullptr,
        {.rasterizer = meshRasterizer,
         // Reverse-Z (ADR 0050): nearer is greater.
         .depthStencil = {.depthTest = true, .depthWrite = true, .depthCompareOp = slag::CompareOp::Greater},
         .depthStencilFormat = sceneDepthFormat,
         .sampleCount = samples,
         .debugName = "DepthPrepass"});
    if (!pipeline)
    {
        return pipeline.status();
    }
    depthPipeline = pipeline.value();
    return {};
}

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
    const DrawList* draws = &context.draws;
    context.graph.addPass(
        "DepthPrepass", slag::CommandQueueType::Graphics,
        [&](slag::rendergraph::PassBuilder& pass) {
            pass.use(view, slag::rendergraph::Usage::ConstantBuffer);
            pass.depthStencil(depth, slag::LoadOp::Clear, slag::StoreOp::Store, {.depth = 0.0f});
        },
        [draws, view](slag::rendergraph::PassContext& pass, slag::CommandBuffer* commands) {
            commands->bindPipeline(depthPipeline);
            commands->setConstantBuffer(pass.buffer(view)->gpuAddress(), viewSize());
            drawBatches(commands, *draws, false);
        });
}

// ---------------------------------------------------------------------------
// Opaque
// ---------------------------------------------------------------------------

slag::Pipeline* opaquePipeline = nullptr;

Result<void> prepareOpaque(slag::GraphicsDevice& device, void*)
{
    const slag::PixelFormat formats[] = {sceneColorFormat};
    Result<slag::Pipeline*> pipeline = createPipeline(
        device, "mesh", "opaqueVertex", "opaquePixel",
        {.rasterizer = meshRasterizer,
         // The prepass wrote the depth; GreaterEqual rather than Equal, so a
         // last-bit difference between the two pipelines' positions can't
         // drop a pixel.
         .depthStencil = {.depthTest = true, .depthWrite = false, .depthCompareOp = slag::CompareOp::GreaterEqual},
         .colorFormats = formats,
         .depthStencilFormat = sceneDepthFormat,
         .sampleCount = samples,
         .debugName = "Opaque"});
    if (!pipeline)
    {
        return pipeline.status();
    }
    opaquePipeline = pipeline.value();
    return {};
}

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
    const DrawList* draws = &context.draws;
    context.graph.addPass(
        "Opaque", slag::CommandQueueType::Graphics,
        [&](slag::rendergraph::PassBuilder& pass) {
            pass.use(view, slag::rendergraph::Usage::ConstantBuffer);
            pass.renderTarget(color, slag::LoadOp::Clear, slag::StoreOp::Store, clear);
            pass.depthStencil(depth, slag::LoadOp::Load, slag::StoreOp::Store, {}, true);
        },
        [draws, view](slag::rendergraph::PassContext& pass, slag::CommandBuffer* commands) {
            commands->bindPipeline(opaquePipeline);
            commands->setConstantBuffer(pass.buffer(view)->gpuAddress(), viewSize());
            drawBatches(commands, *draws, true);
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
    (void)schedule.add({.name = "DepthPrepass",
                        .addPasses = addDepthPrepass,
                        .userData = &depthPipeline,
                        .prepare = prepareDepthPrepass,
                        .release = destroyPipeline});
    (void)schedule.add({.name = "Opaque",
                        .addPasses = addOpaque,
                        .userData = &opaquePipeline,
                        .prepare = prepareOpaque,
                        .release = destroyPipeline});
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
    return schedule.prepareAll(*device);
}

uint32_t msaaSamples()
{
    return samples;
}

} // namespace crucible::render
