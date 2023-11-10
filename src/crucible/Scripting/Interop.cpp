#include "Interop.h"
#include <glm/geometric.hpp>
namespace crucible
{
    void crucibleVector3Cross(glm::vec3 &v1, glm::vec3 &v2, glm::vec3 &outResult)
    {
        outResult = glm::cross(v1,v2);
    }

    void crucibleVector3Dot(glm::vec3& v1, glm::vec3& v2, float& outResult)
    {
        outResult = glm::dot(v1,v2);
    }
}
