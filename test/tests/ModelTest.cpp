#include <gtest/gtest.h>
#include <crucible/core/Model.h>

using namespace crucible::core;
TEST(Model, Load)
{
    std::filesystem::path modelPath = "resources/cube.cmodel";
    Model model(modelPath);
    GTEST_ASSERT_EQ(model.meshCount(),1);
    auto& mesh = model.mesh(0);
    int i=0;
    GTEST_ASSERT_EQ(mesh.vertexCount(),24);
    auto attributes = mesh.definedAttributes();
    GTEST_ASSERT_EQ(attributes.size(),3);

    GTEST_ASSERT_EQ(attributes[0],Mesh::VertexAttribute::POSITION_3D);
    GTEST_ASSERT_EQ(attributes[1],Mesh::VertexAttribute::NORMAL);
    GTEST_ASSERT_EQ(attributes[2],Mesh::VertexAttribute::UV);
}