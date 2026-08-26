#ifndef CRUCIBLE_CRUCIBLE_CALLBACKS_H
#define CRUCIBLE_CRUCIBLE_CALLBACKS_H
namespace crucible
{
    namespace scripting
    {
        typedef void (*VoidCallback)();
        typedef void (*UpdateCallback)(double);

        inline VoidCallback ScriptingInitializeCallback = nullptr;
        inline UpdateCallback ScriptingUpdateCallback = nullptr;
        inline VoidCallback ScriptingCleanupCallback = nullptr;

        struct CallbackLocations
        {
            VoidCallback initialize = nullptr;
            UpdateCallback update = nullptr;
            VoidCallback cleanup = nullptr;
        };
    }
}
#endif //CRUCIBLE_CRUCIBLE_CALLBACKS_H