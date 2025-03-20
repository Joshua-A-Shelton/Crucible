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
    Mesh mesh(streams,indexes.data(),indexes.size()*sizeof(uint16_t),slag::Buffer::UINT16);
    auto data = mesh.toData();
    Mesh mesh2(data.data(),data.size());
    GTEST_ASSERT_EQ(mesh2.vertexCount(),mesh.vertexCount());
    GTEST_ASSERT_EQ(mesh2.indexCount(),mesh.indexCount());
    GTEST_ASSERT_EQ(mesh2.indexSize(),mesh.indexSize());
    auto attributes = mesh.definedAttributes();
    auto attributes2 = mesh2.definedAttributes();
    GTEST_ASSERT_EQ(attributes.size(),attributes2.size());
    for (int i = 0; i < attributes.size(); ++i)
    {
        GTEST_ASSERT_EQ(attributes[i],attributes2[i]);
        auto data1 = mesh.getBuffer(attributes[i])->downloadData();
        auto data2 = mesh2.getBuffer(attributes2[i])->downloadData();
        for (size_t i=0; i<data1.size(); ++i)
        {
            GTEST_ASSERT_EQ(data1[i],data2[i]);
        }
    }

    auto index1 = mesh.indexBuffer()->downloadData();
    auto index2 = mesh2.indexBuffer()->downloadData();

    for (size_t i=0; i<index1.size(); ++i)
    {
        GTEST_ASSERT_EQ(index1[i],index2[i]);
    }
}
