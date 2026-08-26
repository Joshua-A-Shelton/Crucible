#ifndef CRUCIBLE_API_SCRIPTING_H
#define CRUCIBLE_API_SCRIPTING_H
#include "core/DLLLoader.h"
#include "core/ManagedInstance.h"
#include "core/ManagedType.h"
#include <filesystem>

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


#endif //CRUCIBLE_API_SCRIPTING_H