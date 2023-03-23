#ifndef CRUCIBLE_MESH_H
#define CRUCIBLE_MESH_H

#include <slag/VertexBuffer.h>
#include "Vertex.h"

namespace crucible
{
    class Mesh
    {
    private:
        std::vector<Vertex> _verticies;
        slag::VertexBuffer _buffer;
    public:
        Mesh(std::vector<Vertex> verticies);
        Mesh()=delete;
        Mesh(const Mesh&)=delete;
        Mesh& operator=(const Mesh&)=delete;

        slag::VertexBuffer& vertexBuffer();
    };
} // crucible
#endif //CRUCIBLE_MESH_H