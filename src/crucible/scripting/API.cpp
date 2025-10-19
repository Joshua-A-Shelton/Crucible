#include "API.h"

namespace slag
{
    namespace scripting
    {
        float NATIVE_Vector3Dot(glm::vec3& a, glm::vec3& b)
        {
            return glm::dot(a, b);
        }

        void NATIVE_Vector3Cross(glm::vec3& a, glm::vec3& b, glm::vec3& out)
        {
            out = glm::cross(a, b);
        }
    } // scripting
} // slag
