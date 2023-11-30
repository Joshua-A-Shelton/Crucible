#ifndef CRUCIBLE_INTEROP_H
#define CRUCIBLE_INTEROP_H
#include <glm/vec3.hpp>
#include <nethost/hostfxr.h>
#include <vector>
#include <string>
#include "ManagedObjectGCHandle.h"
#include "ManagedType.h"
#include "ManagedFunction.h"

namespace crucible
{

    struct FunctionMapping
    {
        const char_t* CSharpClassName = nullptr;
        const char_t* CSharpDelegateName = nullptr;
        void** NativeFunctionPointer = nullptr;
    };

    struct ManagedFunctionPointers
    {
        void* (*registerUnmanagedFunction)(FunctionMapping&) = nullptr;
        void* (*freeUnmanagedGCHandle)(void*) = nullptr;
        void* (*getFunctionPointer)(ManagedType&, const char* FunctionName, void**)= nullptr;
        void* (*getType)(const char* assemblyQualifiedName, ManagedType& type)= nullptr;
        void (*loadLibrary)(const char* path)=nullptr;
    };

    struct Interop
    {
        static inline ManagedFunctionPointers managedFunctionPointers;
    };


    extern "C"
    {
    //Miscellaneous
    void cruciblePushString(std::vector<std::string> &vector, char *string);

    //Vectors
    void crucibleVector3Cross(glm::vec3 &v1, glm::vec3 &v2, glm::vec3 &outResult);
    void crucibleVector3Dot(glm::vec3 &v1, glm::vec3 &v2, float &outResult);
    }
}





#endif //CRUCIBLE_INTEROP_H
