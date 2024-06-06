#include <SDL_syswm.h>
#include "Game.h"
#include "../third-party/stb/stb_image.h"

namespace crucible
{
    void Game::run(const char* gameName, const char* iconPath)
    {
        buildSwapchain(gameName);
        initialize(gameName,iconPath);
        gameLoop();
        cleanup();
        destroySwapchain();

    }

    void Game::gameLoop()
    {
        Uint64 now = SDL_GetPerformanceCounter();
        Uint64 last = 0;
        float deltaTime = 0;
        while(running)
        {
            processEvents();
            if(running)
            {
                update(deltaTime);
                if(slag::Frame* currentFrame = _swapchain->next())
                {
                    render(currentFrame);
                }
            }
            last = now;
            now = SDL_GetPerformanceCounter();
            deltaTime = ((now - last)*1000 / (float)SDL_GetPerformanceFrequency());
        }
    }

    void Game::initialize(const char* gameName,const char* iconPath)
    {
        int x,y,channels;
        auto pixels = stbi_load(iconPath,&x,&y,&channels,4);
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
        auto icon = SDL_CreateRGBSurfaceFrom(pixels, x, y, channels*8, pitch, Rmask, Gmask,Bmask, Amask);

        SDL_SetWindowIcon(_window,icon);

        SDL_FreeSurface(icon);
        stbi_image_free(pixels);

    }

    void Game::setUpSwapchain(slag::SwapchainBuilder& builder)
    {
        builder.addTextureResource("Color",{slag::TextureResourceDescription::SizingMode::Absolute, 1920, 1080, slag::Pixels::R8G8B8A8_UNORM, slag::Texture::Usage::COLOR, true});
        builder.addTextureResource("Depth",{slag::TextureResourceDescription::SizingMode::Absolute, 1920, 1080, slag::Pixels::D32_SFLOAT, slag::Texture::Usage::DEPTH, true});
    }

    void Game::processEvents()
    {
        SDL_Event e;

        // Events management
        while(SDL_PollEvent(&e))
        {
            if( e.type == SDL_QUIT )
            {
                running = false;
            }
            else if(e.type == SDL_WINDOWEVENT && e.window.event == SDL_WINDOWEVENT_CLOSE)
            {
                running = false;
            }
            else if(e.type == SDL_WINDOWEVENT && (e.window.event == SDL_WINDOWEVENT_RESIZED || e.window.event == SDL_WINDOWEVENT_MAXIMIZED || e.window.event == SDL_WINDOWEVENT_MINIMIZED))
            {
                handleResize();
            }

        }
    }

    void Game::update(float deltaTime)
    {

    }

    void Game::render(slag::Frame* frame)
    {
        frame->begin();
        frame->end();
    }

    void Game::cleanup()
    {
    }

    void Game::buildSwapchain(const char* gameName)
    {
        _window = SDL_CreateWindow(gameName,SDL_WINDOWPOS_UNDEFINED,SDL_WINDOWPOS_UNDEFINED,800,500,SDL_WindowFlags::SDL_WINDOW_VULKAN | SDL_WindowFlags::SDL_WINDOW_RESIZABLE | SDL_WINDOW_MAXIMIZED);
        if(_window == nullptr)
        {
            auto message = SDL_GetError();
            throw std::runtime_error(message);
        }
        slag::PlatformData pd;
        SDL_SysWMinfo wmInfo;
        SDL_VERSION(&wmInfo.version);
        SDL_GetWindowWMInfo(_window, &wmInfo);
#ifdef _WIN32
        pd.nativeWindowHandle = wmInfo.info.win.window;
        pd.nativeDisplayType = wmInfo.info.win.hinstance;

#elif __linux
        pd.nativeWindowHandle = reinterpret_cast<void*>(wmInfo.info.x11.window);
    pd.nativeDisplayType = wmInfo.info.x11.display;
#endif
        auto builder = slag::SwapchainBuilder(pd);
        setUpSwapchain(builder);
        _swapchain = builder.setDesiredBackBuffers(2).setHeight(500).setWidth(800).create();
    }

    void Game::destroySwapchain()
    {
        delete _swapchain;
        SDL_DestroyWindow(_window);
    }

    void Game::handleResize()
    {
        int w,h;
        SDL_GetWindowSize(_window,&w,&h);
        _swapchain->resize(w,h);
    }

    slag::Swapchain *Game::swapchain()
    {
        return _swapchain;
    }

    SDL_Window *Game::window()
    {
        return _window;
    }


} // crucible