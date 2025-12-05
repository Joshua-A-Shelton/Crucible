#include <gtest/gtest.h>
#include <crucible/Mesh.h>
using namespace crucible;
/*
//VertexAttribute, MeshBufferAccessibility field,  accessorFunction, customStride1, customStride2
#define CRUCIBLE_MESH_ATTRIBUTES_DEFINITIONS(DEFINITION)\
DEFINITION(POSITION, position, positionBuffer)\
DEFINITION(NORMAL, normal, normalBuffer)\
DEFINITION(TANGENT, tangent, tangentBuffer)\
DEFINITION(COLOR, color, colorBuffer)\
DEFINITION(BONE_WEIGHT, boneWeight, boneWeightBuffer)\
DEFINITION(UV, uv, uvBuffer)\
DEFINITION(UV2, uv2, uvBuffer2)\
DEFINITION(UV3, uv3, uvBuffer3)\
DEFINITION(UV4, uv4, uvBuffer4)\


#define FOR_ACCESSIBILITY slag::Buffer::Accessibility::GPU
#define AGAINST_ACCESSIBILITY slag::Buffer::Accessibility::CPU_AND_GPU

#define DEFINITION(VERTEX_ATTRIBUTE, MESH_BUFFER_ACCESSIBILITY, ACCESSOR_FUNCTION)\
TEST(Mesh, MeshInstantiateAccessibility_GPU_##VERTEX_ATTRIBUTE)\
{\
    Mesh::MeshBufferAccessibility accessibility;\
    accessibility.position = AGAINST_ACCESSIBILITY;\
    accessibility.normal = AGAINST_ACCESSIBILITY;\
    accessibility.tangent = AGAINST_ACCESSIBILITY;\
    accessibility.color = AGAINST_ACCESSIBILITY;\
    accessibility.boneWeight = AGAINST_ACCESSIBILITY;\
    accessibility.uv = AGAINST_ACCESSIBILITY;\
    accessibility.uv2 = AGAINST_ACCESSIBILITY;\
    accessibility.uv3 = AGAINST_ACCESSIBILITY;\
    accessibility.uv4 = AGAINST_ACCESSIBILITY;\
    accessibility.MESH_BUFFER_ACCESSIBILITY = FOR_ACCESSIBILITY;\
    Mesh mesh(Mesh::VertexAttribute::POSITION | Mesh::VertexAttribute::VERTEX_ATTRIBUTE,3,slag::Buffer::IndexSize::UINT16,3,accessibility);\
    auto buffer = mesh.ACCESSOR_FUNCTION();\
    GTEST_ASSERT_TRUE(buffer->accessibility() == FOR_ACCESSIBILITY);\
}

CRUCIBLE_MESH_ATTRIBUTES_DEFINITIONS(DEFINITION)

#undef DEFINITION
#undef FOR_ACCESSIBILITY
#undef AGAINST_ACCESSIBILITY

#define FOR_ACCESSIBILITY slag::Buffer::Accessibility::CPU_AND_GPU
#define AGAINST_ACCESSIBILITY slag::Buffer::Accessibility::GPU

#define DEFINITION(VERTEX_ATTRIBUTE, MESH_BUFFER_ACCESSIBILITY, ACCESSOR_FUNCTION)\
TEST(Mesh, MeshInstantiateAccessibility_CPU_##VERTEX_ATTRIBUTE)\
{\
Mesh::MeshBufferAccessibility accessibility;\
accessibility.position = AGAINST_ACCESSIBILITY;\
accessibility.normal = AGAINST_ACCESSIBILITY;\
accessibility.tangent = AGAINST_ACCESSIBILITY;\
accessibility.color = AGAINST_ACCESSIBILITY;\
accessibility.boneWeight = AGAINST_ACCESSIBILITY;\
accessibility.uv = AGAINST_ACCESSIBILITY;\
accessibility.uv2 = AGAINST_ACCESSIBILITY;\
accessibility.uv3 = AGAINST_ACCESSIBILITY;\
accessibility.uv4 = AGAINST_ACCESSIBILITY;\
accessibility.MESH_BUFFER_ACCESSIBILITY = FOR_ACCESSIBILITY;\
Mesh mesh(Mesh::VertexAttribute::POSITION | Mesh::VertexAttribute::VERTEX_ATTRIBUTE,3,slag::Buffer::IndexSize::UINT16,3,accessibility);\
auto buffer = mesh.ACCESSOR_FUNCTION();\
GTEST_ASSERT_TRUE(buffer->accessibility() == FOR_ACCESSIBILITY);\
}

CRUCIBLE_MESH_ATTRIBUTES_DEFINITIONS(DEFINITION)

#undef DEFINITION

#undef FOR_ACCESSIBILITY
#undef AGAINST_ACCESSIBILITY


TEST(Mesh, FileMalformedThrowOnLoad)
{
    GTEST_FAIL();
}*/
TEST(MeshAttributeData, Defined)
{
    GTEST_FAIL();
}
TEST(MeshAttributeData, AttributesToBuffers)
{
    GTEST_FAIL();
}