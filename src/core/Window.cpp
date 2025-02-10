#include "Window.h"

namespace crucible
{
    namespace core
    {
        class CrucibleFrameResources: public slag::FrameResources
        {
        public:
            slag::CommandBuffer* commandBuffer = nullptr;
            CrucibleFrameResources()
            {
                commandBuffer = slag::CommandBuffer::newCommandBuffer(slag::GpuQueue::GRAPHICS);
            }
            ~CrucibleFrameResources()override
            {
                delete(commandBuffer);
            }
            void waitForResourcesToFinish()override
            {
                commandBuffer->waitUntilFinished();
            }
            bool isFinished()override
            {
                return commandBuffer->isFinished();
            }
        };

        slag::FrameResources* CRUCIBLE_WINDOW_RESOURCE_CREATE(size_t frameIndex, slag::Swapchain* swapchain)
        {
            return new CrucibleFrameResources();
        }


        Window::Window(const std::string& title, uint32_t width, uint32_t height, slag::Swapchain::PresentMode presentMode)
        {
            SDL_WindowFlags flags = SDL_WINDOW_RESIZABLE;
            if(slag::SlagLib::usingBackEnd() == slag::VULKAN)
            {
                flags |= SDL_WINDOW_VULKAN;
            }

            _sdlHandle = SDL_CreateWindow(title.c_str(),static_cast<int>(width),static_cast<int>(height),flags);
            auto properties = SDL_GetWindowProperties(_sdlHandle);
            slag::PlatformData platformData{};


#ifdef _WIN32
            platformData.platform = slag::PlatformData::WIN_32;
            platformData.data.win32.hwnd = SDL_GetPointerProperty(properties,SDL_PROP_WINDOW_WIN32_HWND_POINTER, nullptr);
            platformData.data.win32.hinstance = SDL_GetPointerProperty(properties,SDL_PROP_WINDOW_WIN32_INSTANCE_POINTER, nullptr);
#else
            const char* driverName = SDL_GetCurrentVideoDriver();
            if (strcmp(driverName, "wayland") == 0)
            {
                platformData.platform = slag::PlatformData::WAYLAND;
                platformData.data.wayland.surface = SDL_GetPointerProperty(properties,SDL_PROP_WINDOW_WAYLAND_SURFACE_POINTER, nullptr);
                platformData.data.wayland.display = SDL_GetPointerProperty(properties,SDL_PROP_WINDOW_WAYLAND_DISPLAY_POINTER, nullptr);
            }
            else
            {
                platformData.platform = slag::PlatformData::X11;
                platformData.data.x11.display = SDL_GetPointerProperty(properties,SDL_PROP_WINDOW_X11_DISPLAY_POINTER, nullptr);
                platformData.data.x11.window = SDL_GetPointerProperty(properties,SDL_PROP_WINDOW_X11_WINDOW_NUMBER, nullptr);
            }
#endif

            _swapchain = slag::Swapchain::newSwapchain(platformData,width,height,3,presentMode,slag::Pixels::B8G8R8A8_UNORM_SRGB,CRUCIBLE_WINDOW_RESOURCE_CREATE);
        }

        Window::~Window()
        {
            if(_sdlHandle)
            {
                SDL_DestroyWindow(_sdlHandle);
            }
            if(_swapchain)
            {
                delete _swapchain;
            }
        }

        Window::Window(Window&& from)
        {
            move(from);
        }

        Window& Window::operator=(Window&& from)
        {
            move(from);
            return *this;
        }

        void Window::move(Window& from)
        {
            std::swap(_sdlHandle,from._sdlHandle);
            std::swap(_swapchain,from._swapchain);
        }

        void Window::run()
        {
            bool stayOpen = true;
            while(stayOpen)
            {
                SDL_Event event;
                while(SDL_PollEvent(&event))
                {

                }
            }
        }
    } // core
} // crucible