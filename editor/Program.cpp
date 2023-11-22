#define SDL_MAIN_HANDLED
#include <crucible/Engine.h>
#include <slag/SlagLib.h>
#include <SDL.h>
#include "../third-party/slag/third-party/stb/stb_image.h"
#include <SDL_syswm.h>
#include "Graphics/DearImGUI/imgui_impl_slag.h"
#include <backends/imgui_impl_sdl2.h>
#include "Graphics/Fonts.h"

int main(int argc, char** args)
{
    crucible::Engine::initialize();
    auto mainWindow = SDL_CreateWindow("Crucible",SDL_WINDOWPOS_UNDEFINED,SDL_WINDOWPOS_UNDEFINED,800,500,SDL_WindowFlags::SDL_WINDOW_VULKAN | SDL_WindowFlags::SDL_WINDOW_RESIZABLE);
    int x,y,channels;
    unsigned char* pixels = stbi_load("Crucible.png",&x,&y,&channels,4);
    // Calculate pitch
    int pitch;
    pitch = x * channels;
    pitch = (pitch + 3) & ~3;

    // Setup relevance bitmask
    int32_t Rmask, Gmask, Bmask, Amask;
#if SDL_BYTEORDER == SDL_LIL_ENDIAN
    Rmask = 0x000000FF;
    Gmask = 0x0000FF00;
    Bmask = 0x00FF0000;
    Amask = (channels == 4) ? 0xFF000000 : 0;
#else
    int s = (channels == 4) ? 0 : 8;
    Rmask = 0xFF000000 >> s;
    Gmask = 0x00FF0000 >> s;
    Bmask = 0x0000FF00 >> s;
    Amask = 0x000000FF >> s;
#endif
    SDL_Surface* crucibleImg = SDL_CreateRGBSurfaceFrom(pixels, x, y, channels*8, pitch, Rmask, Gmask,
                                                    Bmask, Amask);


    SDL_SetWindowIcon(mainWindow,crucibleImg);
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
            .addVertexBufferResource("ImGuiVerts",{15000,slag::Buffer::Usage::GPU})
            .addIndexBufferResource("ImGuiIndexes",{15000,slag::Buffer::Usage::GPU})
            .setHeight(500).setWidth(800).create();

    // Setup Dear ImGui context
    IMGUI_CHECKVERSION();
    ImGui::CreateContext();
    ImGuiIO& io = ImGui::GetIO();
    io.ConfigFlags |= ImGuiConfigFlags_NavEnableKeyboard;     // Enable Keyboard Controls
    io.ConfigFlags |= ImGuiConfigFlags_NavEnableGamepad;      // Enable Gamepad Controls
    io.ConfigFlags |= ImGuiConfigFlags_DockingEnable;         // IF using Docking Branch
    io.ConfigFlags |= ImGuiConfigFlags_ViewportsEnable;

    ImFontConfig font_cfg;
    font_cfg.FontDataOwnedByAtlas = false;
    ImFont* recharge = io.Fonts->AddFontFromMemoryTTF(recharge_bd_ttf,recharge_bd_ttf_len,14,&font_cfg);

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
            ImGui::PushFont(recharge);

            auto* commandBuffer = frame->getCommandBuffer();
            slag::Rectangle rect{{0,0},{swapchain->width(),swapchain->height()}};
            commandBuffer->setViewport(rect);
            commandBuffer->setScissors(rect);

            slag::Rectangle view{{0,0},{swapchain->width(),swapchain->height()}};
            slag::Attachment colorAttachment{.texture = frame->getBackBuffer(), .clearOnLoad = true, .clear={0.5,0.5,0.5,0.5}};
            commandBuffer->setTargetFramebuffer(view,&colorAttachment,1);

            ImGui::DockSpaceOverViewport(ImGui::GetMainViewport());
            ImGui::BeginMainMenuBar();
            ImGui::EndMainMenuBar();

            ImGui::Begin("Main Viewport");
            ImGui::End();

            ImGui::Begin("Scene Tree");
            ImGui::End();

            ImGui::Begin("Inspector");
            ImGui::End();

            ImGui::Begin("Resources");
            ImGui::End();

            ImGui::PopFont();
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
    SDL_FreeSurface(crucibleImg);
    stbi_image_free(pixels);

    crucible::Engine::cleanup();
}