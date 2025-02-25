#include "Game.h"
#include <stb_image.h>
#include "scripting/ScriptingEngine.h"

namespace crucible
{

    class CrucibleFrameResources: public slag::FrameResources
    {
    public:
        slag::CommandBuffer* commandBuffer = nullptr;
        slag::DescriptorPool* descriptorPool = nullptr;
        CrucibleFrameResources()
        {
            commandBuffer = slag::CommandBuffer::newCommandBuffer(slag::GpuQueue::GRAPHICS);
            descriptorPool = slag::DescriptorPool::newDescriptorPool();
        }
        ~CrucibleFrameResources()override
        {
            delete commandBuffer;
            delete descriptorPool;
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

    slag::FrameResources* CRUCIBLE_GAME_RESOURCE_CREATE(size_t, slag::Swapchain*)
    {
        return new CrucibleFrameResources();
    }

    Game::Game(const std::string& name, const std::filesystem::path& icon, const std::filesystem::path& gameDll,  SDL_Window* inWindow)
    {
        if(inWindow == nullptr)
        {
            _windowManaged = true;
            SDL_WindowFlags flags = SDL_WINDOW_RESIZABLE | SDL_WINDOW_MAXIMIZED;
            if(slag::SlagLib::usingBackEnd() == slag::VULKAN)
            {
                flags |= SDL_WINDOW_VULKAN;
            }
            _window = SDL_CreateWindow(name.c_str(),500,500,flags);
        }
        else
        {
            _windowManaged = false;
            _window = inWindow;
        }

        SDL_SetWindowTitle(_window,name.c_str());
        int x,y,channels;
        auto iconData = stbi_load(icon.string().c_str(),&x,&y,&channels,4);
        _icon = SDL_CreateSurfaceFrom(x,y,SDL_PIXELFORMAT_RGBA8888,iconData,static_cast<int>(x*sizeof(char)*4));

        auto success = SDL_SetWindowIcon(_window,_icon);
        stbi_image_free(iconData);

        crucible::scripting::ScriptingEngine::loadManagedDll("Game",gameDll.string().c_str(),false);

        auto properties = SDL_GetWindowProperties(_window);
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
                platformData.data.x11.window = reinterpret_cast<void*>(SDL_GetNumberProperty(properties,SDL_PROP_WINDOW_X11_WINDOW_NUMBER, 0));
            }
#endif

        int w,h;
        SDL_GetWindowSize(_window,&w,&h);
        _swapChain = slag::Swapchain::newSwapchain(platformData, w, h, 3, slag::Swapchain::MAILBOX, slag::Pixels::B8G8R8A8_UNORM_SRGB, CRUCIBLE_GAME_RESOURCE_CREATE);

    }

    Game::~Game()
    {
        if(_windowManaged && _window)
        {
            SDL_DestroyWindow(_window);
        }
        if(_icon)
        {
            SDL_DestroySurface(_icon);
        }
        if(_swapChain)
        {
            delete _swapChain;
        }
    }

    Game::Game(Game&& from)
    {
        move(from);
    }

    Game& Game::operator=(Game&& from)
    {
        move(from);
        return *this;
    }

    void Game::move(Game& from)
    {
        std::swap(_window,from._window);
        std::swap(_windowManaged,from._windowManaged);
        std::swap(_icon,from._icon);
        std::swap(_swapChain,from._swapChain);
        std::swap(_keepOpen,from._keepOpen);
    }

    void Game::handleEvent(SDL_Event& event)
    {
        switch (event.type)
        {
            case SDL_EVENT_WINDOW_CLOSE_REQUESTED:
                close();
                break;
            case SDL_EVENT_WINDOW_RESIZED:
            case SDL_EVENT_WINDOW_RESTORED:
                resize();
                break;
            case SDL_EVENT_WINDOW_MINIMIZED:
                minimize();
                break;
        }
    }

    void Game::update(double deltaTime)
    {

    }

    void Game::draw(slag::CommandBuffer* commandBuffer, slag::Texture* drawBuffer, slag::DescriptorPool* descriptorPool)
    {
        commandBuffer->begin();
        commandBuffer->clearColorImage(drawBuffer,{.floats{1.0f,.15f,0.1,1.0f}},slag::Texture::UNDEFINED,slag::Texture::PRESENT,slag::PipelineStageFlags::NONE,slag::PipelineStageFlags::ALL_COMMANDS);
        commandBuffer->end();
    }

    void Game::close()
    {
       _keepOpen = false;
    }

    void Game::resize()
    {
        int w,h;
        SDL_GetWindowSize(_window,&w,&h);
        _swapChain->resize(w,h);
    }

    void Game::minimize()
    {
        _swapChain->resize(0,0);
    }

    void Game::run()
    {
        Uint64 previousCounter = 0;
        Uint64 currentCounter = 0;
        double deltaTime = 0.0;

        while(_keepOpen)
        {
            currentCounter = SDL_GetPerformanceCounter();
            deltaTime = (currentCounter - previousCounter) / (double)SDL_GetPerformanceFrequency();
            previousCounter = currentCounter;

            SDL_Event event;
            while(SDL_PollEvent(&event))
            {
                handleEvent(event);
            }
            update(deltaTime);
            if(auto frame = _swapChain->next())
            {
                auto resources = static_cast<CrucibleFrameResources*>(frame->resources);
                draw(resources->commandBuffer,frame->backBuffer(),resources->descriptorPool);
                slag::SlagLib::graphicsCard()->graphicsQueue()->submit(&resources->commandBuffer,1, nullptr,0, nullptr,0,frame);
            }
        }
    }
} // crucible