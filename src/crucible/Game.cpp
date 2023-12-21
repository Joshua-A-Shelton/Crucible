#include <SDL_syswm.h>
#include "Game.h"
#include "../third-party/stb/stb_image.h"

namespace crucible
{
    void Game::run(const char* gameName, const char* iconPath)
    {
        initialize(gameName,iconPath);

        Uint64 now = SDL_GetPerformanceCounter();
        Uint64 last = 0;
        float deltaTime = 0;
        while(running)
        {
            processEvents();
            if(running)
            {
                update(deltaTime);
                if(slag::Frame* currentFrame = swapchain->next())
                {
                    render(currentFrame);
                }
            }
            last = now;
            now = SDL_GetPerformanceCounter();
            deltaTime = ((now - last)*1000 / (float)SDL_GetPerformanceFrequency());
        }
        cleanup();
    }

    void Game::initialize(const char* gameName,const char* iconPath)
    {
        window = SDL_CreateWindow(gameName,SDL_WINDOWPOS_UNDEFINED,SDL_WINDOWPOS_UNDEFINED,800,500,SDL_WindowFlags::SDL_WINDOW_VULKAN | SDL_WindowFlags::SDL_WINDOW_RESIZABLE | SDL_WINDOW_MAXIMIZED);

        int x,y,channels;
        unsigned char* pixels = stbi_load(iconPath,&x,&y,&channels,4);
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
        SDL_Surface* icon = SDL_CreateRGBSurfaceFrom(pixels, x, y, channels*8, pitch, Rmask, Gmask,Bmask, Amask);


        SDL_SetWindowIcon(window,icon);
        SDL_FreeSurface(icon);
        stbi_image_free(pixels);

        slag::PlatformData pd;
        SDL_SysWMinfo wmInfo;
        SDL_VERSION(&wmInfo.version);
        SDL_GetWindowWMInfo(window, &wmInfo);
#ifdef _WIN32
        pd.nativeWindowHandle = wmInfo.info.win.window;
        pd.nativeDisplayType = wmInfo.info.win.hinstance;

#elif __linux
        pd.nativeWindowHandle = reinterpret_cast<void*>(wmInfo.info.x11.window);
    pd.nativeDisplayType = wmInfo.info.x11.display;
#endif
        swapchain = slag::SwapchainBuilder(pd)
                .addTextureResource("Color",{slag::TextureResourceDescription::SizingMode::Absolute,1920,1080,slag::Pixels::R8G8B8A8_UNORM,slag::Texture::Usage::COLOR,true})
                .addTextureResource("Depth",{slag::TextureResourceDescription::SizingMode::Absolute,1920,1080,slag::Pixels::D32_SFLOAT,slag::Texture::Usage::DEPTH,true})
                .create();
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
            else if(e.type == SDL_WINDOWEVENT && e.window.event == SDL_WINDOWEVENT_RESIZED)
            {
                int w,h;
                SDL_GetWindowSize(window,&w,&h);
                swapchain->resize(w,h);
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
        delete swapchain;
        SDL_DestroyWindow(window);
    }
} // crucible