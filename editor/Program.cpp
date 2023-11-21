#define SDL_MAIN_HANDLED
#include <crucible/Engine.h>
#include <slag/SlagLib.h>
#include <SDL.h>
#include <SDL_syswm.h>
#include "Graphics/DearImGUI/imgui_impl_slag.h"
#include <backends/imgui_impl_sdl2.h>

int main(int argc, char** args)
{
    crucible::Engine::initialize();
    auto mainWindow = SDL_CreateWindow("Crucible",SDL_WINDOWPOS_UNDEFINED,SDL_WINDOWPOS_UNDEFINED,800,500,SDL_WindowFlags::SDL_WINDOW_VULKAN | SDL_WindowFlags::SDL_WINDOW_RESIZABLE);
    bool open = true;
    slag::PlatformData pd;
    SDL_SysWMinfo wmInfo;
    SDL_VERSION(&wmInfo.version);
    SDL_GetWindowWMInfo(mainWindow, &wmInfo);
#ifdef _WIN32
    pd.nativeWindowHandle = wmInfo.info.win.window;
#elif __linux
    pd.nativeWindowHandle = reinterpret_cast<void*>(wmInfo.info.x11.window);
    pd.nativeDisplayType = wmInfo.info.x11.display;
#endif
    slag::Swapchain* swapchain = slag::SwapchainBuilder(pd)
            .setDesiredBackBuffers(2)
            .addVertexBufferResource("ImGuiVerts",{15000,slag::Buffer::Usage::CPU})
            .addIndexBufferResource("ImGuiIndexes",{15000,slag::Buffer::Usage::CPU})
            .setHeight(500).setWidth(800).create();

    // Setup Dear ImGui context
    IMGUI_CHECKVERSION();
    ImGui::CreateContext();
    ImGuiIO& io = ImGui::GetIO();
    io.ConfigFlags |= ImGuiConfigFlags_NavEnableKeyboard;     // Enable Keyboard Controls
    io.ConfigFlags |= ImGuiConfigFlags_NavEnableGamepad;      // Enable Gamepad Controls
    io.ConfigFlags |= ImGuiConfigFlags_DockingEnable;         // IF using Docking Branch
    io.ConfigFlags |= ImGuiConfigFlags_ViewportsEnable;

    ImGui_ImplSDL2_InitForOther(mainWindow);
    ImGui_ImplSlag_Init(swapchain->imageFormat());

    while(open)
    {
        SDL_Event e;

        // Events management
        while(SDL_PollEvent(&e))
        {
            if( e.type == SDL_QUIT )
            {
                open = false;
            }
            else if(e.type == SDL_WINDOWEVENT && e.window.event == SDL_WINDOWEVENT_CLOSE)
            {
                open = false;
            }
            else if(e.type == SDL_WINDOWEVENT && e.window.event == SDL_WINDOWEVENT_RESIZED)
            {
                int w,h;
                SDL_GetWindowSize(mainWindow,&w,&h);
                swapchain->resize(w,h);
            }
            ImGui_ImplSDL2_ProcessEvent(&e); // Forward your event to backend

        }
        if(auto frame = swapchain->next())
        {
            frame->begin();
            ImGui_ImplSlag_NewFrame();
            ImGui_ImplSDL2_NewFrame();
            ImGui::NewFrame();

            auto* commandBuffer = frame->getCommandBuffer();
            slag::Rectangle rect{{0,0},{swapchain->width(),swapchain->height()}};
            commandBuffer->setViewport(rect);
            commandBuffer->setScissors(rect);

            slag::Rectangle view{{0,0},{swapchain->width(),swapchain->height()}};
            slag::Attachment colorAttachment{.texture = frame->getBackBuffer(), .clearOnLoad = true, .clear={0.5,0.5,0.5,0.5}};
            commandBuffer->setTargetFramebuffer(view,&colorAttachment,1);

            ImGui::Begin("hello");
            ImGui::End();
            ImGui::Render();
            ImGui_ImplSlag_RenderDrawData(ImGui::GetDrawData(),frame, nullptr);
            commandBuffer->endTargetFramebuffer();
            frame->end();
        }
        ImGui::UpdatePlatformWindows();
        ImGui::RenderPlatformWindowsDefault();
    }

    ImGui_ImplSlag_Shutdown();
    ImGui_ImplSDL2_Shutdown();
    ImGui::DestroyContext();
    delete swapchain;
    SDL_DestroyWindow(mainWindow);

    crucible::Engine::cleanup();
}