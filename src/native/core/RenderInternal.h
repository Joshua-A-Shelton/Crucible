#pragma once

// Between the renderer (Render.cpp), the render schedule, and the default
// steps (ADR 0048). Internal.

#include <crucible/RenderAssets.h>
#include <crucible/RenderSchedule.h>

#include "shared.g.h" // ViewConstants, generated from shaders/crucible/shared.slang

namespace crucible::ecs {
struct World;
}

namespace crucible::render {

struct FrameInputs
{
    slag::rendergraph::RenderGraph& graph;
    slag::GraphicsDevice& device;
    uint32_t width;
    uint32_t height;
    uint64_t frame;
    const float* clearColor; // 4
    slag::rendergraph::RenderGraphTexture backbuffer;
    slag::rendergraph::RenderGraphBuffer view; // the frame's ViewConstants
};

// Runs every step of the schedule for one frame. InvalidData (already
// reported) when a step read a blackboard entry no one published.
Result<void> addStepPasses(const FrameInputs& inputs);

// Clear, then Triangle (RenderSteps.cpp).
void addDefaultSteps(RenderSchedule& schedule);

// RenderAssets.cpp's part of the renderer (ADR 0049): started after the
// device, collected every frame, stopped before the device shuts down.
Result<void> startAssets(slag::GraphicsDevice& device, slag::CommandQueue& queue);
void collectAssets();
void stopAssets();
// The next value of the work semaphore, which every frame and upload
// signals; unloading waits for the last one signalled.
slag::SemaphoreValue workSignal();

// For tests: the renderer's device and queue (null before it starts), and
// uploading through staging even where direct writes are possible.
slag::GraphicsDevice* renderDevice();
slag::CommandQueue* renderQueue();
void forceStagingUploads(bool force);
slag::Texture* textureOf(TextureHandle texture);

// The frame's view, from the world's chosen camera (Camera.cpp): the one with
// the highest priority, or a default one when the world (which may be null)
// has none.
ViewConstants viewConstants(ecs::World* world, uint32_t width, uint32_t height, float time);

// The backbuffer's view format: the surfaces' sRGB view (Render.cpp).
inline constexpr slag::PixelFormat backbufferFormat = slag::PixelFormat::B8G8R8A8UnormSrgb;

} // namespace crucible::render
