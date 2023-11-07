#include "Engine.h"
#include <slag/SlagLib.h>
#include <SDL.h>
#include "Scripting/ScriptingEngine.h"


using namespace slag;
namespace crucible
{

    void Engine::initialize()
    {
        SDL_Init(SDL_INIT_EVERYTHING);
        SlagInitDetails details;
        details.backend = VULKAN;
        SlagLib::initialize(details);
        ScriptingEngine::initialize();
    }

    void Engine::cleanup()
    {
        ScriptingEngine::cleanup();
        SlagLib::cleanup();
    }
} // crucible