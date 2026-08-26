#include "DLLLoader.h"

namespace crucible
{
    namespace scripting
    {
        AssemblyLoadResult DLLLoader::loadAssembly(const std::string& contextName, const std::string& assemblyPath, bool collectible)
        {
            return _managedLoadAssembly(contextName.c_str(), assemblyPath.c_str(), collectible);
        }

        void DLLLoader::unloadAssembly(const std::string& assemblyPath)
        {
            _managedUnloadAssembly(assemblyPath.c_str());
        }

        void DLLLoader::unloadAllContexts()
        {
            _managedUnloadAllContexts();
        }
    } // scripting
} // crucible