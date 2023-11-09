#ifndef CRUCIBLE_INTEROP_H
#define CRUCIBLE_INTEROP_H


#include <glm/vec3.hpp>
extern "C"
{
    void crucibleVector3Cross(glm::vec3& v1, glm::vec3& v2, glm::vec3& outResult);
}

#endif //CRUCIBLE_INTEROP_H
