#ifndef CRUCIBLE_SCRIPTINGENGINE_H
#define CRUCIBLE_SCRIPTINGENGINE_H

#include "../Crucible.h"
#include <string>
#include "nethost/hostfxr.h"
#include "nethost/coreclr_delegates.h"
#include "Interop.h"

namespace crucible
{

    class CRUCIBLE_EXPORT ScriptingEngine
    {
    public:
        static void initialize();
        static void cleanup();

        static ManagedType getManagedType(const std::string& typeName);
        static void* getManagedFunction(ManagedType& type,const std::string& fullyQualifiedFunctionName);
        static void registerUnmanagedFunction(const std::string& assemblyQualifiedClassName, const std::string& managedDelegateName, void** functionPointer);
        static void loadManagedDll(const char* path);
    private:
        static bool loadHostFXR();
        static load_assembly_and_get_function_pointer_fn get_dotnet_load_assembly(const char_t *config_path);

        static void registerCoreFunctions();
        static void getCoreFunctions();
    };

} // crucible

#endif //CRUCIBLE_SCRIPTINGENGINE_H
