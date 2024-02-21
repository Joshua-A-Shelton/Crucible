#ifndef CRUCIBLEEDITOR_VERTEX2D_H
#define CRUCIBLEEDITOR_VERTEX2D_H
#include "../Crucible.h"
#include <glm/glm.hpp>
namespace crucible
{
    struct CRUCIBLE_EXPORT Vertex2D
    {
        glm::vec2 position;
        glm::vec2 uv;
    };
} // crucible
#endif //CRUCIBLEEDITOR_VERTEX2D_H
