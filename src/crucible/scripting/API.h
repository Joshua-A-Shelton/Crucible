#ifndef CRUCIBLE_API_H
#define CRUCIBLE_API_H
#include "crucible/CrucibleCore.h"
#include <glm/glm.hpp>
namespace slag
{
    namespace scripting
    {
        extern "C"
        {
            CRUCIBLE_API float NATIVE_Vector3Dot(glm::vec3& a, glm::vec3& b);
            CRUCIBLE_API void NATIVE_Vector3Cross(glm::vec3& a, glm::vec3& b, glm::vec3& out);
        }
    } // scripting
} // slag

#endif //CRUCIBLE_API_H
