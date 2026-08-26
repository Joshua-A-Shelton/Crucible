#include "Window.h"
#include <crucible/rendering/API_Rendering.h>
#include <unordered_set>
namespace crucible
{
    namespace rendering
    {
        std::unordered_set<Window*> CRUCIBLE_OPENED_WINDOWS;
        CRUCIBLE_WINDOW_PRESENT_MODE CRUCIBLE_CURRENT_PRESENT_MODE = CRUCIBLE_WINDOW_PRESENT_MODE::TRIPLE_BUFFER;

        Window::Window(const std::string& name, uint32_t width, uint32_t height, WindowDecorationMode decorationMode, WindowTransparency transparency)
        {
            SDL_WindowFlags windowFlags = SDL_WINDOW_RESIZABLE | SDL_WINDOW_HIDDEN;
            if (decorationMode == WindowDecorationMode::BORDERLESS)
            {
                windowFlags |= SDL_WINDOW_BORDERLESS;
            }
            if (transparency != WindowTransparency::OPAQUE)
            {
                windowFlags |= SDL_WINDOW_TRANSPARENT;
            }
            if (slag::Slag::backend()->api() == slag::BackendAPI::VULKAN)
            {
                windowFlags |= SDL_WINDOW_VULKAN;
            }
            _window = SDL_CreateWindow(name.c_str(), width, height, windowFlags);
            slag::PlatformData pd{};
            auto properties = SDL_GetWindowProperties(_window);
#ifdef _WIN32
            pd.platform = slag::Platform::WIN_32;
            pd.details.win32.hwnd = (HWND)SDL_GetPointerProperty(properties,SDL_PROP_WINDOW_WIN32_HWND_POINTER, nullptr);
            pd.details.win32.hinstance = (HINSTANCE)SDL_GetPointerProperty(properties,SDL_PROP_WINDOW_WIN32_INSTANCE_POINTER, nullptr);
#else
            const char* driverName = SDL_GetCurrentVideoDriver();
            if (strcmp(driverName, "wayland") == 0)
            {
                pd.platform = slag::Platform::WAYLAND;
                pd.details.wayland.surface = static_cast<wl_surface*>(SDL_GetPointerProperty(properties,SDL_PROP_WINDOW_WAYLAND_SURFACE_POINTER, nullptr));
                pd.details.wayland.display = static_cast<wl_display*>(SDL_GetPointerProperty(properties,SDL_PROP_WINDOW_WAYLAND_DISPLAY_POINTER, nullptr));
            }
            else
            {
                pd.platform = slag::Platform::X11;
                pd.details.x11.display = static_cast<Display*>(SDL_GetPointerProperty(properties,SDL_PROP_WINDOW_X11_DISPLAY_POINTER, nullptr));
                pd.details.x11.window = SDL_GetNumberProperty(properties,SDL_PROP_WINDOW_X11_WINDOW_NUMBER, 0);
            }
#endif

            slag::SwapChainParameters swapChainParameters{};
            swapChainParameters.imageFormat = slag::PixelFormat::R8G8B8A8_UNORM_SRGB;
            if (transparency == WindowTransparency::PRE_MULTIPLIED)
            {
                swapChainParameters.alphaCompositing = slag::AlphaCompositing::PREMULTIPLIED;
            }

            switch (CRUCIBLE_CURRENT_PRESENT_MODE)
            {
            case CRUCIBLE_WINDOW_PRESENT_MODE::TEAR:
                swapChainParameters.presentMode = slag::PresentMode::IMMEDIATE;
                swapChainParameters.imageCount = 2;
                break;
            case CRUCIBLE_WINDOW_PRESENT_MODE::DOUBLE_BUFFER:
                swapChainParameters.presentMode = slag::PresentMode::QUEUE;
                swapChainParameters.imageCount = 2;
                break;
            case CRUCIBLE_WINDOW_PRESENT_MODE::TRIPLE_BUFFER:
                swapChainParameters.presentMode = slag::PresentMode::BUFFER;
                swapChainParameters.imageCount = 3;
                break;
            }

            _swapChain = getGraphicsCard()->newSwapchain(pd,width,height,swapChainParameters);
            CRUCIBLE_OPENED_WINDOWS.emplace(this);

            SDL_SetPointerProperty(SDL_GetWindowProperties(_window),OWNING_CRUCIBLE_WINDOW_OBJECT_ID,this);
        }

        Window::~Window()
        {
            delete _swapChain;
            SDL_DestroyWindow(_window);
            CRUCIBLE_OPENED_WINDOWS.erase(this);
        }

        void Window::show() const
        {
            SDL_ShowWindow(_window);
        }

        void Window::updateWindowPresentMode(CRUCIBLE_WINDOW_PRESENT_MODE newPresentMode)
        {
            CRUCIBLE_CURRENT_PRESENT_MODE = newPresentMode;
            for (auto window : CRUCIBLE_OPENED_WINDOWS)
            {
                auto swapChainParameters = window->_swapChain->parameters();
                switch (CRUCIBLE_CURRENT_PRESENT_MODE)
                {
                case CRUCIBLE_WINDOW_PRESENT_MODE::TEAR:
                    swapChainParameters.presentMode = slag::PresentMode::IMMEDIATE;
                    swapChainParameters.imageCount = 2;
                    break;
                case CRUCIBLE_WINDOW_PRESENT_MODE::DOUBLE_BUFFER:
                    swapChainParameters.presentMode = slag::PresentMode::QUEUE;
                    swapChainParameters.imageCount = 2;
                    break;
                case CRUCIBLE_WINDOW_PRESENT_MODE::TRIPLE_BUFFER:
                    swapChainParameters.presentMode = slag::PresentMode::BUFFER;
                    swapChainParameters.imageCount = 3;
                    break;
                }
                window->_swapChain->setParameters(swapChainParameters);
            }
        }

        CRUCIBLE_WINDOW_PRESENT_MODE Window::getWindowPresentMode()
        {
            return CRUCIBLE_CURRENT_PRESENT_MODE;
        }
    } //rendering
} // crucible