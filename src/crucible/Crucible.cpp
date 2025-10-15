#include "Crucible.h"

#include <iostream>
#include <slag/Slag.h>
#include <SDL3/SDL.h>
#include <crucible/scripting/ScriptingEngine.h>
namespace crucible
{
#ifndef NDEBUG
    void SLAG_DEBUG_HANDLER(const std::string& message, slag::SlagDebugLevel debugLevel, int32_t messageID)
    {
        std::cout << message << std::endl;
    }
#endif

    bool initialize()
    {
        if (!SDL_Init(SDL_INIT_EVENTS | SDL_INIT_GAMEPAD | SDL_INIT_HAPTIC | SDL_INIT_JOYSTICK | SDL_INIT_VIDEO | SDL_INIT_AUDIO))
        {
            std::cout << "Unable to initialize SDL3\n";
            return false;
        }
        slag::SlagInitInfo initInfo;
        initInfo.graphicsBackend = slag::GraphicsBackend::VULKAN_GRAPHICS_BACKEND;
#ifndef NDEBUG
        initInfo.slagDebugHandler = SLAG_DEBUG_HANDLER;
#endif

        auto initializationResult = slag::initialize(initInfo);
        if (initializationResult != slag::SLAG_INITIALIZATION_SUCCESS)
        {
            std::cout << "Unable to initialize Slag Graphics Library\n";
            return false;
        }
        if (!scripting::ScriptingEngine::initialize())
        {
            std::cout << "Unable to initialize C# Scripting Engine\n";
        }
        return true;
    }

    void cleanup()
    {
        scripting::ScriptingEngine::cleanup();
        slag::cleanup();
        SDL_Quit();
    }
} // crucible
