#pragma once

// The renderer's foundation (ADR 0015): one Slag device, and a surface per
// window that renders the render schedule's steps (ADR 0048) and presents
// with the latency rules of ADR 0003. Names are placeholders unless listed
// in docs/naming.md.
//
// Threading: like the platform layer, everything runs on the launcher's main
// thread.

#include <crucible/Camera.h>
#include <crucible/Ecs.h>
#include <crucible/Export.h>
#include <crucible/Expose.h>
#include <crucible/Platform.h>
#include <crucible/RenderAssets.h>
#include <crucible/Result.h>

#include <cstdint>
#include <span>

namespace crucible::ecs {
struct World;
}

namespace slag {
enum class Backend : uint8_t;
enum class PresentMode : uint8_t;
} // namespace slag

namespace crucible::render {

// A window's swapchain and per-frame resources (never 0; 0 means none).
struct CRUCIBLE_EXPOSE SurfaceHandle
{
    uint32_t value;
};

// The launcher's --present-mode: Default follows ADR 0003 (Mailbox where
// supported, else Fifo); the others force a mode where the window supports it.
enum class PresentPreference : uint8_t
{
    Default,
    Fifo,
    Mailbox,
};

// Called by the launcher after platform::initialize and before the engine
// starts, and after it returns (ADR 0011), so not exposed to C#. Creates the
// Slag instance and initializes the first device it lists.
CRUCIBLE_EXPORT Result<void> initialize(slag::Backend backend,
                                        PresentPreference presentMode = PresentPreference::Default);
CRUCIBLE_EXPORT void shutdown();

// Creates the window's swapchain (ADR 0003: Mailbox, else double-buffered
// Fifo). The surface must be destroyed before its window.
CRUCIBLE_EXPOSE Result<SurfaceHandle> createSurface(platform::WindowHandle window);

// A surface without a window: the same frames, rendered into a texture
// instead of a swapchain, for headless runs and tests. Presenting is skipped,
// and pacing waits for the GPU to finish the previous frame.
CRUCIBLE_EXPOSE Result<SurfaceHandle> createOffscreenSurface(uint32_t width, uint32_t height);

CRUCIBLE_EXPOSE void destroySurface(SurfaceHandle surface);

// A surface's own camera (ADR 0067), drawn from instead of the world's: an
// editor's viewport looks through a camera that isn't in the scene. The
// world's chosen camera still gives the post-processing (PostProcess), so
// the view looks like the game's. NotFound for a stale surface.
CRUCIBLE_EXPOSE_STATUS Result<void> setSurfaceCamera(SurfaceHandle surface, Vector3 position, Quaternion rotation,
                                                     Camera camera);
CRUCIBLE_EXPOSE void clearSurfaceCamera(SurfaceHandle surface);

// An offscreen surface's image as a texture the UI can draw (ADR 0067). From
// the first call on, the surface leaves each frame ready to sample, and its
// next frame starts after the last submit that read it (one queue). The
// handle lives as long as the surface; NotFound for a windowed or stale one.
CRUCIBLE_EXPOSE_STATUS Result<TextureHandle> surfaceTexture(SurfaceHandle surface);

// A new size for an offscreen surface (waits for the GPU); its texture
// handle stays the same. NotFound for a windowed or stale surface,
// InvalidData for a zero size.
CRUCIBLE_EXPOSE_STATUS Result<void> resizeOffscreenSurface(SurfaceHandle surface, uint32_t width, uint32_t height);

// The editor's view of a surface (ADR 0067), drawn by the editor's steps
// (RenderCommands.h's addEditorSteps) on surfaces that ask: the ground
// grid; the selection outlined (the meshes of up to maxOutlined entities,
// such as a prefab instance's parts; none for no outline); and picking.
inline constexpr uint32_t maxOutlined = 16;
CRUCIBLE_EXPOSE void setSurfaceGrid(SurfaceHandle surface, bool shown);
CRUCIBLE_EXPOSE void setSurfaceSelection(SurfaceHandle surface, std::span<const ecs::Entity> entities);

// What was drawn at a pixel: the entity, from the frame that looked.
struct CRUCIBLE_EXPOSE PickResult
{
    uint64_t frame; // the surface's frame it came from (0: no answer yet)
    ecs::Entity entity;
    bool hit; // false where nothing opaque was drawn
    // The surface's depth there (ADR 0071): reverse-Z, so near / depth is the
    // distance along the view axis; 0 where nothing was drawn.
    float depth;
};

// Asks the next frame which entity is drawn at a pixel (from the top left);
// the answer comes once the GPU has finished that frame, a frame or two
// later. pickResult is the newest answer.
CRUCIBLE_EXPOSE void requestPick(SurfaceHandle surface, uint32_t x, uint32_t y);
CRUCIBLE_EXPOSE PickResult pickResult(SurfaceHandle surface);

// Entities picking looks through (ADR 0071): a model being placed, which
// would otherwise always be what's under the pointer. Replaces the last
// list; empty for none.
CRUCIBLE_EXPOSE void setSurfacePickExclusion(SurfaceHandle surface, std::span<const ecs::Entity> entities);

// The render schedule the surface renders (ADR 0066), by ID
// (RenderCommands.h's createSchedule; 0 for the default). NotFound for a
// stale surface or an unknown schedule.
CRUCIBLE_EXPOSE_INTERNAL Result<void> setSurfaceSchedule(SurfaceHandle surface, uint32_t schedule);

// Copies an offscreen surface's last frame into `pixels`: rows top to bottom,
// 4 bytes per pixel in B8G8R8A8 order, sRGB-encoded, tightly packed (width * 4
// bytes per row). Waits for the GPU. InvalidData if `pixels` is too small,
// NotFound before the first frame or for a windowed surface. For tests and
// screenshots, not per frame.
CRUCIBLE_EXPOSE Result<void> readSurfacePixels(SurfaceHandle surface, std::span<uint8_t> pixels);

// The start of a frame (ADR 0003's just-in-time pacing): waits until a new
// frame may start, so input read afterwards is as fresh as possible. Uses
// Slag's frame latency capability, or else waits for the GPU to finish the
// previous frame.
CRUCIBLE_EXPOSE_STATUS Result<void> waitForNextFrame(SurfaceHandle surface);

// Renders one frame through the render schedule (ADR 0048), with
// `red`..`alpha` (linear) as the Clear step's color, and presents it. The
// view is the world's camera with the highest priority (ADR 0050); without a
// world (null) or a camera, a default one at the origin looking along +Z.
// Resizes the swapchain when the window's size changed; skips the frame
// (still Ok) while the window is minimized or hidden.
//
// lastRunVersion is the render system's previous run on this world
// (FrameContext.LastRunVersion; 0 for a world not drawn before): mesh
// instances whose model changed since are resolved again (ADR 0051).
CRUCIBLE_EXPOSE_STATUS Result<void> renderFrame(SurfaceHandle surface, ecs::World* world, uint32_t lastRunVersion,
                                                float red, float green, float blue, float alpha);

// The default steps' MSAA sample count (ADR 0051): 1, 2, 4 (the default),
// or 8. Changing it while the renderer runs waits for the GPU and prepares
// every step again. Unsupported for another count, or one the device can't
// render the scene's formats with.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE Result<void> setMsaaSamples(uint32_t samples);
CRUCIBLE_EXPOSE uint32_t msaaSamples();

// Shadow quality (ADR 0054): each of the sun's cascades is a square map this
// many texels wide (default 2048), and each surface's shadow atlas, holding
// point and spot lights' tiles, this many (default 4096). Powers of two from
// 256 to 8192; Unsupported for others. Changing the atlas's size while the
// renderer runs waits for the GPU, and every tile is drawn again.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE Result<void> setShadowMapSize(uint32_t size);
CRUCIBLE_EXPOSE uint32_t shadowMapSize();
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE Result<void> setShadowAtlasSize(uint32_t size);
CRUCIBLE_EXPOSE uint32_t shadowAtlasSize();

// Latency harness support (ADR 0003).
//
// How many frames the surface has submitted; a frame that was skipped
// (minimized, occluded) doesn't change it.
CRUCIBLE_EXPOSE uint64_t framesSubmitted(SurfaceHandle surface);
// Whether waitForNextFrame waits for the previous present (Slag's frame
// latency capability), so its return marks that present as done. Otherwise it
// waits for the GPU only.
CRUCIBLE_EXPOSE bool pacingWaitsForPresent();
// While on, a thread waits on the surface's frame semaphore and stamps each
// submitted frame the moment the GPU finishes it.
CRUCIBLE_EXPOSE_STATUS Result<void> setGpuTiming(SurfaceHandle surface, bool enabled);
// When the GPU finished submitted frame `frame` (counting from 1, as
// framesSubmitted does), on the timestampNow clock; 0 if not known (not
// finished yet, timing off, or more than 256 frames ago).
CRUCIBLE_EXPOSE uint64_t gpuFrameDoneNs(SurfaceHandle surface, uint64_t frame);

// The present mode and minimum image count for a window supporting `modes`
// (ADR 0003): Mailbox with three images, else Fifo with two. A preference
// for a mode the window lacks falls back to the default choice.
struct PresentChoice
{
    slag::PresentMode mode;
    uint32_t minImageCount;
};
PresentChoice choosePresentMode(std::span<const slag::PresentMode> modes,
                                PresentPreference preference = PresentPreference::Default);

} // namespace crucible::render
