#include "Mesh.h"

namespace crucible
{
    slag::Buffer* Mesh::verticies()
    {
        return _verticies;
    }

    slag::Buffer* Mesh::indicies()
    {
        return _indicies;
    }
} // crucible