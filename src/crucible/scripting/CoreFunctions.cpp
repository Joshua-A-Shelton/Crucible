#include <boost/container_hash/hash.hpp>
#include "CoreFunctions.h"
#include <crucible/core/scenes/World.h>

namespace crucible
{
    namespace scripting
    {
//Node
        crucible::core::Node* cs_NodeReferenceFromUUID(boost::uuids::uuid& id)
        {
            return crucible::core::Node::getNodeByID(id);
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


    } // scripting
} // crucible