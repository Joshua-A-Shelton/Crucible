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
        auto success = SlagLib::initialize(details);
        int i=0;
    }

    void Engine::cleanup()
    {
        SlagLib::cleanup();
    }
} // crucible