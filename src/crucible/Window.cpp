#include "Window.h"
#include <SDL_syswm.h>
#ifdef _WIN32
#include <windows.h>
#elif __linux
#endif
using namespace slag;
namespace crucible
{
    Window::Window(uint32_t width, uint32_t height, uint32_t backBuffers, bool vysncEnabled, const char* name)
    {
        SDL_WindowFlags flags = static_cast<SDL_WindowFlags>(SDL_WINDOW_VULKAN | SDL_WINDOW_RESIZABLE);
        _window = SDL_CreateWindow(name,SDL_WINDOWPOS_UNDEFINED,SDL_WINDOWPOS_UNDEFINED,500,500,flags);
        PlatformData pd;
        SDL_SysWMinfo wmInfo;
        SDL_VERSION(&wmInfo.version);
        SDL_GetWindowWMInfo(_window, &wmInfo);
#ifdef _WIN32
        pd.nativeWindowHandle = wmInfo.info.win.window;
#elif __linux
        pd.nativeWindowHandle = reinterpret_cast<void*>(wmInfo.info.x11.window);
        pd.nativeDisplayType = wmInfo.info.x11.display;
#endif
        _swapchain = SwapchainBuilder(pd).setWidth(width).setHeight(height).setDesiredBackBuffers(backBuffers).setVSyncEnabled(vysncEnabled).create();
    }

    Window::~Window()
    {
        if(_swapchain)
        {
            delete _swapchain;
        }
        if(_window)
        {
            SDL_DestroyWindow(_window);
        }
    }

    int Window::width()
    {
        int w, h;
        SDL_GetWindowSize(_window,&w,&h);
        return  w;
    }

    int Window::height()
    {
        int w, h;
        SDL_GetWindowSize(_window,&w,&h);
        return  w;
    }

    std::string Window::name()
    {
        return SDL_GetWindowTitle(_window);
    }

    void Window::open()
    {
        bool keepOpen = true;
        Uint64 now, last;
        now = SDL_GetPerformanceCounter();
        last = 0;
        while(keepOpen)
        {

            SDL_Event e;
            while(SDL_PollEvent(&e))
            {
                if( e.type == SDL_QUIT )
                {
                    delete _swapchain;
                    keepOpen = false;
                    return;
                }
                else if(e.type == SDL_WINDOWEVENT && e.window.event == SDL_WINDOWEVENT_RESIZED)
                {
                    int w,h;
                    SDL_GetWindowSize(_window,&w,&h);
                    _swapchain->resize(w,h);
                }
            }
            Frame* frame = nullptr;
            if(frame = _swapchain->next())
            {
                last = now;
                now = SDL_GetPerformanceCounter();
                frame->begin();
                auto deltaTime = ((now - last)*1000 / (double)SDL_GetPerformanceFrequency() );
                ImageMemoryBarrier imageBarrier{.oldLayout = slag::Texture::Layout::PRESENT, .newLayout = slag::Texture::Layout::RENDER_TARGET,.requireCachesFinish=slag::PipelineAccess::NONE,.usingCaches=slag::PipelineAccess::ACCESS_SHADER_WRITE_BIT, .texture=frame->getBackBuffer()};
                frame->getCommandBuffer()->insertBarriers(nullptr,0,&imageBarrier,1, nullptr,0,slag::PipelineStage::PipelineStageFlags::TOP,slag::PipelineStage::PipelineStageFlags::ALL_GRAPHICS);

                if(update)
                {
                    update(frame,deltaTime);
                }
                ImageMemoryBarrier imageBarrier2{.oldLayout = slag::Texture::Layout::RENDER_TARGET, .newLayout = slag::Texture::Layout::PRESENT,.requireCachesFinish=slag::PipelineAccess::ACCESS_SHADER_READ_BIT,.usingCaches=slag::PipelineAccess::NONE,.texture=frame->getBackBuffer()};
                frame->getCommandBuffer()->insertBarriers(nullptr,0,&imageBarrier2,1, nullptr,0,slag::PipelineStage::PipelineStageFlags::FRAGMENT_SHADER,slag::PipelineStage::PipelineStageFlags::BOTTOM);
                frame->end();
            }
        }
    }



} // crucible