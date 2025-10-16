#ifndef CRUCIBLE_MANAGEDFUNCTIONPOINTERS_H
#define CRUCIBLE_MANAGEDFUNCTIONPOINTERS_H
#include <crucible/CrucibleCore.h>
#include "ManagedType.h"
namespace crucible::scripting
{
    enum class BindingFlags;

    struct CRUCIBLE_API CSharpFunctionPointers
    {
        void (*loadAssembly)(const char* contextName, const char* path, bool collectible) = nullptr;
        void (*unloadContext)(const char* contextName) = nullptr;
        void (*unloadAllContexts)() = nullptr;
        void (*getManagedType)(const char* typeName, ManagedType& outType) = nullptr;
        void (*getManagedFunctionDelegate)(ManagedType& onType, const char* functionName, BindingFlags flags, ManagedType* parameterArray, int32_t parameterTypeCount, ManagedFunctionDelegateInternals& outFunctionInternals) = nullptr;
        void (*newInstance)(ManagedType& type, int32_t, ManagedType* parameterTypes, void** parameter, void** outInstance) = nullptr;
        void (*freeInstance)(void* instance) = nullptr;
        void (*invokeInstanceMethod)(void* instanceType,void* instanceHandle, const char* methodName, int32_t parameterCount, void** parameterTypes, void** parameters);
        void (*invokeInstanceMethodReturnReference)(void* instanceType,void* instanceHandle, const char* methodName, int32_t parameterCount, void** parameterTypes, void** parameters,void** returnObjectHandle,ManagedType& returnType);
        void (*invokeInstanceMethodReturnValue)(void* instanceType,void* instanceHandle, const char* methodName, int32_t parameterCount, void** parameterTypes, void** parameters,void* valuePtr);
        void (*gameManagerInitialize)() = nullptr;
        void (*gameManagerCleanUp)() = nullptr;
    };
}
#endif //CRUCIBLE_MANAGEDFUNCTIONPOINTERS_H