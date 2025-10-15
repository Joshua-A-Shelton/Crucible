#ifndef CRUCIBLE_MANAGEDFUNCTIONPOINTERS_H
#define CRUCIBLE_MANAGEDFUNCTIONPOINTERS_H
#include <crucible/CrucibleCore.h>
#include "ManagedType.h"
namespace crucible::scripting
{
    enum class BindingFlags;

    struct CRUCIBLE_API ManagedFunctionPointers
    {
        void (*loadAssembly)(const char* contextName, const char* path, bool collectible) = nullptr;
        void (*unloadContext)(const char* contextName) = nullptr;
        void (*unloadAllContexts)() = nullptr;
        void (*getManagedType)(const char* typeName, ManagedType& outType) = nullptr;
        void (*getManagedFunction)(ManagedType& onType, const char* functionName, BindingFlags flags, ManagedType* parameterArray, int32_t parameterTypeCount, ManagedFunctionInternals& outFunctionInternals) = nullptr;
        void (*newInstance)(ManagedType& type, void** outInstance) = nullptr;
        void (*freeInstance)(void* instance) = nullptr;
    };
}
#endif //CRUCIBLE_MANAGEDFUNCTIONPOINTERS_H