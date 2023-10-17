#include "Mesh.h"
using namespace slag;
namespace crucible
{
    Mesh::Mesh(std::vector<Vertex3D>&& verticies, std::vector<uint16_t>&& indicies)
    {
        _verticies = Buffer::create(verticies.data(),sizeof(Vertex3D)*verticies.size(),Buffer::Usage::GPU_ONLY);
        _indicies = Buffer::create(indicies.data(),sizeof(uint16_t)*indicies.size(),Buffer::Usage::GPU_ONLY);
    }

    Mesh::~Mesh()
    {
        if(_verticies)
        {
            delete _verticies;
        }
        if(_indicies)
        {
            delete _indicies;
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

    slag::Buffer* Mesh::verticies()
    {
        return _verticies;
    }

    slag::Buffer* Mesh::indicies()
    {
        return _indicies;
    }

    void Mesh::move(Mesh&& from)
    {
        std::swap(_verticies,from._verticies);
        std::swap(_indicies,from._indicies);
    }

} // crucible