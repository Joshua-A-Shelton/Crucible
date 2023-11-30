#ifndef CRUCIBLE_VERTEX3D_H
#define CRUCIBLE_VERTEX3D_H
#include "../Crucible.h"
#include <glm/glm.hpp>

namespace crucible
{
    class CRUCIBLE_EXPORT Vertex3D
    {
        glm::vec3 position;
        glm::vec3 normal;
        glm::vec2 uv;
    };
} // crucible
#endif //CRUCIBLE_VERTEX3D_H