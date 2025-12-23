#include "API.h"

#include <vector>
#include <glm/gtc/quaternion.hpp>
#include <crucible/Game.h>
#include <fstream>
#include <crucible/ecs/World.h>
#include <slag/Slag.h>
#include <lz4.h>
#include <crucible/utils/GPUUtils.h>
#include <boost/container_hash/hash.hpp>
#include "ManagedInstance.h"
#include "crucible/DeferredJobQueue.h"
#include <crucible/ShaderManager.h>

#define STB_IMAGE_IMPLEMENTATION
#include <stb_image.h>

namespace crucible
{
    namespace scripting
    {

        void CRUCIBLE_NATIVE_Matrix4X4Multiply(const glm::mat4& a, const glm::mat4& b, glm::mat4& out)
        {
            out = a*b;
        }

        void CRUCIBLE_NATIVE_Matrix4X4FromPositionRotationScale(const glm::vec3& position, const glm::quat& rotation, const glm::vec3& scale, glm::mat4& out)
        {
            auto translationMatrix = glm::translate(glm::mat4(1.0f), position);
            auto rotationMatrix = glm::mat4(rotation);
            auto scaleMatrix = glm::scale(glm::mat4(1.0f), scale);
            out = translationMatrix*rotationMatrix*scaleMatrix;
        }

        void CRUCIBLE_NATIVE_QuaternionFromAngleAxis(float angle, const glm::vec3& axis, glm::quat& out)
        {
            out = glm::quat(angle,axis);
        }

        void CRUCIBLE_NATIVE_QuaternionFromEulerAngles(const glm::vec3& eulerAngles, glm::quat& out)
        {
            out = glm::quat(eulerAngles);
        }

        void CRUCIBLE_NATIVE_QuaternionMultiply(const glm::quat& a, const glm::quat& b, glm::quat& out)
        {
            out = a*b;
        }

        void CRUCIBLE_NATIVE_QuaternionInverse(const glm::quat& of, glm::quat& out)
        {
            out = glm::inverse(of);
        }

        float CRUCIBLE_NATIVE_QuaternionMagnitude(const glm::quat& quat)
        {
            return glm::length(quat);
        }

        void CRUCIBLE_NATIVE_QuaternionNormalized(const glm::quat& quat, glm::quat& out)
        {
            out = glm::normalize(quat);
        }


        void CRUCIBLE_NATIVE_Vector4MatrixMultipy(const glm::vec4& vector, const glm::mat4& matrix, glm::vec4& out)
        {
            out = vector*matrix;
        }

        float CRUCIBLE_NATIVE_Vector4DotProduct(const glm::vec4& a, const glm::vec4& b)
        {
            return glm::dot(a,b);
        }

        float CRUCIBLE_NATIVE_Vector4Magnitude(const glm::vec4& vector)
        {
            return glm::length(vector);
        }

        void CRUCIBLE_NATIVE_Vector4Normalized(const glm::vec4& vector, glm::vec4& out)
        {
            out = glm::normalize(vector);
        }

        void CRUCIBLE_NATIVE_Vector3MatrixMultipy(const glm::vec3& vector, const glm::mat4& matrix, glm::vec3& out)
        {
            glm::vec4 vec(vector,1);
            out = vec * matrix;
        }

        float CRUCIBLE_NATIVE_Vector3DotProduct(const glm::vec3& a, const glm::vec3& b)
        {
            return glm::dot(a,b);
        }

        void CRUCIBLE_NATIVE_Vector3CrossProduct(const glm::vec3& a,const glm::vec3&b, glm::vec3& out)
        {
            out = glm::cross(a,b);
        }

        float CRUCIBLE_NATIVE_Vector3Magnitude(const glm::vec3& vector)
        {
            return glm::length(vector);
        }

        void CRUCIBLE_NATIVE_Vector3Normalized(const glm::vec3& vector, glm::vec3& out)
        {
            out = glm::normalize(vector);
        }

        float CRUCIBLE_NATIVE_Vector2DotProduct(const glm::vec2& a, const glm::vec2& b)
        {
            return glm::dot(a,b);
        }

        float CRUCIBLE_NATIVE_Vector2Magnitude(const glm::vec2& vector)
        {
            return glm::length(vector);
        }

        void CRUCIBLE_NATIVE_Vector2Normalized(const glm::vec2& vector, glm::vec2& out)
        {
            out = glm::normalize(vector);
        }

        int32_t CRUCIBLE_NATIVE_LZ4MaxCompressedSize(int32_t uncompressedSize)
        {
            return LZ4_compressBound(uncompressedSize);
        }

        int32_t CRUCIBLE_NATIVE_LZ4Compress(void* uncompressedData, int32_t uncompressedLength, void* compressedBuffer, int32_t compressedCapacity)
        {
            auto compressed = LZ4_compress_default((char*)uncompressedData, (char*)compressedBuffer, uncompressedLength, compressedCapacity);
            if (compressed == 0)
            {
                memcpy(compressedBuffer, uncompressedData, uncompressedLength);
                return uncompressedLength;
            }
            return compressed;
        }

        void CRUCIBLE_NATIVE_LZ4Decompress(void* compressedData, int32_t compressedLength, void* uncompressedBuffer, int32_t uncompressedLength)
        {
            if (compressedLength == uncompressedLength)
            {
                memcpy(uncompressedBuffer, compressedData, compressedLength);
            }
            else
            {
                LZ4_decompress_safe((char*)compressedData,(char*)uncompressedBuffer,compressedLength,uncompressedLength);
            }
        }

        DeferredJobQueue* CRUCIBLE_NATIVE_DeferredJobQueueNew()
        {
            return new DeferredJobQueue();
        }

        void CRUCIBLE_NATIVE_DeferredJobQueueDelete(DeferredJobQueue* deferredJobQueue)
        {
            delete deferredJobQueue;
        }

        void CRUCIBLE_NATIVE_DeferredJobQueueProcess(DeferredJobQueue* deferredJobQueue)
        {
            deferredJobQueue->process();
        }

        void CRUCIBLE_NATIVE_DeferredJobQueueAddDeferredInit(DeferredJobQueue* deferredJobQueue, void* IDeferredInitHandle)
        {
            deferredJobQueue->enqueue(DeferredJob(ManagedInstance(IDeferredInitHandle)));
        }

        slag::Texture* CRUCIBLE_NATIVE_TextureCreate2D(slag::Pixels::Format format, uint32_t width, uint32_t height,uint32_t mips, slag::Texture::SampleCount sampleCount)
        {
            auto formatProperties = slag::Pixels::formatProperties(format);
            slag::Texture::UsageFlags usage = formatProperties.validUsageFlags;
            return slag::Texture::newTexture(format,slag::Texture::Type::TEXTURE_2D,usage,width,height,1,mips,1,sampleCount);
        }

        slag::Texture* CRUCIBLE_NATIVE_TextureLoadExchange(const unsigned char* fileBytesBuffer,int32_t bufferLength, uint32_t mipLevels, int autoFillMips)
        {
            int width,height,channels;
            auto pixels = stbi_load_from_memory(fileBytesBuffer,bufferLength,&width,&height,&channels,4);
            if (pixels == nullptr)
            {
                return nullptr;
            }
            slag::TextureBufferMapping mapping
            {
                .bufferOffset = 0,
                .textureSubresource =
                {
                    .aspectFlags = slag::Pixels::AspectFlags::COLOR,
                    .mipLevel = 0,
                    .baseArrayLayer = 0,
                    .layerCount = 1,
                },
                .textureOffset = {0,0,0},
                .textureExtent = {(uint32_t)width,(uint32_t)height,1}

            };
            auto texture = slag::Texture::newTexture(slag::Pixels::Format::R8G8B8A8_UNORM,slag::Texture::Type::TEXTURE_2D,slag::Texture::UsageFlags::SAMPLED_IMAGE,width,height,1,mipLevels,1,slag::Texture::SampleCount::ONE,pixels,sizeof(uint8_t)*width*height*4,&mapping,1);
            stbi_image_free(pixels);
            if (autoFillMips && mipLevels > 1)
            {
                auto commandBuffer = slag::CommandBuffer::newCommandBuffer(slag::GPUQueue::QueueType::GRAPHICS);
                auto finished = slag::Semaphore::newSemaphore(0);
                commandBuffer->begin();
                for (auto i=1; i<mipLevels; i++)
                {
                    commandBuffer->updateMip(texture,0,0,i);
                }
                commandBuffer->end();
                slag::SemaphoreValue signal{.semaphore = finished,.value = 1};
                slag::QueueSubmissionBatch batch
                {
                    .waitSemaphores = nullptr,
                    .waitSemaphoreCount = 0,
                    .commandBuffers = &commandBuffer,
                    .commandBufferCount = 1,
                    .signalSemaphores = &signal,
                    .signalSemaphoreCount = 1,
                };
                slag::slagGraphicsCard()->graphicsQueue()->submit(&batch,1);
                finished->waitForValue(1);
                delete finished;
                delete commandBuffer;
            }
            return texture;
        }

        void CRUCIBLE_NATIVE_TextureDestroy(slag::Texture* texture)
        {
            auto instance = Game::instance();
            if (instance)
            {
                instance->queueForDeletion(texture);
            }
            else
            {
                delete texture;
            }
        }

        uint32_t CRUCIBLE_NATIVE_TextureGetWidth(slag::Texture* texture)
        {
            return texture->width();
        }

        uint32_t CRUCIBLE_NATIVE_TextureGetMipWidth(slag::Texture* texture, uint32_t mip)
        {
            return texture->width(mip);
        }

        uint32_t CRUCIBLE_NATIVE_TextureGetHeight(slag::Texture* texture)
        {
            return texture->height();
        }

        uint32_t CRUCIBLE_NATIVE_TextureGetMipHeight(slag::Texture* texture, uint32_t mip)
        {
            return texture->height(mip);
        }

        uint32_t CRUCIBLE_NATIVE_TextureGetDepth(slag::Texture* texture)
        {
            return texture->depth();
        }

        uint32_t CRUCIBLE_NATIVE_TextureGetMipDepth(slag::Texture* texture, uint32_t mip)
        {
            return texture->depth(mip);
        }

        uint32_t CRUCIBLE_NATIVE_TextureGetArraySize(slag::Texture* texture)
        {
            return texture->layers();
        }

        uint32_t CRUCIBLE_NATIVE_TextureGetMipCount(slag::Texture* texture)
        {
            return texture->mipLevels();
        }

        uint64_t CRUCIBLE_NATIVE_TextureGetByteSize(slag::Texture* texture)
        {
            return texture->byteSize();
        }

        uint32_t CRUCIBLE_NATIVE_TextureGetPixelSize(slag::Pixels::Format format, slag::Pixels::AspectFlags aspectFlags)
        {
            return slag::Pixels::size(format,aspectFlags);
        }


        uint64_t CRUCIBLE_NATIVE_TextureGetMipByteSize(slag::Texture* texture, uint32_t mip)
        {
            return texture->byteSize(mip);
        }

        slag::Pixels::Format CRUCIBLE_NATIVE_TextureGetFormat(slag::Texture* texture)
        {
            return texture->format();
        }

        slag::Pixels::AspectFlags CRUCIBLE_NATIVE_TextureGetAspectFlags(slag::Pixels::Format format)
        {
            return slag::Pixels::aspectFlags(format);
        }

        slag::Texture::SampleCount CRUCIBLE_NATIVE_TextureGetSampleCount(slag::Texture* texture)
        {
            return texture->sampleCount();
        }

        void CRUCIBLE_NATIVE_TextureSetPixelsDeferred(slag::Texture* texture, void* data, uint64_t dataLength, slag::TextureBufferMapping* mappings, uint32_t mappingCount, DeferredJobQueue* deferredQueue, void* IDeferredInitHandle)
        {
            auto dataBuffer = slag::Buffer::newBuffer(data,dataLength,slag::Buffer::Accessibility::CPU_AND_GPU);
            auto commandBuffer = deferredQueue->commandBuffer();
            commandBuffer->copyBufferToTexture(dataBuffer,texture,mappings,mappingCount);
            if (IDeferredInitHandle)
            {
                deferredQueue->enqueue(DeferredJob(std::vector<slag::Buffer*>{dataBuffer},ManagedInstance(IDeferredInitHandle)));
            }
            else
            {
                deferredQueue->enqueue(DeferredJob(std::vector<slag::Buffer*>{dataBuffer}));
            }

        }

        void CRUCIBLE_NATIVE_TextureGetPixels(slag::Texture* texture, void* outBuffer, uint64_t outBufferLength, slag::TextureBufferMapping* mappings, uint32_t mappingCount)
        {
            auto commandBuffer = slag::CommandBuffer::newCommandBuffer(slag::GPUQueue::QueueType::TRANSFER);
            auto finished = slag::Semaphore::newSemaphore(0);
            auto dataBuffer = slag::Buffer::newBuffer(outBuffer,outBufferLength,slag::Buffer::Accessibility::CPU_AND_GPU);
            commandBuffer->begin();
            commandBuffer->copyTextureToBuffer(texture,dataBuffer,mappings,mappingCount);
            commandBuffer->end();
            slag::SemaphoreValue signal{.semaphore = finished,.value = 1};
            slag::QueueSubmissionBatch batch
            {
                .waitSemaphores = nullptr,
                .waitSemaphoreCount = 0,
                .commandBuffers = &commandBuffer,
                .commandBufferCount = 1,
                .signalSemaphores = &signal,
                .signalSemaphoreCount = 1,
            };
            slag::slagGraphicsCard()->transferQueue()->submit(&batch,1);
            finished->waitForValue(1);
            memcpy(outBuffer,dataBuffer->cpuHandle(),outBufferLength);
            delete commandBuffer;
            delete finished;
            delete dataBuffer;
        }


        void CRUCIBLE_NATIVE_TransformToGlobal(crucible::Transform& transform, Node* node, Transform& out)
        {
            out = transform.toGlobal(node);
        }

        void CRUCIBLE_NATIVE_TransformInverse(crucible::Transform& transform, Transform& out)
        {
            out = transform.inverse();
        }

        void CRUCIBLE_NATIVE_TransformConcat(crucible::Transform& a, Transform& b, Transform& out)
        {
            out = a + b;
        }

        void CRUCIBLE_NATIVE_TransformDecat(crucible::Transform& a, Transform& b, Transform& out)
        {
            out = a - b;
        }

        int32_t CRUCIBLE_NATIVE_UUIDHash(boost::uuids::uuid& id)
        {
            static boost::hash<boost::uuids::uuid> hasher;
            return static_cast<int32_t>(hasher(id));
        }


        ecs_entity_t CRUCIBLE_NATIVE_EcsGetDataTypeID(const char* typeName, uint64_t size, uint64_t alignment)
        {
            return ecs::registerOrRetrieveDataType(typeName,size,alignment);
        }

        ecs_entity_t CRUCIBLE_NATIVE_EcsGetReferenceTypeID(const char* typeName)
        {
            return ecs::registerOrRetrieveReferenceType(typeName);
        }

        Node* CRUCIBLE_NATIVE_NodeNew()
        {
            return new Node();
        }

        void CRUCIBLE_NATIVE_NodeDelete(Node* node)
        {
            delete node;
        }

        Node* CRUCIBLE_NATIVE_NodeReferenceFromUUID(boost::uuids::uuid& id)
        {
            return Node::getNodeByID(id);
        }

        boost::uuids::uuid CRUCIBLE_NATIVE_NodeGetUUID(crucible::Node* node)
        {
            return node->uuid();
        }

        int32_t CRUCIBLE_NATIVE_NodeGetNameLength(Node* node)
        {
            return node->name().length();
        }

        void CRUCIBLE_NATIVE_NodeGetName(crucible::Node* node, char* nameArray)
        {
            memcpy(nameArray,node->name().c_str(),node->name().size());
        }

        void CRUCIBLE_NATIVE_NodeSetName(crucible::Node* node, const char* name)
        {
            node->setName(name);
        }

        Node* CRUCIBLE_NATIVE_NodeGetParent(crucible::Node* node)
        {
            return node->getParent();
        }

        void CRUCIBLE_NATIVE_NodeSetParent(crucible::Node* node, crucible::Node* parent)
        {
            node->setParent(parent);
        }

        uint32_t CRUCIBLE_NATIVE_NodeGetChildCount(crucible::Node* node)
        {
            return node->childrenCount();
        }

        Node* CRUCIBLE_NATIVE_NodeGetChild(crucible::Node* node, uint32_t index)
        {
            return node->getChild(index);
        }

        Node* CRUCIBLE_NATIVE_NodeAddChild(crucible::Node* node)
        {
            return node->addChild();
        }

        void CRUCIBLE_NATIVE_NodeRemoveChildByIndex(crucible::Node* node, uint32_t index)
        {
            auto instance = Game::instance();
            if (instance)
            {
                instance->queueForDeletion(node->removeChild(index));
            }
            else
            {
                node->killChild(index);
            }
        }

        void CRUCIBLE_NATIVE_NodeRemoveChildByValue(crucible::Node* node, crucible::Node* child)
        {
            auto instance = Game::instance();
            if (instance)
            {
                instance->queueForDeletion(node->removeChildByReference(child));
            }
            else
            {
                node->killChildByReference(child);
            }
        }

        void CRUCIBLE_NATIVE_NodeAddDataComponent(crucible::Node* node, const char* typeName, uint64_t size, uint64_t alignment, void* data)
        {
            auto typeId = ecs::registerOrRetrieveDataType(typeName,size,alignment);
            CRUCIBLE_NATIVE_NodeAddDataComponentFast(node,typeId,data,size);
        }

        void CRUCIBLE_NATIVE_NodeAddDataComponentFast(crucible::Node* node, ecs_entity_t typeId, void* data, uint64_t dataSize)
        {
            auto entity = node->entity();
            auto world = ecs::world();
            ecs_add_id(world.world_,entity,typeId);
            ecs_set_id(world.world_,entity,typeId,dataSize,data);
        }


        void CRUCIBLE_NATIVE_NodeRemoveDataComponent(crucible::Node* node, const char* typeName, uint64_t size,uint64_t alignment)
        {
            auto componentType = ecs::registerOrRetrieveDataType(typeName,size,alignment);
            CRUCIBLE_NATIVE_NodeRemoveDataComponentFast(node,componentType);
        }

        void CRUCIBLE_NATIVE_NodeRemoveDataComponentFast(crucible::Node* node, ecs_entity_t typeId)
        {
            ecs_remove_id(ecs::world().world_,node->entity(),typeId);
        }

        void* CRUCIBLE_NATIVE_NodeGetDataComponent(crucible::Node* node, const char* typeName, uint64_t size,uint64_t alignment)
        {
            auto componentType = ecs::registerOrRetrieveDataType(typeName,size,alignment);
            return CRUCIBLE_NATIVE_NodeGetDataComponentFast(node,componentType);
        }

        void* CRUCIBLE_NATIVE_NodeGetDataComponentFast(crucible::Node* node, ecs_entity_t typeId)
        {
            return (void*)node->entity().get(typeId);
        }

        bool CRUCIBLE_NATIVE_NodeHasDataComponent(crucible::Node* node, const char* typeName, uint64_t size, uint64_t alignment)
        {
            auto componentType = ecs::registerOrRetrieveDataType(typeName,size,alignment);
            return CRUCIBLE_NATIVE_NodeHasDataComponentFast(node,componentType);
        }

        bool CRUCIBLE_NATIVE_NodeHasDataComponentFast(crucible::Node* node, ecs_entity_t typeId)
        {
            return node->entity().has(typeId);
        }

        void CRUCIBLE_NATIVE_NodeAddReferenceComponent(crucible::Node* node, const char* typeName, void* gcHandle)
        {
            auto componentType = ecs::registerOrRetrieveReferenceType(typeName);
            CRUCIBLE_NATIVE_NodeAddReferenceComponentFast(node,componentType,gcHandle);
        }

        void CRUCIBLE_NATIVE_NodeAddReferenceComponentFast(crucible::Node* node, ecs_entity_t typeId, void* gcHandle)
        {
            ecs_add_id(ecs::world().world_,node->entity(),typeId);
            auto inst = (scripting::ManagedInstance*)node->entity().get(typeId);
            *inst = ManagedInstance(gcHandle);
        }

        void CRUCIBLE_NATIVE_NodeRemoveReferenceComponent(crucible::Node* node, const char* typeName)
        {
            auto componentType = ecs::registerOrRetrieveReferenceType(typeName);
            CRUCIBLE_NATIVE_NodeRemoveReferenceComponentFast(node,componentType);
        }

        void CRUCIBLE_NATIVE_NodeRemoveReferenceComponentFast(crucible::Node* node, ecs_entity_t typeId)
        {
            ecs_remove_id(ecs::world().world_,node->entity(),typeId);
        }

        bool CRUCIBLE_NATIVE_NodeHasReferenceComponent(crucible::Node* node, const char* typeName)
        {
            auto componentType = ecs::registerOrRetrieveReferenceType(typeName);
            return CRUCIBLE_NATIVE_NodeHasReferenceComponentFast(node,componentType);
        }

        bool CRUCIBLE_NATIVE_NodeHasReferenceComponentFast(crucible::Node* node, ecs_entity_t typeId)
        {
            return node->entity().has(typeId);
        }

        void* CRUCIBLE_NATIVE_NodeGetReferenceComponent(crucible::Node* node, const char* typeName)
        {
            auto componentType = ecs::registerOrRetrieveReferenceType(typeName);
            return CRUCIBLE_NATIVE_NodeGetReferenceComponentFast(node,componentType);
        }

        void* CRUCIBLE_NATIVE_NodeGetReferenceComponentFast(crucible::Node* node, ecs_entity_t typeId)
        {
            return ((scripting::ManagedInstance*)node->entity().get(typeId))->gcHandle();
        }

        void CRUCIBLE_NATIVE_NodeGetCumulativeTransform(crucible::Node* node, Transform& out)
        {
            out = Transform::cumulativeFrom(node);
        }

        bool CRUCIBLE_NATIVE_NodeIsEnabled(crucible::Node* node)
        {
            return node->isEnabled();
        }

        void CRUCIBLE_NATIVE_NodeEnable(crucible::Node* node, bool propagate)
        {
            node->enable(propagate);
        }

        void CRUCIBLE_NATIVE_NodeDisable(crucible::Node* node, bool propagate)
        {
            node->disable(propagate);
        }

        Mesh* CRUCIBLE_NATIVE_MeshNew(Mesh::MeshAttributeData* data, void* indexData,slag::Buffer::IndexSize indexType, uint32_t indexCount,const Mesh::MeshBufferAccessibility& bufferAccessibility)
        {
            auto m = new Mesh(*data,indexData,(indexType==slag::Buffer::IndexSize::UINT16? sizeof(uint16_t) : sizeof(uint32_t))*indexCount,indexType,bufferAccessibility);
            return  m;
        }

        Mesh* CRUCIBLE_NATIVE_MeshNewBatchedInit(Mesh::MeshAttributeData* data, void* indexData,slag::Buffer::IndexSize indexType, uint32_t indexCount,const Mesh::MeshBufferAccessibility& bufferAccessibility, DeferredJobQueue* deferredQueue, void* IDeferredInitHandle)
        {
            auto mesh = new Mesh(data->definedAttributes(),data->vertexCount,indexType,indexCount,bufferAccessibility);
            auto commandBuffer = deferredQueue->commandBuffer();
            auto bufferData = data->toBuffers();
            auto& attributes = std::get<std::vector<Mesh::VertexAttribute>>(bufferData);
            auto& buffers = std::get<std::vector<slag::Buffer*>>(bufferData);
            for (auto i = 0u; i < attributes.size(); i++)
            {
                auto attribute =attributes[i];
                auto source = buffers[i];
                auto destination = mesh->attributeBuffer(attribute);
                commandBuffer->copyBufferToBuffer(source,0,destination->size(),destination,0);
            }
            slag::Buffer* indexBuffer = slag::Buffer::newBuffer(indexData,mesh->indexBuffer()->size(),slag::Buffer::Accessibility::CPU_AND_GPU);
            buffers.emplace_back(indexBuffer);
            commandBuffer->copyBufferToBuffer(indexBuffer,0,indexBuffer->size(),mesh->indexBuffer(),0);
            if (IDeferredInitHandle)
            {
                deferredQueue->enqueue(DeferredJob(std::move(buffers),ManagedInstance(IDeferredInitHandle)));
            }
            else
            {
                deferredQueue->enqueue(DeferredJob(std::move(buffers)));
            }
            return mesh;

        }

        void CRUCIBLE_NATIVE_MeshDelete(Mesh* mesh)
        {
            delete mesh;
        }

        Mesh::VertexAttributeFlags CRUCIBLE_NATIVE_MeshDefinedVertexAttributeFlags(const Mesh* mesh)
        {
            return mesh->definedAttributes();
        }

        uint32_t CRUCIBLE_NATIVE_MeshVertexCount(const Mesh* mesh)
        {
            return mesh->vertexCount();
        }

        uint32_t CRUCIBLE_NATIVE_MeshIndexCount(const Mesh* mesh)
        {
            return mesh->indexCount();
        }

        slag::Buffer::IndexSize CRUCIBLE_NATIVE_MeshIndexSize(const Mesh* mesh)
        {
            return mesh->indexSize();
        }

        void CRUCIBLE_NATIVE_MeshCopyPositionData(const Mesh* mesh, void* destination)
        {
            auto buffer = mesh->positionBuffer();
            if (buffer)
            {
                utils::copyBufferToArray(buffer,destination);
            }
        }

        void CRUCIBLE_NATIVE_MeshCopyNormalData(const Mesh* mesh, void* destination)
        {
            auto buffer = mesh->normalBuffer();
            if (buffer)
            {
                utils::copyBufferToArray(buffer,destination);
            }
        }

        void CRUCIBLE_NATIVE_MeshCopyTangentData(const Mesh* mesh, void* destination)
        {
            auto buffer = mesh->tangentBuffer();
            if (buffer)
            {
                utils::copyBufferToArray(buffer,destination);
            }
        }

        void CRUCIBLE_NATIVE_MeshCopyColorData(const Mesh* mesh, void* destination)
        {
            auto buffer = mesh->colorBuffer();
            if (buffer)
            {
                utils::copyBufferToArray(buffer,destination);
            }
        }

        void CRUCIBLE_NATIVE_MeshCopyBoneWeightData(const Mesh* mesh, void* destination)
        {
            auto buffer = mesh->boneWeightBuffer();
            if (buffer)
            {
                utils::copyBufferToArray(buffer,destination);
            }
        }

        void CRUCIBLE_NATIVE_MeshCopyUVData(const Mesh* mesh, void* destination)
        {
            auto buffer = mesh->uvBuffer();
            if (buffer)
            {
                utils::copyBufferToArray(buffer,destination);
            }
        }

        void CRUCIBLE_NATIVE_MeshCopyUV2Data(const Mesh* mesh, void* destination)
        {
            auto buffer = mesh->uvBuffer2();
            if (buffer)
            {
                utils::copyBufferToArray(buffer,destination);
            }
        }

        void CRUCIBLE_NATIVE_MeshCopyUV3Data(const Mesh* mesh, void* destination)
        {
            auto buffer = mesh->uvBuffer3();
            if (buffer)
            {
                utils::copyBufferToArray(buffer,destination);
            }
        }

        void CRUCIBLE_NATIVE_MeshCopyUV4Data(const Mesh* mesh, void* destination)
        {
            auto buffer = mesh->uvBuffer4();
            if (buffer)
            {
                utils::copyBufferToArray(buffer,destination);
            }
        }


        void CRUCIBLE_NATIVE_MeshCopyIndexData(const Mesh* mesh, void* destination)
        {
            auto buffer = mesh->indexBuffer();
            if (buffer)
            {
                utils::copyBufferToArray(buffer,destination);
            }
        }

        slag::Buffer::Accessibility CRUCIBLE_NATIVE_MeshVertexAttributeBufferAccess(Mesh* mesh,Mesh::VertexAttribute attribute)
        {
            auto buffer =  mesh->attributeBuffer(attribute);
            if (buffer)
            {
                return buffer->accessibility();
            }
            return slag::Buffer::Accessibility::GPU;
        }

        slag::Buffer::Accessibility CRUCIBLE_NATIVE_MeshIndexAttributeBufferAccess(Mesh* mesh)
        {
            return mesh->indexBuffer()->accessibility();
        }

        void* CRUCIBLE_NATIVE_MeshPositionBufferCpuHandle(Mesh* mesh)
        {
            if (mesh->positionBuffer()->accessibility() == slag::Buffer::Accessibility::CPU_AND_GPU)
            {
                return mesh->positionBuffer()->cpuHandle();
            }
            return nullptr;
        }

        void* CRUCIBLE_NATIVE_MeshNormalBufferCpuHandle(Mesh* mesh)
        {
            if (mesh->normalBuffer()->accessibility() == slag::Buffer::Accessibility::CPU_AND_GPU)
            {
                return mesh->normalBuffer()->cpuHandle();
            }
            return nullptr;
        }

        void* CRUCIBLE_NATIVE_MeshTangentBufferCpuHandle(Mesh* mesh)
        {
            if (mesh->tangentBuffer()->accessibility() == slag::Buffer::Accessibility::CPU_AND_GPU)
            {
                return mesh->tangentBuffer()->cpuHandle();
            }
            return nullptr;
        }

        void* CRUCIBLE_NATIVE_MeshColorBufferCpuHandle(Mesh* mesh)
        {
            if (mesh->colorBuffer()->accessibility() == slag::Buffer::Accessibility::CPU_AND_GPU)
            {
                return mesh->colorBuffer()->cpuHandle();
            }
            return nullptr;
        }

        void* CRUCIBLE_NATIVE_MeshBoneWeightBufferCpuHandle(Mesh* mesh)
        {
            if (mesh->boneWeightBuffer()->accessibility() == slag::Buffer::Accessibility::CPU_AND_GPU)
            {
                return mesh->boneWeightBuffer()->cpuHandle();
            }
            return nullptr;
        }

        void* CRUCIBLE_NATIVE_MeshUVBufferCpuHandle(Mesh* mesh)
        {
            if (mesh->uvBuffer()->accessibility() == slag::Buffer::Accessibility::CPU_AND_GPU)
            {
                return mesh->uvBuffer()->cpuHandle();
            }
            return nullptr;
        }

        void* CRUCIBLE_NATIVE_MeshUV2BufferCpuHandle(Mesh* mesh)
        {
            if (mesh->uvBuffer2()->accessibility() == slag::Buffer::Accessibility::CPU_AND_GPU)
            {
                return mesh->uvBuffer2()->cpuHandle();
            }
            return nullptr;
        }

        void* CRUCIBLE_NATIVE_MeshUV3BufferCpuHandle(Mesh* mesh)
        {
            if (mesh->uvBuffer3()->accessibility() == slag::Buffer::Accessibility::CPU_AND_GPU)
            {
                return mesh->uvBuffer3()->cpuHandle();
            }
            return nullptr;
        }

        void* CRUCIBLE_NATIVE_MeshUV4BufferCpuHandle(Mesh* mesh)
        {
            if (mesh->uvBuffer4()->accessibility() == slag::Buffer::Accessibility::CPU_AND_GPU)
            {
                return mesh->uvBuffer4()->cpuHandle();
            }
            return nullptr;
        }

        void* CRUCIBLE_NATIVE_MeshIndexBufferCpuHandle(Mesh* mesh)
        {
            if (mesh->indexBuffer()->accessibility() == slag::Buffer::Accessibility::CPU_AND_GPU)
            {
                return mesh->indexBuffer()->cpuHandle();
            }
            return nullptr;
        }

        void CRUCIBLE_NATIVE_ShaderReferenceDelete(ShaderReference* shaderReference)
        {
            delete shaderReference;
        }


        ShaderReference* CRUCIBLE_NATIVE_ShaderPipelineGraphicsGetOrLoad(const char* name,
            void(* createShader)(const char* shaderPath, const char* shaderName, ShaderManager::ShaderCreateResult(*
            nativeAddShader)(const char* name, Mesh::VertexAttributeFlags attributes, unsigned char** shaderCodeArray,
            uint32_t* shaderCodeLengthsArray, slag::ShaderStageFlags* stages, uint32_t shaderCount,slag::ShaderProperties* properties, slag::FrameBufferDescription* framebufferDescription)))
        {
            return new ShaderReference(ShaderManager::getShader(name,createShader));
        }
    } // scripting
} // slag
