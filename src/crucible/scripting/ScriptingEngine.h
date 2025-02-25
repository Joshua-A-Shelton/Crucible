#ifndef CRUCIBLE_SCRIPTINGENGINE_H
#define CRUCIBLE_SCRIPTINGENGINE_H
#include "../CrucibleCore.h"
#include <hostfxr.h>
#include <coreclr_delegates.h>
#include "ManagedType.h"
#include <vector>

namespace crucible
{
    namespace scripting
    {

        struct CRUCIBLE_API FunctionMapping
        {
            const char_t* CSharpClassName = nullptr;
            const char_t* CSharpDelegateName = nullptr;
            void** NativeFunctionPointer = nullptr;
        };

        struct CRUCIBLE_API ManagedFunctionPointers
        {
            void* (*registerUnmanagedFunction)(FunctionMapping&) = nullptr;
            void* (*newInstance)(void*,void*) = nullptr;
            void* (*freeUnmanagedGCHandle)(void*) = nullptr;
            void* (*getFunctionPointer)(ManagedType&, const char* FunctionName, void**)= nullptr;
            void* (*getType)(const char* assemblyQualifiedName, ManagedType& type)= nullptr;
            void* (*loadLibrary)(const char* contextName,const char* path,bool collectible)=nullptr;
            void* (*unloadLibrary)(const char* path)= nullptr;
            void* (*unloadAllContexts)()= nullptr;
        };

        struct CRUCIBLE_API Interop
        {
            static inline ManagedFunctionPointers managedFunctionPointers;
        };

        class CRUCIBLE_API ScriptingEngine
        {
        public:
            static void initialize();
            static void cleanup();

            static ManagedType getManagedType(const std::string& typeName);
            static void* getManagedFunction(ManagedType& type,const std::string& fullyQualifiedFunctionName);
            static void registerUnmanagedFunction(const std::string& assemblyQualifiedClassName, const std::string& managedDelegateName, void** functionPointer);
            static void loadManagedDll(const char* contextName, const char* path, bool collectible);
            static void unloadManagedDllContext(const char* contextName);
            static void* newInstance(const ManagedType& type);
            static void freeGCHandle(void* handle);
        private:
            static bool loadHostFXR();
            static load_assembly_and_get_function_pointer_fn get_dotnet_load_assembly(const char_t *config_path);
            static void registerManagedFunctions();

        };

    } // scripting
} // crucible

#endif //CRUCIBLE_SCRIPTINGENGINE_H
