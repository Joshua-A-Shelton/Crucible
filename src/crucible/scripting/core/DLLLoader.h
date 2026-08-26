#ifndef CRUCIBLE_DLLLOADER_H
#define CRUCIBLE_DLLLOADER_H
#include <string>



namespace crucible
{
    namespace scripting
    {
        enum class AssemblyLoadResult
        {
            SUCCESS = 0,
            ASSEMBLY_NOT_FOUND = 1,
            UNKNOWN_ERROR
        };

        typedef AssemblyLoadResult (*ManagedLoadAssembly)(const char* contextName, const char* assemblyPath, bool collectible);
        typedef void (*ManagedUnloadAssembly)(const char* assemblyPath);
        typedef void (*ManagedUnloadAllContexts)();

        class DLLLoader
        {
        public:

            friend class ScriptingModule;
            static AssemblyLoadResult loadAssembly(const std::string& contextName, const std::string& assemblyPath, bool collectible = true);
            static void unloadAssembly(const std::string& assemblyPath);
            static void unloadAllContexts();
        private:
            inline static ManagedLoadAssembly _managedLoadAssembly = nullptr;
            inline static ManagedUnloadAssembly _managedUnloadAssembly = nullptr;
            inline static ManagedUnloadAllContexts _managedUnloadAllContexts = nullptr;
        };
    } // scripting
} // crucible

#endif //CRUCIBLE_DLLLOADER_H