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

        static ManagedType getManagedType(const std::string& typeName);
        static void* getManagedFunction(ManagedType& type,const std::string& fullyQualifiedFunctionName);
    private:
        static bool loadHostFXR();
        static load_assembly_and_get_function_pointer_fn get_dotnet_load_assembly(const char_t *config_path);

        static void registerUnmanagedFunction(const std::string& assemblyQualifiedClassName, const std::string& managedDelegateName, void** functionPointer);


    };

} // crucible

#endif //CRUCIBLE_SCRIPTINGENGINE_H
