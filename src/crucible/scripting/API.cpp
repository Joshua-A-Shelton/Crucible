#include "API.h"

#include <vector>
#include <glm/gtc/quaternion.hpp>

namespace slag
{
    namespace scripting
    {

        void CRUCIBLE_NATIVE_Matrix4X4Multiply(glm::mat4& a, glm::mat4& b, glm::mat4& out)
        {
            out = a*b;
        }

        void CRUCIBLE_NATIVE_QuaternionFromAngleAxis(float angle, glm::vec3& axis, glm::quat& out)
        {
            out = glm::quat(angle,axis);
        }

        void CRUCIBLE_NATIVE_QuaternionFromEulerAngles(glm::vec3& eulerAngles, glm::quat& out)
        {
            out = glm::quat(eulerAngles);
        }

        void CRUCIBLE_NATIVE_QuaternionMultiply(glm::quat& a, glm::quat& b, glm::quat& out)
        {
            out = a*b;
        }

        void CRUCIBLE_NATIVE_QuaternionInverse(glm::quat& of, glm::quat& out)
        {
            out = glm::inverse(of);
        }


        void CRUCIBLE_NATIVE_Vector4MatrixMultipy(glm::vec4& vector, glm::mat4& matrix, glm::vec4& out)
        {
            out = vector*matrix;
        }

        float CRUCIBLE_NATIVE_Vector4DotProduct(glm::vec4& a, glm::vec4& b)
        {
            return glm::dot(a,b);
        }

        void CRUCIBLE_NATIVE_Vector3MatrixMultipy(glm::vec3& vector, glm::mat4& matrix, glm::vec3& out)
        {
            glm::vec4 vec(vector,1);
            out = matrix*vec;
        }

        float CRUCIBLE_NATIVE_Vector3DotProduct(glm::vec3& a, glm::vec3& b)
        {
            return glm::dot(a,b);
        }

        void CRUCIBLE_NATIVE_Vector3CrossProduct(glm::vec3& a,glm::vec3&b, glm::vec3& out)
        {
            out = glm::cross(a,b);
        }
    } // scripting
} // slag
