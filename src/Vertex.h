#ifndef CRUCIBLE_VERTEX_H
#define CRUCIBLE_VERTEX_H

#include <slag/VertexDescription.h>
#include <glm/glm.hpp>

namespace crucible
{
    struct Vertex
    {
    public:
        glm::vec3 position;
        glm::vec3 normal;
        glm::vec3 color;
        glm::vec2 uv;
        static slag::VertexDescription& description();
    };
} // crucible
#endif //CRUCIBLE_VERTEX_H