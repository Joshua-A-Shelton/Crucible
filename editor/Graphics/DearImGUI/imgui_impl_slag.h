#ifndef CRUCIBLE_IMGUI_IMPL_SLAG_H
#define CRUCIBLE_IMGUI_IMPL_SLAG_H
#include <imgui.h>
#include <slag/SlagLib.h>
#include <SDL.h>


// Backend API
IMGUI_IMPL_API bool     ImGui_ImplSlag_Init(slag::Pixels::PixelFormat renderTargetFormat);
IMGUI_IMPL_API void     ImGui_ImplSlag_Shutdown();
IMGUI_IMPL_API void     ImGui_ImplSlag_NewFrame();
IMGUI_IMPL_API void     ImGui_ImplSlag_RenderDrawData(ImDrawData *draw_data, slag::CommandBuffer *commandBuffer, slag::Shader* shader,slag::UniformSetDataAllocator* allocator);


struct ImGui_SlagBackEndData
{
    slag::Shader* shader = nullptr;
    slag::TextureSampler* sampler = nullptr;
    slag::Pixels::PixelFormat renderTargetFormat;
    slag::Texture* fontAtlas = nullptr;
};
struct ImGui_SlagFrameRenderBuffers
{
    slag::VertexBuffer* vertexBuffer = nullptr;
    slag::IndexBuffer* indexBuffer = nullptr;
};
struct ImGui_SlagWindowRenderBuffers
{
    uint32_t            Index = 0;
    uint32_t            Count = 0;
    ImGui_SlagFrameRenderBuffers* FrameRenderBuffers = nullptr;
};
struct ImGui_SlagViewPort
{
    bool WindowOwned = false;
    slag::Swapchain* window = nullptr;
    SDL_Window* handle = nullptr;
    ImGui_SlagWindowRenderBuffers RenderBuffers;

};

ImGui_SlagBackEndData* imGui_getSlagBackendData();
static void ImGui_Slag_RenderWindow(ImGuiViewport* viewport, void*);
static void ImGui_Slag_SetupRenderState(ImDrawData *draw_data, slag::Shader *pipeline, slag::CommandBuffer *command_buffer, slag::VertexBuffer* vertexBuffer, slag:: IndexBuffer* indexBuffer, int fb_width, int fb_height);
static void ImGui_Slag_InitPlatformInterface();

static void ImGui_Slag_CreateWindow(ImGuiViewport* viewport);
static void ImGui_Slag_DestroyWindow(ImGuiViewport* viewport);
static void ImGui_Slag_SetWindowSize(ImGuiViewport* viewport, ImVec2 size);
static void ImGui_Slag_SetWindowPos(ImGuiViewport* viewport, ImVec2 pos);


#endif //CRUCIBLE_IMGUI_IMPL_SLAG_H
