#include "CrucibleLib.h"
#include <SDL2/SDL.h>
#include <SDL2/SDL_vulkan.h>
#include <slag/SlagLib.h>

namespace crucible
{
    bool CrucibleLib::init()
    {
        SDL_Init(SDL_INIT_EVERYTHING);
        return slag::SlagLib::init(true);
    }

    void CrucibleLib::cleanup()
    {
        slag::SlagLib::cleanup();
    }
} // crucible