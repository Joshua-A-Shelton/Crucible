#include "CoreFunctions.h"

namespace crucible
{
    namespace scripting
    {
        void cs_Matrix4x4Multiply(glm::mat4& m1, glm::mat4& m2, glm::mat4& result)
        {
            result = m1 * m2;
        }
    } // scripting
} // crucible