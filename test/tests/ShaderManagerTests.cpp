#include "gtest/gtest.h"
#include <crucible/core//ShaderManager.h>
using namespace crucible::core;
TEST(ShaderManagerTests, NullShader)
{
    auto shaderRef =ShaderManager::getShaderReference("Null");
    GTEST_ASSERT_TRUE(shaderRef.requiredAttributesCount()==1);
    GTEST_ASSERT_TRUE(shaderRef.attribute(0) == Mesh::VertexAttribute::POSITION_3D);

    GTEST_ASSERT_EQ(shaderRef.pipeline()->descriptorGroupCount(),3);
    //skip globals, those can change
    //slag::Descriptor::Shape globalsShape(slag::Descriptor::UNIFORM_BUFFER,1,0,slag::ShaderStageFlags::VERTEX);
    slag::Descriptor::Shape passShape(slag::Descriptor::UNIFORM_BUFFER,1,0,slag::ShaderStageFlags::VERTEX | slag::ShaderStageFlags::FRAGMENT);

    GTEST_ASSERT_EQ(shaderRef.pipeline()->descriptorGroup(0)->descriptorCount(),1);
    GTEST_ASSERT_EQ(shaderRef.pipeline()->descriptorGroup(1)->descriptorCount(),1);

    GTEST_ASSERT_EQ(shaderRef.pipeline()->descriptorGroup(1)->descriptor(0).shape(),passShape);
}
TEST(ShaderManagerTests, FlatTestShader)
{
    auto shaderRef =ShaderManager::getShaderReference("Flat-Textured");
    GTEST_ASSERT_TRUE(shaderRef.requiredAttributesCount()==2);
    GTEST_ASSERT_TRUE(shaderRef.attribute(0) == Mesh::VertexAttribute::POSITION_3D);
    GTEST_ASSERT_TRUE(shaderRef.attribute(1) == Mesh::VertexAttribute::UV);

    GTEST_ASSERT_EQ(shaderRef.pipeline()->descriptorGroupCount(),3);
    //skip globals, those can change
    //slag::Descriptor::Shape globalsShape(slag::Descriptor::UNIFORM_BUFFER,1,0,slag::ShaderStageFlags::VERTEX);
    slag::Descriptor::Shape passShape(slag::Descriptor::UNIFORM_BUFFER,1,0,slag::ShaderStageFlags::VERTEX | slag::ShaderStageFlags::FRAGMENT);
    slag::Descriptor::Shape ubo(slag::Descriptor::UNIFORM_BUFFER,1,0,slag::ShaderStageFlags::VERTEX | slag::ShaderStageFlags::FRAGMENT);
    slag::Descriptor::Shape uboTex(slag::Descriptor::SAMPLER_AND_TEXTURE,1,1,slag::ShaderStageFlags::VERTEX | slag::ShaderStageFlags::FRAGMENT);

    GTEST_ASSERT_EQ(shaderRef.pipeline()->descriptorGroup(1)->descriptorCount(),1);
    GTEST_ASSERT_EQ(shaderRef.pipeline()->descriptorGroup(2)->descriptorCount(),2);

    GTEST_ASSERT_EQ(shaderRef.pipeline()->descriptorGroup(1)->descriptor(0).shape(),passShape);
    GTEST_ASSERT_EQ(shaderRef.pipeline()->descriptorGroup(2)->descriptor(0).shape(),ubo);
    GTEST_ASSERT_EQ(shaderRef.pipeline()->descriptorGroup(2)->descriptor(1).shape(),uboTex);
}
