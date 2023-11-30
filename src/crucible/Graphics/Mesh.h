#ifndef CRUCIBLE_MESH_H
#define CRUCIBLE_MESH_H
#include "../Crucible.h"
#include <slag/SlagLib.h>
#include <vector>
#include "Vertex3D.h"
namespace crucible
{
    class CRUCIBLE_EXPORT Mesh
    {
    public:
        Mesh(std::vector<Vertex3D>&& verticies, std::vector<uint16_t>&& indicies);
        ~Mesh();
        Mesh(const Mesh&)=delete;
        Mesh& operator=(Mesh&)=delete;
        Mesh(Mesh&& from);
        Mesh& operator=(Mesh&& from);
        slag::Buffer* verticies();
        slag::Buffer* indicies();
    private:
        slag::Buffer* _verticies = nullptr;
        slag::Buffer* _indicies = nullptr;
        void move(Mesh&& from);
    };
} // crucible
#endif //CRUCIBLE_MESH_H