#pragma once

// Between the renderer (Render.cpp), the render schedule, and the default
// steps (ADR 0048). Internal.

#include <crucible/RenderSchedule.h>

namespace crucible::render {

struct FrameInputs
{
    slag::rendergraph::RenderGraph& graph;
    slag::GraphicsDevice& device;
    uint32_t width;
    uint32_t height;
    uint64_t frame;
    const float* clearColor; // 4
    slag::rendergraph::TextureHandle backbuffer;
};

// Runs every step of the schedule for one frame. InvalidData (already
// reported) when a step read a blackboard entry no one published.
Result<void> addStepPasses(const FrameInputs& inputs);

// Clear, then Triangle (RenderSteps.cpp).
void addDefaultSteps(RenderSchedule& schedule);

// The backbuffer's view format: the surfaces' sRGB view (Render.cpp).
inline constexpr slag::PixelFormat backbufferFormat = slag::PixelFormat::B8G8R8A8UnormSrgb;

} // namespace crucible::render
