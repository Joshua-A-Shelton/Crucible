#ifndef CRUCIBLE_SCRIPTINGENGINE_H
#define CRUCIBLE_SCRIPTINGENGINE_H

#include "nethost/hostfxr.h"
#include "nethost/coreclr_delegates.h"

namespace crucible
{

    class ScriptingEngine
    {
    public:
        static void initialize();
        static void cleanup();
    private:
        static bool loadHostFXR();
        static load_assembly_and_get_function_pointer_fn get_dotnet_load_assembly(const char_t *config_path);
    };

} // crucible

#endif //CRUCIBLE_SCRIPTINGENGINE_H
