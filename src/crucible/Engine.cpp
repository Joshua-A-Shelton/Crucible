#include "Engine.h"
#include <slag/SlagLib.h>
#include <SDL.h>
#include <mono/jit/jit.h>
#include <mono/metadata/assembly.h>

using namespace slag;
namespace crucible
{
    void Engine::initialize()
    {
        SDL_Init(SDL_INIT_EVERYTHING);
        SlagInitDetails details;
        details.backend = VULKAN;
        SlagLib::initialize(details);
        mono_set_assemblies_path("mono/lib");
    }

    void Engine::cleanup()
    {
        SlagLib::cleanup();
    }
} // crucible