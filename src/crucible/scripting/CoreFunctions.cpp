#include <boost/container_hash/hash.hpp>
#include "CoreFunctions.h"
#include <crucible/core/scenes/World.h>

#include "crucible/core/ModelLoader.h"
#include <stb_image.h>

namespace crucible
{
    namespace scripting
    {
//World
        void cs_gameManagerGetRootNode(crucible::core::Node** node)
        {
            *node = crucible::core::World::RootNode;
        }
//Node
        crucible::core::Node* cs_NodeReferenceFromUUID(boost::uuids::uuid& id)
        {
            return crucible::core::Node::getNodeByID(id);
        }

        int32_t cs_nodePointerGetNameLength(core::Node* node)
        {
            return node->name().length()+1;
        }

        void cs_nodePointerGetName(core::Node* node, char* out, int32_t outLength)
        {
            if (outLength > node->name().length()+1)
            {
                outLength = node->name().length()+1;
            }
            memcpy(out,node->name().c_str(),outLength);
        }

        void cs_nodePointerSetName(core::Node* node, const char* name)
        {
            node->setName(name);
        }

        void cs_NodePointerLockFamily(core::Node* node)
        {
            node->lockFamily();
        }

        void cs_nodePointerUnlockFamily(core::Node* node)
        {
            node->unlockFamily();
        }

        boost::uuids::uuid cs_nodePointerUUID(core::Node* node)
        {
            return node->uuid();
        }

        core::Node* cs_nodePointerParent(core::Node* node)
        {
            return node->getParent();
        }

        void cs_nodePointerSetParent(core::Node* node, core::Node* newParent)
        {
            node->setParent(newParent);
        }

        int32_t cs_nodePointerChildCount(core::Node* node)
        {
            return node->childrenCount();
        }

        core::Node* cs_nodePointerGetChild(core::Node* node, int32_t index)
        {
            return node->getChild(index);
        }

        core::Node* cs_nodePointerAddChild(core::Node* node)
        {
            return node->addChild();
        }

        void cs_nodePointerRemoveChildByIndex(core::Node* node, int32_t index)
        {
            node->killChild(index);
        }

        void cs_nodePointerAddDataComponent(core::Node* node,const char* typeName, uint64_t size, uint64_t alignment, void* data)
        {
            auto componentType = core::World::RegisterOrRetrieveType(typeName,size,alignment);
            ecs_add_id(core::World::ECS.world_,node->entity(),componentType);
            ecs_set_id(core::World::ECS.world_,node->entity(),componentType,size,data);
        }

        void cs_nodePointerRemoveDataComponent(core::Node* node,const char* typeName, uint64_t size, uint64_t alignment)
        {
            auto componentType = core::World::RegisterOrRetrieveType(typeName,size,alignment);
            ecs_remove_id(core::World::ECS.world_,node->entity(),componentType);
        }

        void* cs_nodePointerGetDataComponent(crucible::core::Node* node,const char* typeName, uint64_t size, uint64_t alignment)
        {
            auto componentType = core::World::RegisterOrRetrieveType(typeName,size,alignment);
            return (void*)node->entity().get(componentType);
        }

        void cs_nodePointerAddReferenceComponent(crucible::core::Node* node,const char* typeName, crucible::scripting::ManagedType managedType, void* gcHandle)
        {
            auto componentType = core::World::RegisterOrRetrieveScriptingType(typeName);
            ecs_add_id(core::World::ECS.world_,node->entity(),componentType);
            auto inst = (scripting::ManagedInstance*)node->entity().get(componentType);
            *inst = ManagedInstance(managedType,gcHandle);

        }

        void cs_nodePointerRemoveReferenceComponent(crucible::core::Node* node,const char* typeName)
        {
            auto componentType = core::World::RegisterOrRetrieveScriptingType(typeName);
            ecs_remove_id(core::World::ECS.world_,node->entity(),componentType);
        }

        void* cs_nodePointerGetReferenceComponent(crucible::core::Node* node,const char* typeName)
        {
            auto componentType = core::World::RegisterOrRetrieveScriptingType(typeName);
            return ((scripting::ManagedInstance*)node->entity().get(componentType))->gcHandle();
        }

        void cs_nodePointerCumulativeTransform(crucible::core::Node* node, crucible::core::Transform& transform)
        {
            transform = core::Transform::cumulativeFrom(node);
        }

//UUID
        int32_t cs_UUIDHash(boost::uuids::uuid& id)
        {
            static boost::hash<boost::uuids::uuid> hasher;
            return static_cast<int32_t>(hasher(id));
        }

//Quaternion
        void cs_quaternionFromAngleAxis(glm::quat& quaternion, float angle,  glm::vec3& axis)
        {
            quaternion = glm::angleAxis(angle, axis);
        }

//Transform

        void cs_TransformTranslate(core::Transform& transform, glm::vec3& translation)
        {
            transform.translate(translation);
        }

        void cs_TransformRotate(core::Transform& transform, glm::quat& rotation)
        {
            transform.rotate(rotation);
        }

        void cs_TransformSetRotationEuler(core::Transform& transform, glm::vec3& newRotation)
        {
            transform.setRotation(newRotation);
        }

        void cs_TransformRotateEuler(core::Transform& transform, glm::vec3& rotation)
        {
            transform.rotate(rotation);
        }

        void cs_TransformRotateAxisAngle(core::Transform& transform, float angle, glm::vec3 & axis)
        {
            transform.rotate(angle,axis);
        }

        void cs_TransformScale(core::Transform& transform, glm::vec3& scale)
        {
            transform.scale(scale);
        }

        void cs_TransformConcatTransforms(core::Transform& transform1,core::Transform& transform2, core::Transform& out)
        {
            out = transform1+transform2;
        }

        void cs_TransformDecatTransforms(crucible::core::Transform& transform1, crucible::core::Transform& transform2, crucible::core::Transform& out)
        {
            out = transform1-transform2;
        }

        void cs_TransformToGlobal(crucible::core::Transform& transform, core::Node* relativeTo, crucible::core::Transform& out)
        {
            out = transform.toGlobal(relativeTo);
        }

        void cs_TransformInverse(crucible::core::Transform& transform, crucible::core::Transform& out)
        {
            out = transform.inverse();
        }

//Camera
        void cs_cameraSetNearPlane(crucible::core::Camera& camera, float nearPlane)
        {
            camera.nearPlane(nearPlane);
        }

        void cs_cameraSetFarPlane(crucible::core::Camera& camera, float farPlane)
        {
            camera.farPlane(farPlane);
        }

        void cs_cameraSetFOV(crucible::core::Camera& camera, float fov)
        {
            camera.fov(fov);
        }

        void cs_cameraSetWidth(crucible::core::Camera& camera, float width)
        {
            camera.width(width);
        }

        void cs_cameraSetHeight(crucible::core::Camera& camera, float height)
        {
            camera.height(height);
        }

        void cs_cameraSetPerspective(crucible::core::Camera& camera, bool isPerspective)
        {
            camera.isPerspective(isPerspective);
        }
//Mesh
        void cs_meshInstanceInitializeSerialized(crucible::core::Mesh** mesh, unsigned char* data, uint64_t length)
        {
            *mesh = new core::Mesh(data, length);
        }

        void cs_meshInstanceFromFile(core::Mesh** mesh, const char* file)
        {
            core::ModelLoader loader(file);
            if (loader.meshCount()!=1)
            {
                return;
            }
            *mesh = loader.nextMesh();
        }

        void cs_meshInstanceCleanup(crucible::core::Mesh* mesh)
        {
            delete mesh;
        }

//Texture
        void cs_textureInitFromPath(slag::Texture** texture, const char* file, slag::Pixels::Format format, uint32_t mipLevels)
        {
            int x,y,channels;
            auto data = stbi_load(file, &x, &y, &channels, 4);

            auto commandBuffer = slag::CommandBuffer::newCommandBuffer(slag::GpuQueue::GRAPHICS);
            *texture = slag::Texture::newTexture(format,slag::Texture::TEXTURE_2D,x,y,mipLevels,1,1,slag::TextureUsageFlags::SAMPLED_IMAGE);
            slag::Buffer* tempBuffer = slag::Buffer::newBuffer(data,x*y*4*sizeof(char),slag::Buffer::CPU_AND_GPU,slag::Buffer::DATA_BUFFER);

            commandBuffer->begin();
            commandBuffer->insertBarrier(slag::ImageBarrier
                {
                    .texture = *texture,
                    .baseLayer = 0,
                    .layerCount = 1,
                    .baseMipLevel = 0,
                    .mipCount = mipLevels,
                    .oldLayout = slag::Texture::Layout::UNDEFINED,
                    .newLayout = slag::Texture::TRANSFER_DESTINATION,
                    .accessBefore = slag::BarrierAccessFlags::NONE,
                    .accessAfter = slag::BarrierAccessFlags::TRANSFER_WRITE,
                    .syncBefore = slag::PipelineStageFlags::NONE,
                    .syncAfter = slag::PipelineStageFlags::TRANSFER,
                });
            commandBuffer->copyBufferToImage(tempBuffer,0,*texture,slag::Texture::TRANSFER_DESTINATION,0,0);
            if (mipLevels > 1)
            {
                commandBuffer->updateMipChain
                (
                    *texture,
                    0,
                    slag::Texture::Layout::TRANSFER_DESTINATION,
                    slag::Texture::SHADER_RESOURCE,
                    slag::Texture::TRANSFER_DESTINATION,
                    slag::Texture::SHADER_RESOURCE,
                    slag::PipelineStageFlags::TRANSFER,
                    slag::PipelineStageFlags::ALL_COMMANDS
                );
            }
            else
            {
                commandBuffer->insertBarrier(slag::ImageBarrier
                {
                    .texture = *texture,
                    .baseLayer = 0,
                    .layerCount = 1,
                    .baseMipLevel = 0,
                    .mipCount = mipLevels,
                    .oldLayout = slag::Texture::Layout::TRANSFER_DESTINATION,
                    .newLayout = slag::Texture::SHADER_RESOURCE,
                    .accessBefore = slag::BarrierAccessFlags::TRANSFER_WRITE,
                    .accessAfter = slag::BarrierAccessFlags::TRANSFER_READ,
                    .syncBefore = slag::PipelineStageFlags::TRANSFER,
                    .syncAfter = slag::PipelineStageFlags::TRANSFER,
                });
            }

            commandBuffer->end();
            slag::SlagLib::graphicsCard()->graphicsQueue()->submit(commandBuffer);

            stbi_image_free(data);
            commandBuffer->waitUntilFinished();
            delete commandBuffer;
            delete tempBuffer;
        }

        void cs_textureInitFromRaw(slag::Texture** texture, unsigned char* data, slag::Pixels::Format format, uint32_t width,uint32_t height,uint32_t mipLevels)
        {
            auto pixelSize = slag::Pixels::pixelBytes(format);
            auto commandBuffer = slag::CommandBuffer::newCommandBuffer(slag::GpuQueue::GRAPHICS);
            *texture = slag::Texture::newTexture(format,slag::Texture::TEXTURE_2D,width,height,mipLevels,1,1,slag::TextureUsageFlags::SAMPLED_IMAGE);
            slag::Buffer* tempBuffer = slag::Buffer::newBuffer(data,width*height*pixelSize,slag::Buffer::CPU_AND_GPU,slag::Buffer::DATA_BUFFER);

            commandBuffer->begin();
            commandBuffer->insertBarrier(slag::ImageBarrier
                {
                    .texture = *texture,
                    .oldLayout = slag::Texture::Layout::UNDEFINED,
                    .newLayout = slag::Texture::TRANSFER_DESTINATION,
                    .accessBefore = slag::BarrierAccessFlags::NONE,
                    .accessAfter = slag::BarrierAccessFlags::TRANSFER_WRITE,
                    .syncBefore = slag::PipelineStageFlags::NONE,
                    .syncAfter = slag::PipelineStageFlags::TRANSFER,
                });
            commandBuffer->copyBufferToImage(tempBuffer,0,*texture,slag::Texture::TRANSFER_DESTINATION,0,0);
            commandBuffer->updateMipChain
            (
                *texture,
                0,
                slag::Texture::Layout::TRANSFER_DESTINATION,
                slag::Texture::SHADER_RESOURCE,
                slag::Texture::TRANSFER_DESTINATION,
                slag::Texture::SHADER_RESOURCE,
                slag::PipelineStageFlags::TRANSFER,
                slag::PipelineStageFlags::ALL_COMMANDS
            );
            commandBuffer->end();
            slag::SlagLib::graphicsCard()->graphicsQueue()->submit(commandBuffer);

            stbi_image_free(data);
            commandBuffer->waitUntilFinished();
            delete commandBuffer;
            delete tempBuffer;
        }

        void cs_textureCleanResources(slag::Texture* texture)
        {
            delete texture;
        }

        slag::Pixels::Format cs_texturePixelFormat(slag::Texture* texture)
        {
            return  texture->format();
        }

        slag::Texture::Type cs_textureType(slag::Texture* texture)
        {
            return  texture->type();
        }

        uint32_t cs_textureWidth(slag::Texture* texture)
        {
            return texture->width();
        }

        uint32_t cs_textureHeight(slag::Texture* texture)
        {
            return texture->height();
        }

        uint32_t cs_textureDepth(slag::Texture* texture)
        {
            return texture->layers();
        }

        uint32_t cs_textureMipMapLevels(slag::Texture* texture)
        {
            return texture->mipLevels();
        }
//Materials
        void cs_materialInitialize(core::Material** material, const char* shaderName)
        {
            *material = new core::Material(shaderName);
        }

        void cs_materialCleanup(core::Material* material)
        {
            delete material;
        }

        bool cs_materialHasUniform(core::Material* material, const char* uniformName)
        {
            return material->hasUniform(uniformName);
        }

        bool cs_materialHasTexture(core::Material* material, const char* textureName)
        {
            return material->hasTexture(textureName);
        }

        void cs_materialSetUniform(core::Material* material, const char* uniformName, unsigned char* data, uint32_t length)
        {
            material->setData(material->offsetOfBuffer(uniformName), data, length);
        }

        void cs_materialSetTexture(core::Material* material, const char* textureName, slag::Texture* texture)
        {
            material->setTexture(textureName, texture);
        }

        uint32_t cs_materialGetUniformCount(core::Material* material)
        {
            return material->uniformCount();
        }

        uint32_t cs_materialGetTextureCount(core::Material* material)
        {
            return material->textureCount();
        }

        int32_t cs_materialGetUniformNameSize(core::Material* material, uint32_t index)
        {
            return material->uniformBufferName(index).size();
        }

        int32_t cs_materialGetTextureNameSize(core::Material* material, uint32_t index)
        {
            return material->textureName(index).size();
        }

        void cs_materialGetUniformNamePtr(core::Material* material, uint32_t index,char* name, int32_t length)
        {
            auto cppName = material->uniformBufferName(index);
            memcpy(name, cppName.c_str(), length);
        }

        void cs_materialGetTextureNamePtr(core::Material* material, uint32_t index, char* name, int32_t length)
        {
            auto cppName = material->textureName(index);
            memcpy(name, cppName.c_str(), length);
        }

    } // scripting
} // crucible