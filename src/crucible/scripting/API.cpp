#include "API.h"

namespace slag
{
    namespace scripting
    {
        float NATIVE_Vector3Dot(glm::vec3& a, glm::vec3& b)
        {
            return glm::dot(a, b);
        }
    } // scripting
} // slag
