#ifndef CRUCIBLE_CRUCIBLE_H
#define CRUCIBLE_CRUCIBLE_H

#ifdef CRUCIBLE_MODULE_RENDERING
#include "rendering/API_Rendering.h"
#endif
#ifdef CRUCIBLE_MODULE_SCRIPTING
#include "scripting/API_Scripting.h"
#endif
namespace crucible
{
    enum class CrucibleInitializationResult
    {
        SUCCESS,
        UNABLE_TO_INIT_GRAPHICS_BACKEND,
        UNABLE_TO_LOAD_DOTNET_CORE,
        UNABLE_TO_FIND_SCRIPTING_ENTRY_POINT,
        ERROR_IN_SCRIPTING_INITIALIZATION,
        SCRIPTING_DLL_LOADING_FAILURE,
        UNABLE_TO_INIT_WINDOWING
    };

    enum class CrucibleModuleID
    {
        RENDERING = 1,
        SCRIPTING = 2
    };

    struct CrucibleInitParams
    {
#ifdef CRUCIBLE_MODULE_SCRIPTING
        std::filesystem::path scriptingDLLPath;
#endif

    };

    class Crucible
    {
    public:
        static CrucibleInitializationResult initialize(const CrucibleInitParams& params);
        static void cleanup();

    };
} // crucible

#endif //CRUCIBLE_CRUCIBLE_H