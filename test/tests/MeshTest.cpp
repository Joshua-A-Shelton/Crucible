#include "gtest/gtest.h"
#include <crucible/core/Mesh.h>
#include <glm/glm.hpp>
using namespace  crucible::core;
TEST(Mesh, LZ4Load)
{

    std::vector<glm::vec3> verts{glm::vec3(1.0f, 0.0f, 0.0f),glm::vec3(0.0f,1.0f,0.0f),glm::vec3(0.0f,0.0f,1.0f)};
    std::vector<uint16_t> indexes{0,1,2};
    std::vector<Mesh::VertexAttributeInputInfo> streams;
    streams.emplace_back(Mesh::POSITION_3D,verts.data(),verts.size()*sizeof(glm::vec3),slag::Buffer::GPU);
    Mesh mesh(streams,indexes.data(),indexes.size(),slag::Buffer::UINT16);
    auto data = mesh.toData();
    Mesh mesh2(data.data(),&*data.end(),1);
    GTEST_FAIL();
}
