#ifndef CRUCIBLE_API_H
#define CRUCIBLE_API_H
#include <algorithm>
#include <__msvc_ranges_to.hpp>

#include "crucible/CrucibleCore.h"
#include "crucible/Node.h"
#include "crucible/Transform.h"
#include "crucible/Mesh.h"
#include "crucible/BoneWeights.h"
#include <glm/glm.hpp>

#include <slag/Slag.h>
#include <boost/uuid/uuid.hpp>



namespace crucible
{
    namespace scripting
    {
        extern "C"
        {
            CRUCIBLE_API void CRUCIBLE_NATIVE_Matrix4X4Multiply(const glm::mat4& a, const glm::mat4& b, glm::mat4& out);
            CRUCIBLE_API void CRUCIBLE_NATIVE_Matrix4X4FromPositionRotationScale(const glm::vec3& position, const glm::quat& rotation, const glm::vec3& scale, glm::mat4& out);

            CRUCIBLE_API void CRUCIBLE_NATIVE_QuaternionFromAngleAxis(float angle, const glm::vec3& axis, glm::quat& out);
            CRUCIBLE_API void CRUCIBLE_NATIVE_QuaternionFromEulerAngles(const glm::vec3& euler, glm::quat& out);
            CRUCIBLE_API void CRUCIBLE_NATIVE_QuaternionMultiply(const glm::quat& a, const glm::quat& b, glm::quat& out);
            CRUCIBLE_API void CRUCIBLE_NATIVE_QuaternionInverse(const glm::quat& of, glm::quat& out);
            CRUCIBLE_API float CRUCIBLE_NATIVE_QuaternionMagnitude(const glm::quat& quat);
            CRUCIBLE_API void CRUCIBLE_NATIVE_QuaternionNormalized(const glm::quat& quat, glm::quat& out);

            CRUCIBLE_API void CRUCIBLE_NATIVE_Vector4MatrixMultipy(const glm::vec4& vector, const glm::mat4& matrix, glm::vec4& out);
            CRUCIBLE_API float CRUCIBLE_NATIVE_Vector4DotProduct(const glm::vec4& a, const glm::vec4& b);
            CRUCIBLE_API float CRUCIBLE_NATIVE_Vector4Magnitude(const glm::vec4& vector);
            CRUCIBLE_API void CRUCIBLE_NATIVE_Vector4Normalized(const glm::vec4& vector, glm::vec4& out);

            CRUCIBLE_API void CRUCIBLE_NATIVE_Vector3MatrixMultipy(const glm::vec3& vector, const glm::mat4& matrix, glm::vec3& out);
            CRUCIBLE_API float CRUCIBLE_NATIVE_Vector3DotProduct(const glm::vec3& a, const glm::vec3& b);
            CRUCIBLE_API void CRUCIBLE_NATIVE_Vector3CrossProduct(const glm::vec3& a, const glm::vec3&b, glm::vec3& out);
            CRUCIBLE_API float CRUCIBLE_NATIVE_Vector3Magnitude(const glm::vec3& vector);
            CRUCIBLE_API void CRUCIBLE_NATIVE_Vector3Normalized(const glm::vec3& vector, glm::vec3& out);

            CRUCIBLE_API float CRUCIBLE_NATIVE_Vector2DotProduct(const glm::vec2& a, const glm::vec2& b);
            CRUCIBLE_API float CRUCIBLE_NATIVE_Vector2Magnitude(const glm::vec2& vector);
            CRUCIBLE_API void CRUCIBLE_NATIVE_Vector2Normalized(const glm::vec2& vector, glm::vec2& out);

            CRUCIBLE_API int32_t CRUCIBLE_NATIVE_LZ4MaxCompressedSize(int32_t uncompressedSize);
            CRUCIBLE_API int32_t CRUCIBLE_NATIVE_LZ4Compress(void* uncompressedData, int32_t uncompressedLength, void* compressedBuffer, int32_t compressedCapacity);
            CRUCIBLE_API void CRUCIBLE_NATIVE_LZ4Decompress(void* compressedData, int32_t compressedLength, void* uncompressedBuffer, int32_t uncompressedLength);

            CRUCIBLE_API DeferredJobQueue* CRUCIBLE_NATIVE_DeferredJobQueueNew();
            CRUCIBLE_API void CRUCIBLE_NATIVE_DeferredJobQueueDelete(DeferredJobQueue* deferredJobQueue);
            CRUCIBLE_API void CRUCIBLE_NATIVE_DeferredJobQueueProcess(DeferredJobQueue* deferredJobQueue);

            CRUCIBLE_API void CRUCIBLE_NATIVE_TextureCreate2D(slag::Pixels::Format format, uint32_t width, uint32_t height, uint32_t mips, slag::Texture::SampleCount sampleCount, slag::Texture** outTexture);
            CRUCIBLE_API void CRUCIBLE_NATIVE_TextureDestroy(slag::Texture* texture);
            CRUCIBLE_API uint32_t CRUCIBLE_NATIVE_TextureGetWidth(slag::Texture* texture);
            CRUCIBLE_API uint32_t CRUCIBLE_NATIVE_TextureGetHeight(slag::Texture* texture);
            CRUCIBLE_API uint32_t CRUCIBLE_NATIVE_TextureGetDepth(slag::Texture* texture);
            CRUCIBLE_API uint32_t CRUCIBLE_NATIVE_TextureGetArraySize(slag::Texture* texture);
            CRUCIBLE_API uint32_t CRUCIBLE_NATIVE_TextureGetMipCount(slag::Texture* texture);
            CRUCIBLE_API slag::Pixels::Format CRUCIBLE_NATIVE_TextureGetFormat(slag::Texture* texture);
            CRUCIBLE_API slag::Texture::SampleCount CRUCIBLE_NATIVE_TextureGetSampleCount(slag::Texture* texture);

            CRUCIBLE_API void CRUCIBLE_NATIVE_Texture2DFromExchange(const char* filePath, uint32_t mipLevels, slag::Texture** out)noexcept(false);
            CRUCIBLE_API uint64_t CRUCIBLE_NATIVE_Texture2DFromEngineFormat(const unsigned char* data, slag::Texture** out);
            CRUCIBLE_API void CRUCIBLE_NATIVE_Texture2DToEngineFormat(const char* filepath, slag::Texture* texture);

            CRUCIBLE_API void CRUCIBLE_NATIVE_TransformToGlobal(crucible::Transform& transform, Node* node, Transform& out);
            CRUCIBLE_API void CRUCIBLE_NATIVE_TransformInverse(crucible::Transform& transform, Transform& out);
            CRUCIBLE_API void CRUCIBLE_NATIVE_TransformConcat(crucible::Transform& a, Transform& b, Transform& out);
            CRUCIBLE_API void CRUCIBLE_NATIVE_TransformDecat(crucible::Transform& a, Transform& b, Transform& out);


            CRUCIBLE_API int32_t CRUCIBLE_NATIVE_UUIDHash(boost::uuids::uuid& id);

            CRUCIBLE_API ecs_entity_t CRUCIBLE_NATIVE_EcsGetDataTypeID(const char* typeName, uint64_t size, uint64_t alignment);
            CRUCIBLE_API ecs_entity_t CRUCIBLE_NATIVE_EcsGetReferenceTypeID(const char* typeName);

            CRUCIBLE_API Node* CRUCIBLE_NATIVE_NodeNew();
            CRUCIBLE_API void CRUCIBLE_NATIVE_NodeDelete(Node* node);
            CRUCIBLE_API Node* CRUCIBLE_NATIVE_NodeReferenceFromUUID(boost::uuids::uuid& id);
            CRUCIBLE_API boost::uuids::uuid CRUCIBLE_NATIVE_NodeGetUUID(crucible::Node* node);
            CRUCIBLE_API int32_t CRUCIBLE_NATIVE_NodeGetNameLength(Node* node);
            CRUCIBLE_API void CRUCIBLE_NATIVE_NodeGetName(crucible::Node* node, char* nameArray);
            CRUCIBLE_API void CRUCIBLE_NATIVE_NodeSetName(crucible::Node* node, const char* name);
            CRUCIBLE_API Node* CRUCIBLE_NATIVE_NodeGetParent(crucible::Node* node);
            CRUCIBLE_API void CRUCIBLE_NATIVE_NodeSetParent(crucible::Node* node, crucible::Node* parent);
            CRUCIBLE_API uint32_t CRUCIBLE_NATIVE_NodeGetChildCount(crucible::Node* node);
            CRUCIBLE_API Node* CRUCIBLE_NATIVE_NodeGetChild(crucible::Node* node, uint32_t index);
            CRUCIBLE_API Node* CRUCIBLE_NATIVE_NodeAddChild(crucible::Node* node);
            CRUCIBLE_API void CRUCIBLE_NATIVE_NodeRemoveChildByIndex(crucible::Node* node, uint32_t index);
            CRUCIBLE_API void CRUCIBLE_NATIVE_NodeRemoveChildByValue(crucible::Node* node, crucible::Node* child);
            CRUCIBLE_API void CRUCIBLE_NATIVE_NodeAddDataComponent(crucible::Node* node,const char* typeName, uint64_t size, uint64_t alignment, void* data);
            CRUCIBLE_API void CRUCIBLE_NATIVE_NodeAddDataComponentFast(crucible::Node* node, ecs_entity_t typeId, void* data, uint64_t dataSize);
            CRUCIBLE_API void CRUCIBLE_NATIVE_NodeRemoveDataComponent(crucible::Node* node, const char* typeName, uint64_t size, uint64_t alignment);
            CRUCIBLE_API void CRUCIBLE_NATIVE_NodeRemoveDataComponentFast(crucible::Node*, ecs_entity_t typeId);
            CRUCIBLE_API bool CRUCIBLE_NATIVE_NodeHasDataComponent(crucible::Node* node, const char* typeName, uint64_t size, uint64_t alignment);
            CRUCIBLE_API bool CRUCIBLE_NATIVE_NodeHasDataComponentFast(crucible::Node* node, ecs_entity_t typeId);
            CRUCIBLE_API void* CRUCIBLE_NATIVE_NodeGetDataComponent(crucible::Node* node, const char* typeName, uint64_t size, uint64_t alignment);
            CRUCIBLE_API void* CRUCIBLE_NATIVE_NodeGetDataComponentFast(crucible::Node* node, ecs_entity_t typeId);
            CRUCIBLE_API void CRUCIBLE_NATIVE_NodeAddReferenceComponent(crucible::Node* node, const char* typeName, void* gcHandle);
            CRUCIBLE_API void CRUCIBLE_NATIVE_NodeAddReferenceComponentFast(crucible::Node* node, ecs_entity_t typeId, void* gcHandle);
            CRUCIBLE_API void CRUCIBLE_NATIVE_NodeRemoveReferenceComponent(crucible::Node* node, const char* typeName);
            CRUCIBLE_API void CRUCIBLE_NATIVE_NodeRemoveReferenceComponentFast(crucible::Node* node, ecs_entity_t typeId);
            CRUCIBLE_API bool CRUCIBLE_NATIVE_NodeHasReferenceComponent(crucible::Node* node, const char* typeName);
            CRUCIBLE_API bool CRUCIBLE_NATIVE_NodeHasReferenceComponentFast(crucible::Node* node, ecs_entity_t typeId);
            CRUCIBLE_API void* CRUCIBLE_NATIVE_NodeGetReferenceComponent(crucible::Node* node, const char* typeName);
            CRUCIBLE_API void* CRUCIBLE_NATIVE_NodeGetReferenceComponentFast(crucible::Node* node, ecs_entity_t typeId);
            CRUCIBLE_API void CRUCIBLE_NATIVE_NodeGetCumulativeTransform(crucible::Node* node, Transform& out);
            CRUCIBLE_API void CRUCIBLE_NATIVE_NodeLockFamily(crucible::Node* node);
            CRUCIBLE_API void CRUCIBLE_NATIVE_NodeUnlockFamily(crucible::Node* node);
            CRUCIBLE_API bool CRUCIBLE_NATIVE_NodeIsEnabled(crucible::Node* node);
            CRUCIBLE_API void CRUCIBLE_NATIVE_NodeEnable(crucible::Node* node, bool propagate);
            CRUCIBLE_API void CRUCIBLE_NATIVE_NodeDisable(crucible::Node* node, bool propagate);

            CRUCIBLE_API Mesh* CRUCIBLE_NATIVE_MeshNew(Mesh::MeshAttributeData* data, uint32_t vertexCount, void* indexData, slag::Buffer::IndexSize indexType, uint32_t indexCount, const Mesh::MeshBufferAccessibility& bufferAccessibility);
            CRUCIBLE_API Mesh* CRUCIBLE_NATIVE_MeshNewBatchedInit(Mesh::MeshAttributeData* data, uint32_t vertexCount, void* indexData, slag::Buffer::IndexSize indexType, uint32_t indexCount, const Mesh::MeshBufferAccessibility& bufferAccessibility, DeferredJobQueue* deferredQueue, void* IDeferredInitHandle);
            CRUCIBLE_API void CRUCIBLE_NATIVE_MeshDelete(Mesh* mesh);
            CRUCIBLE_API Mesh::VertexAttributeFlags CRUCIBLE_NATIVE_MeshDefinedVertexAttributeFlags(const Mesh* mesh);
            CRUCIBLE_API uint32_t CRUCIBLE_NATIVE_MeshVertexCount(const Mesh* mesh);
            CRUCIBLE_API uint32_t CRUCIBLE_NATIVE_MeshIndexCount(const Mesh* mesh);
            CRUCIBLE_API slag::Buffer::IndexSize CRUCIBLE_NATIVE_MeshIndexSize(const Mesh* mesh);
            CRUCIBLE_API void CRUCIBLE_NATIVE_MeshCopyPositionData(const Mesh* mesh, void* destination);
            CRUCIBLE_API void CRUCIBLE_NATIVE_MeshCopyNormalData(const Mesh* mesh, void* destination);
            CRUCIBLE_API void CRUCIBLE_NATIVE_MeshCopyTangentData(const Mesh* mesh, void* destination);
            CRUCIBLE_API void CRUCIBLE_NATIVE_MeshCopyColorData(const Mesh* mesh, void* destination);
            CRUCIBLE_API void CRUCIBLE_NATIVE_MeshCopyBoneWeightData(const Mesh* mesh, void* destination);
            CRUCIBLE_API void CRUCIBLE_NATIVE_MeshCopyUVData(const Mesh* mesh, void* destination);
            CRUCIBLE_API void CRUCIBLE_NATIVE_MeshCopyUV2Data(const Mesh* mesh, void* destination);
            CRUCIBLE_API void CRUCIBLE_NATIVE_MeshCopyUV3Data(const Mesh* mesh, void* destination);
            CRUCIBLE_API void CRUCIBLE_NATIVE_MeshCopyUV4Data(const Mesh* mesh, void* destination);
            CRUCIBLE_API void CRUCIBLE_NATIVE_MeshCopyIndexData(const Mesh* mesh, void* destination);
            CRUCIBLE_API slag::Buffer::Accessibility CRUCIBLE_NATIVE_MeshVertexAttributeBufferAccess(Mesh* mesh, Mesh::VertexAttribute attribute);
            CRUCIBLE_API slag::Buffer::Accessibility CRUCIBLE_NATIVE_MeshIndexAttributeBufferAccess(Mesh* mesh);
            CRUCIBLE_API void* CRUCIBLE_NATIVE_MeshPositionBufferCpuHandle(Mesh* mesh);
            CRUCIBLE_API void* CRUCIBLE_NATIVE_MeshNormalBufferCpuHandle(Mesh* mesh);
            CRUCIBLE_API void* CRUCIBLE_NATIVE_MeshTangentBufferCpuHandle(Mesh* mesh);
            CRUCIBLE_API void* CRUCIBLE_NATIVE_MeshColorBufferCpuHandle(Mesh* mesh);
            CRUCIBLE_API void* CRUCIBLE_NATIVE_MeshBoneWeightBufferCpuHandle(Mesh* mesh);
            CRUCIBLE_API void* CRUCIBLE_NATIVE_MeshUVBufferCpuHandle(Mesh* mesh);
            CRUCIBLE_API void* CRUCIBLE_NATIVE_MeshUV2BufferCpuHandle(Mesh* mesh);
            CRUCIBLE_API void* CRUCIBLE_NATIVE_MeshUV3BufferCpuHandle(Mesh* mesh);
            CRUCIBLE_API void* CRUCIBLE_NATIVE_MeshUV4BufferCpuHandle(Mesh* mesh);
            CRUCIBLE_API void* CRUCIBLE_NATIVE_MeshIndexBufferCpuHandle(Mesh* mesh);
        }
    } // scripting
} // slag

#endif //CRUCIBLE_API_H
