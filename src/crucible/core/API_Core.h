#ifndef CRUCIBLE_API_CORE_H
#define CRUCIBLE_API_CORE_H
#include <glm/glm.hpp>
#include <crucible/core/CrucibleAPI.h>

extern "C"
{
    CRUCIBLE_API float CRUCIBLE_API_Vector3DotProduct(const glm::vec3& a, const glm::vec3& b);
    CRUCIBLE_API void CRUCIBLE_API_Vector3CrossProduct(const glm::vec3& a, const glm::vec3& b, glm::vec3& out);
}

#endif //CRUCIBLE_API_CORE_H