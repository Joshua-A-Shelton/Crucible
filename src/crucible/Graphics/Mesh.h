#ifndef CRUCIBLE_MESH_H
#define CRUCIBLE_MESH_H
#include <slag/SlagLib.h>
namespace crucible
{
    class Mesh
    {
    public:
        slag::Buffer* verticies();
        slag::Buffer* indicies();
    private:
        slag::Buffer* _verticies = nullptr;
        slag::Buffer* _indicies = nullptr;
    };
} // crucible
#endif //CRUCIBLE_MESH_H