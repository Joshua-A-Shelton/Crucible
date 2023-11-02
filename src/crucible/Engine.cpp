#include "Engine.h"
#include <slag/SlagLib.h>
#include <SDL.h>



using namespace slag;
namespace crucible
{

    void Engine::initialize()
    {
        SDL_Init(SDL_INIT_EVERYTHING);
        SlagInitDetails details;
        details.backend = VULKAN;
        SlagLib::initialize(details);

    }

    void Engine::cleanup()
    {
        SlagLib::cleanup();
    }
} // crucible