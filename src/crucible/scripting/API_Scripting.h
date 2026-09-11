#ifndef CRUCIBLE_API_SCRIPTING_H
#define CRUCIBLE_API_SCRIPTING_H

#ifdef CRUCIBLE_SCRIPTING_MODULE
#include <filesystem>
#include "core/Callbacks.h"

namespace crucible
{
    enum class CrucibleInitializationResult;
}

namespace crucible
{
    namespace scripting
    {
        CrucibleInitializationResult initializeScriptingSubmodule(const std::filesystem::path& scriptingDLL);
        void cleanupScriptingSubmodule();

        //no api functions here, the API functions exists *for* the scripting module
    }
}
#endif //CRUCIBLE_SCRIPTING_MODULE

#endif //CRUCIBLE_API_SCRIPTING_H