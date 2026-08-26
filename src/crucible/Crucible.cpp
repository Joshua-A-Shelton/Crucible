#include "Crucible.h"
#include <SDL3/SDL.h>
#ifdef CRUCIBLE_MODULE_RENDERING
#include "rendering/API_Rendering.h"
#endif
#ifdef CRUCIBLE_MODULE_SCRIPTING
#include "scripting/API_Scripting.h"
#endif


namespace crucible
{
    CrucibleInitializationResult Crucible::initialize(const CrucibleInitParams& params)
    {
#ifdef CRUCIBLE_INCLUDE_SDL
        SDL_InitFlags sdlInitFlags = SDL_INIT_EVENTS;
#endif

#ifdef CRUCIBLE_RENDERING_MODULE
        auto renderingResult = rendering::initializeRenderingSubmodule();
        if (renderingResult != CrucibleInitializationResult::SUCCESS)
        {
            return renderingResult;
        }
        sdlInitFlags = SDL_INIT_VIDEO;
#endif

#ifdef CRUCIBLE_SCRIPTING_MODULE
        auto scriptingResult = scripting::initializeScriptingSubmodule(params.scriptingDLLPath);
        if (scriptingResult != CrucibleInitializationResult::SUCCESS)
        {
            return scriptingResult;
        }
#endif


#ifdef CRUCIBLE_INCLUDE_SDL
        if (!SDL_Init(sdlInitFlags))
        {
            return CrucibleInitializationResult::UNABLE_TO_INIT_WINDOWING;
        }
#endif

        return CrucibleInitializationResult::SUCCESS;
    }

    void Crucible::cleanup()
    {
#ifdef CRUCIBLE_SCRIPTING_MODULE
        scripting::cleanupScriptingSubmodule();
#endif

#ifdef CRUCIBLE_RENDERING_MODULE
        rendering::cleanupRenderingSubmodule();
#endif
#ifdef CRUCIBLE_INCLUDE_SDL
        SDL_Quit();
#endif
    }

} // crucible