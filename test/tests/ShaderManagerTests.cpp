#include "gtest/gtest.h"
#include <crucible/core//ShaderManager.h>
using namespace crucible::core;
TEST(ShaderManagerTests, LoadShader)
{
    auto shaderRef =ShaderManager::getShaderReference("test-basic");
    int i=0;
    GTEST_ASSERT_TRUE(shaderRef.requiredAttributesCount()==1);
    GTEST_ASSERT_TRUE(shaderRef.attribute(1) == Mesh::VertexAttribute::POSITION_3D);
    GTEST_FAIL();
}
