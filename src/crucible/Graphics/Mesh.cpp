#include "Mesh.h"
using namespace slag;
namespace crucible
{
    Mesh::Mesh(std::vector<Vertex3D>& verticies, std::vector<uint16_t>& indecies)
    {
        _verticies = VertexBuffer::create(verticies.data(),sizeof(Vertex3D)*verticies.size(),Buffer::Usage::GPU);
        _vertexCount = verticies.size();
        _indecies = IndexBuffer::create(indecies.data(),sizeof(uint16_t)*indecies.size(),Buffer::Usage::GPU);
        _indeciesCount = indecies.size();
    }

    Mesh::~Mesh()
    {
        if(_verticies)
        {
            delete _verticies;
        }
        if(_indecies)
        {
            delete _indecies;
        }

    }

    Mesh::Mesh(Mesh&& from)
    {
        move(std::move(from));
    }

    Mesh& Mesh::operator=(Mesh&& from)
    {
        move(std::move(from));
        return *this;
    }

    slag::VertexBuffer* Mesh::verticies()
    {
        return _verticies;
    }

    slag::IndexBuffer* Mesh::indecies()
    {
        return _indecies;
    }

    void Mesh::move(Mesh&& from)
    {
        std::swap(_verticies,from._verticies);
        std::swap(_indecies,from._indecies);
    }

    size_t Mesh::vertexCount()
    {
        return _vertexCount;
    }

    size_t Mesh::indeciesCount()
    {
        return _indeciesCount;
    }

} // crucible