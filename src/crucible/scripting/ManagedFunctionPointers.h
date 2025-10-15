#ifndef CRUCIBLE_MANAGEDFUNCTIONPOINTERS_H
#define CRUCIBLE_MANAGEDFUNCTIONPOINTERS_H
#include <crucible/CrucibleCore.h>
namespace crucible::scripting
{
    struct CRUCIBLE_API FunctionMapping
    {
        const char_t* CSharpClassName = nullptr;
        const char_t* CSharpDelegateName = nullptr;
        void** NativeFunctionPointer = nullptr;
    };
    struct CRUCIBLE_API ManagedFunctionPointers
    {
        void* (*registerUnmanagedFunction)(const FunctionMapping&) = nullptr;
    };
}
#endif //CRUCIBLE_MANAGEDFUNCTIONPOINTERS_H