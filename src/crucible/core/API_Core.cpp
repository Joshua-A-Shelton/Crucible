#include "API_Core.h"

float CRUCIBLE_API_Vector3DotProduct(const glm::vec3& a, const glm::vec3& b)
{
    return glm::dot(a,b);
}

void CRUCIBLE_API_Vector3CrossProduct(const glm::vec3& a, const glm::vec3& b, glm::vec3& out)
{
    out = glm::cross(a,b);
}
