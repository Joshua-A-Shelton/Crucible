#include "Interop.h"
#include <glm/geometric.hpp>

namespace crucible
{
    void crucibleVector3Cross(glm::vec3 &v1, glm::vec3 &v2, glm::vec3 &outResult)
    {
        outResult = glm::cross(v1,v2);
    }

    void crucibleVector3Dot(glm::vec3& v1, glm::vec3& v2, float& outResult)
    {
        outResult = glm::dot(v1,v2);
    }

    Mesh *cs_CreateMesh()
    {
        std::vector<Vertex3D> verts
                {
                        Vertex3D{.position{-1,1,0},.normal{0,0,1},.uv{0,0}},
                        Vertex3D{.position{1,1,0},.normal{0,0,1},.uv{0,1}},
                        Vertex3D{.position{-1,-1,0},.normal{0,0,1},.uv{1,0}},
                        Vertex3D{.position{1,-1,0},.normal{0,0,1},.uv{1,1}},
                };
        std::vector<uint16_t> indecies
                {
                        0,1,2,1,3,2
                };
        return new Mesh(verts,indecies);
    }

    void cs_DeleteMesh(Mesh *mesh)
    {
        delete mesh;
    }

}
