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
            CRUCIBLE_API void CRUCIBLE_NATIVE_Matrix4X4Multiply(glm::mat4& a, glm::mat4& b, glm::mat4& out);

            CRUCIBLE_API void CRUCIBLE_NATIVE_QuaternionFromAngleAxis(float angle, glm::vec3& axis, glm::quat& out);
            CRUCIBLE_API void CRUCIBLE_NATIVE_QuaternionFromEulerAngles(glm::vec3& euler, glm::quat& out);
            CRUCIBLE_API void CRUCIBLE_NATIVE_QuaternionMultiply(glm::quat& a, glm::quat& b, glm::quat& out);
            CRUCIBLE_API void CRUCIBLE_NATIVE_QuaternionInverse(glm::quat& of, glm::quat& out);

            CRUCIBLE_API void CRUCIBLE_NATIVE_Vector4MatrixMultipy(glm::vec4& vector, glm::mat4& matrix, glm::vec4& out);
            CRUCIBLE_API float CRUCIBLE_NATIVE_Vector4DotProduct(glm::vec4& a, glm::vec4& b);

            CRUCIBLE_API void CRUCIBLE_NATIVE_Vector3MatrixMultipy(glm::vec3& vector, glm::mat4& matrix, glm::vec3& out);
            CRUCIBLE_API float CRUCIBLE_NATIVE_Vector3DotProduct(glm::vec3& a,glm::vec3& b);
            CRUCIBLE_API void CRUCIBLE_NATIVE_Vector3CrossProduct(glm::vec3& a, glm::vec3&b, glm::vec3& out);
        }
    } // scripting
} // slag

#endif //CRUCIBLE_API_H
