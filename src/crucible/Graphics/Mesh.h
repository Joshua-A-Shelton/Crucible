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
        Mesh(std::vector<Vertex3D>& verticies, std::vector<uint16_t>& indecies);
        ~Mesh();
        Mesh(const Mesh&)=delete;
        Mesh& operator=(Mesh&)=delete;
        Mesh(Mesh&& from);
        Mesh& operator=(Mesh&& from);
        slag::VertexBuffer* verticies();
        slag::IndexBuffer* indecies();
        size_t vertexCount();
        size_t indeciesCount();
    private:
        size_t _vertexCount;
        slag::VertexBuffer* _verticies = nullptr;
        size_t _indeciesCount;
        slag::IndexBuffer* _indecies = nullptr;
        void move(Mesh&& from);
    };

} // crucible
#endif //CRUCIBLE_MESH_H