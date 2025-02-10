#ifndef CRUCIBLE_COREFUNCTIONS_H
#define CRUCIBLE_COREFUNCTIONS_H
#include "ScriptingEngine.h"
#include <glm/glm.hpp>

#define CRUCIBLE_CORE_DELEGATES \
DELEGATE(Crucible.Core.Matrix4x4, void, _matrixMultiply_ptr, cs_Matrix4x4Multiply, glm::mat4& m1, glm::mat4& m2, glm::mat4& result)\




namespace crucible
{
    namespace scripting
    {

        extern "C"
        {
#define DELEGATE(className, returnValue, delegateName, functionName, ...) returnValue functionName(__VA_ARGS__);
        CRUCIBLE_CORE_DELEGATES
#undef DELEGATE
        }
        class CoreFunctionsInitializer
        {
        public:
            CoreFunctionsInitializer()
            {
#define DELEGATE(className, returnValue, delegateName, functionName, ...) ScriptingEngine::registerUnmanagedFunction(#className,#delegateName,(void**)functionName);
                CRUCIBLE_CORE_DELEGATES
#undef DELEGATE
            }
        };

    } // scripting
} // crucible

#endif //CRUCIBLE_COREFUNCTIONS_H
