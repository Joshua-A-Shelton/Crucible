#ifndef CRUCIBLE_IMGUI_IMPL_SLAG_H
#define CRUCIBLE_IMGUI_IMPL_SLAG_H
#include <slag/SlagLib.h>
#include <imgui.h>
namespace crucible
{
    IMGUI_IMPL_API bool ImGui_ImplSlag_Init();
    IMGUI_IMPL_API void ImGui_ImplSlag_NewFrame();
    IMGUI_IMPL_API void ImGui_ImplSlag_RenderDrawData(ImDrawData* draw_data, slag::CommandBuffer* commandBuffer, slag::Shader* shader);
    IMGUI_IMPL_API void ImGui_ImplSlag_Shutdown();

    IMGUI_IMPL_API bool     ImGui_ImplSlag_CreateFontsTexture();
    IMGUI_IMPL_API void     ImGui_ImplSlag_DestroyFontsTexture();
    IMGUI_IMPL_API bool     ImGui_ImplSlag_CreateDeviceObjects();
    IMGUI_IMPL_API void     ImGui_ImplSlag_DestroyDeviceObjects();
} // crucible
#endif //CRUCIBLE_IMGUI_IMPL_SLAG_H