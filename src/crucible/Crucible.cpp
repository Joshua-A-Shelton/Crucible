#include "Crucible.h"
#include <slag/SlagLib.h>
#include <SDL3/SDL.h>
#include <iostream>

#include "core/scenes/World.h"
#include "scripting/ScriptingEngine.h"

using namespace slag;
namespace crucible
{
    void RENDERING_DEBUG_HANDLER(std::string& message, SlagInitDetails::DebugLevel level, int32_t id)
    {
        std::cout << message << std::endl;
    }

    bool Crucible::initialize(RenderingBackend backend, bool debugEnabled)
    {
        auto sdlInit = SDL_Init(SDL_INIT_EVENTS | SDL_INIT_GAMEPAD | SDL_INIT_HAPTIC | SDL_INIT_JOYSTICK | SDL_INIT_VIDEO | SDL_INIT_AUDIO);
        if(!sdlInit)
        {
            std::cout << "Unable to initialize SDL3\n";
            SDL_Quit();
            return false;
        }
        SlagInitDetails details{.backend = BackEnd::VULKAN, .debug = debugEnabled,.slagDebugHandler=RENDERING_DEBUG_HANDLER};
        if(backend == RenderingBackend::DEFAULT)
        {
#ifdef _WIN32
            details.backend = BackEnd::DIRECTX12;
#else
            details.backend = BackEnd::VULKAN;
#endif
        }
        else if(backend == RenderingBackend::VULKAN)
        {
            details.backend = BackEnd::VULKAN;
        }
        else if(backend == RenderingBackend::DX12)
        {
#ifdef _WIN32
            details.backend = BackEnd::DIRECTX12;
#else
            details.backend = BackEnd::VULKAN;
#endif
        }
        auto slagInit = SlagLib::initialize(details);
        if(!slagInit && details.backend == BackEnd::DIRECTX12)
        {
            std::cout << "Unable to initialize Slag with DX12 backend. Falling back to Vulkan\n";
            details.backend = BackEnd::VULKAN;
            SlagLib::initialize(details);
        }
        if(!slagInit)
        {
            std::cout << "Unable to initialize Slag\n";
            SlagLib::cleanup();
            return false;
        }
        scripting::ScriptingEngine::initialize();
        crucible::core::World::RootNode = new core::Node(nullptr);
        return true;
    }

    void Crucible::cleanup()
    {
        if(crucible::core::World::RootNode != nullptr)
        {
            delete crucible::core::World::RootNode;
        }
        scripting::ScriptingEngine::cleanup();
        SlagLib::cleanup();
        SDL_Quit();
    }
} // crucible