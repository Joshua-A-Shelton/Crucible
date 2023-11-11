#ifndef CRUCIBLE_INTEROP_H
#define CRUCIBLE_INTEROP_H
#include <glm/vec3.hpp>
#include <nethost/hostfxr.h>


namespace crucible
{

    struct FunctionMapping
    {
        const char_t* CSharpFunctionName;
        void** NativeFunctionPointer;
    };

    struct InitArgs
    {
        void* (*RegisterUnmanagedFunction)(FunctionMapping&) = nullptr;
    };

    void crucibleVector3Cross(glm::vec3& v1, glm::vec3& v2, glm::vec3& outResult);
    void crucibleVector3Dot(glm::vec3& v1, glm::vec3& v2, float& outResult);
}





#endif //CRUCIBLE_INTEROP_H
