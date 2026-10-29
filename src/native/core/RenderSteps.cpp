// The default render steps (ADR 0048), temporary until M5 step 5 draws
// meshes: Clear clears the backbuffer to renderFrame's color, Triangle draws
// one triangle over it with an embedded engine shader.

#include "RenderInternal.h"

#include <crucible/Shader.h>

#include <cstdio>

namespace crucible::render {

namespace {

// ---------------------------------------------------------------------------
// Clear
// ---------------------------------------------------------------------------

void addClear(RenderStepContext& context, void*)
{
    const slag::rendergraph::TextureHandle backbuffer = context.texture(backbufferKey());
    slag::ClearValue clear{};
    for (int c = 0; c < 4; ++c)
    {
        clear.color[c] = context.clearColor[c];
    }
    // A pass with no commands: the graph clears as it begins rendering.
    context.graph.addPass(
        "Clear", slag::CommandQueueType::Graphics,
        [&](slag::rendergraph::PassBuilder& pass) {
            pass.renderTarget(backbuffer, slag::LoadOp::Clear, slag::StoreOp::Store, clear);
        },
        [](slag::rendergraph::PassContext&, slag::CommandBuffer*) {});
}

// ---------------------------------------------------------------------------
// Triangle
// ---------------------------------------------------------------------------

struct TriangleStep
{
    slag::Pipeline* pipeline = nullptr;
};

TriangleStep triangle;

Result<void> prepareTriangle(slag::GraphicsDevice& device, void* userData)
{
    auto& step = *static_cast<TriangleStep*>(userData);
    Result<ShaderFile*> file = loadShaderFile(engineShader("triangle"));
    if (!file)
    {
        std::fprintf(stderr, "crucible: the embedded triangle shader can't be read\n");
        return file.status();
    }
    slag::ShaderStage vertex, pixel;
    const int32_t vertexEntry = findShaderEntry(*file.value(), "vertexMain");
    const int32_t pixelEntry = findShaderEntry(*file.value(), "pixelMain");
    if (vertexEntry < 0 || pixelEntry < 0)
    {
        destroyShaderFile(file.value());
        return Status::InvalidData;
    }
    shaderStage(*file.value(), static_cast<uint32_t>(vertexEntry), vertex);
    shaderStage(*file.value(), static_cast<uint32_t>(pixelEntry), pixel);
    const slag::PixelFormat formats[] = {backbufferFormat};
    // Clockwise front faces (ADR 0024), back faces culled: the shader's
    // corners are clockwise, so a winding mistake shows as no triangle.
    slag::Result<slag::Pipeline*> pipeline =
        device.createGraphicsPipeline({.vertex = vertex,
                                       .pixel = pixel,
                                       .rasterizer = {.cullMode = slag::CullMode::Back, .frontCounterClockwise = false},
                                       .colorFormats = formats,
                                       .debugName = "Triangle"});
    destroyShaderFile(file.value());
    if (!pipeline)
    {
        return Status::Unsupported;
    }
    step.pipeline = pipeline.value();
    return {};
}

void releaseTriangle(slag::GraphicsDevice& device, void* userData)
{
    auto& step = *static_cast<TriangleStep*>(userData);
    if (step.pipeline != nullptr)
    {
        device.destroy(step.pipeline);
        step.pipeline = nullptr;
    }
}

void addTriangle(RenderStepContext& context, void* userData)
{
    const slag::rendergraph::TextureHandle backbuffer = context.texture(backbufferKey());
    slag::Pipeline* pipeline = static_cast<TriangleStep*>(userData)->pipeline;
    context.graph.addPass(
        "Triangle", slag::CommandQueueType::Graphics,
        [&](slag::rendergraph::PassBuilder& pass) { pass.renderTarget(backbuffer); },
        [pipeline](slag::rendergraph::PassContext&, slag::CommandBuffer* commands) {
            commands->bindPipeline(pipeline);
            commands->draw(3, 1);
        });
}

} // namespace

void addDefaultSteps(RenderSchedule& schedule)
{
    (void)schedule.add({.name = "Clear", .addPasses = addClear});
    (void)schedule.add({.name = "Triangle",
                        .addPasses = addTriangle,
                        .userData = &triangle,
                        .prepare = prepareTriangle,
                        .release = releaseTriangle});
}

} // namespace crucible::render
