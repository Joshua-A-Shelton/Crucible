#include "Window.h"
#include <crucible/rendering/API_Rendering.h>
#include <crucible/update/API_Update.h>
#include <unordered_set>

#include "ResourceManager.h"

namespace crucible
{
    namespace rendering
    {
        std::unordered_set<Window*> CRUCIBLE_OPENED_WINDOWS;
        CRUCIBLE_WINDOW_PRESENT_MODE CRUCIBLE_CURRENT_PRESENT_MODE = CRUCIBLE_WINDOW_PRESENT_MODE::TRIPLE_BUFFER;
        Window* ROOT_WINDOW = nullptr;

        Window::Window(const std::string& name, uint32_t width, uint32_t height, Window* parentWindow, WindowDecorationMode decorationMode, WindowTransparency transparency)
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
            if (parentWindow)
            {
                SDL_SetWindowParent(_window, parentWindow->_window);
            }
            else if (ROOT_WINDOW)
            {
                SDL_SetWindowParent(_window, ROOT_WINDOW->_window);
            }
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

            auto graphicsCard = getGraphicsCard();
            _swapChain = graphicsCard->newSwapchain(pd,width,height,swapChainParameters);
            _renderBuffer = graphicsCard->newTexture2D(swapChainParameters.imageFormat, slag::TextureUsageFlags::COLOR_TARGET, width, height, 1);
            _depthBuffer = graphicsCard->newTexture2D(slag::PixelFormat::D32_FLOAT_S8X24_UINT, slag::TextureUsageFlags::DEPTH_STENCIL_TARGET, width, height, 1);


            CRUCIBLE_OPENED_WINDOWS.emplace(this);

            SDL_SetPointerProperty(SDL_GetWindowProperties(_window),OWNING_CRUCIBLE_WINDOW_OBJECT_ID,this);

            if (ROOT_WINDOW == nullptr)
            {
                ROOT_WINDOW = this;
            }
        }

        Window::~Window()
        {
            delete _swapChain;
            delete _renderBuffer;
            delete _depthBuffer;

            if (_window)
            {
                SDL_DestroyWindow(_window);
                CRUCIBLE_OPENED_WINDOWS.erase(this);
                if (this == ROOT_WINDOW)
                {
                    ROOT_WINDOW = nullptr;
                    CRUCIBLE_API_finish();
                }
            }


        }

        void Window::show() const
        {
            SDL_ShowWindow(_window);
        }

        void Window::attemptClose()
        {
            SDL_DestroyWindow(_window);
            CRUCIBLE_OPENED_WINDOWS.erase(this);
            _window = nullptr;
            if (this == ROOT_WINDOW)
            {
                ROOT_WINDOW = nullptr;
                CRUCIBLE_API_finish();
            }
        }

        void Window::rebuildRenderTargets(uint32_t width, uint32_t height)
        {
            ResourceManager::queueDelete(_renderBuffer);
            ResourceManager::queueDelete(_depthBuffer);
            _renderBuffer = getGraphicsCard()->newTexture2D(slag::PixelFormat::R8G8B8A8_UNORM_SRGB,slag::TextureUsageFlags::COLOR_TARGET,width,height);
            _depthBuffer = getGraphicsCard()->newTexture2D(slag::PixelFormat::D32_FLOAT_S8X24_UINT, slag::TextureUsageFlags::DEPTH_STENCIL_TARGET, width, height);
        }

        Window* Window::parent() const
        {
            SDL_Window* parent = SDL_GetWindowParent(_window);
            if (parent == nullptr)
            {
                return nullptr;
            }
            Window* crucibleWindow = (Window*)SDL_GetPointerProperty(SDL_GetWindowProperties(parent),OWNING_CRUCIBLE_WINDOW_OBJECT_ID,nullptr);
            return crucibleWindow;
        }

        slag::SwapChain* Window::swapChain() const
        {
            return _swapChain;
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

        std::vector<Window*> Window::getOpenedWindows()
        {
            std::vector<Window*> windows(CRUCIBLE_OPENED_WINDOWS.begin(), CRUCIBLE_OPENED_WINDOWS.end());
            return windows;
        }
    } //rendering
} // crucible