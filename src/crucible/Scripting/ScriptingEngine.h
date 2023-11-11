#ifndef CRUCIBLE_SCRIPTINGENGINE_H
#define CRUCIBLE_SCRIPTINGENGINE_H

#include <string>
#include "nethost/hostfxr.h"
#include "nethost/coreclr_delegates.h"
#include "Interop.h"

namespace crucible
{

    class ScriptingEngine
    {
    public:
        static void initialize();
        static void cleanup();
        friend class ManagedObjectHandle;
    private:
        static bool loadHostFXR();
        static load_assembly_and_get_function_pointer_fn get_dotnet_load_assembly(const char_t *config_path);

        static void registerUnmanagedFunction(const std::string& managedFunctionName, void** functionPointer);
        //Managed Function Pointers
        static inline void* (*_registerUnmanagedFunction_fn_ptr)(FunctionMapping&) = nullptr;
        static inline void* (*_freeUnmanagedGCHandleFunction_fn_ptr)(void*) = nullptr;
        static inline void* (*_getComponentTypesFunction_fn_ptr)(std::vector<std::string>&) = nullptr;

    };

} // crucible

#endif //CRUCIBLE_SCRIPTINGENGINE_H
