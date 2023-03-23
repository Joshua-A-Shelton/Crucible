#include "Mesh.h"

namespace crucible
{
    Mesh::Mesh(std::vector<Vertex> verticies):_verticies(verticies), _buffer(_verticies.data(), _verticies.size()*sizeof(Vertex),slag::VertexBuffer::Usage::CPU_TO_GPU)
    {
    }

    slag::VertexBuffer& Mesh::vertexBuffer()
    {
        return _buffer;
    }
} // crucible