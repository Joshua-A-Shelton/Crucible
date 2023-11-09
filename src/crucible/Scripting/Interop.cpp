#include "Interop.h"
#include <glm/geometric.hpp>
void crucibleVector3Cross(glm::vec3 &v1, glm::vec3 &v2, glm::vec3 &outResult)
{
    outResult = glm::cross(v1,v2);
}