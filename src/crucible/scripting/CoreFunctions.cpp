#include <boost/container_hash/hash.hpp>
#include "CoreFunctions.h"

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

        crucible::core::Transform* cs_nodePointerGetTransform(core::Node* node)
        {
            return &(node->transform());
        }

//UUID
        int32_t cs_UUIDHash(boost::uuids::uuid& id)
        {
            static boost::hash<boost::uuids::uuid> hasher;
            return static_cast<int32_t>(hasher(id));
        }

//Transform

        void cs_TransformSetPosition(core::Transform& transform, glm::vec3& newPosition)
        {
            transform.setPosition(newPosition);
        }

        void cs_TransformTranslate(core::Transform& transform, glm::vec3& translation)
        {
            transform.translate(translation);
        }

        void cs_TransformSetRotation(core::Transform& transform, glm::quat& newRotation)
        {
            transform.setRotation(newRotation);
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

        void cs_TransformSetScale(core::Transform& transform, glm::vec3& newScale)
        {
            transform.setScale(newScale);
        }

        void cs_TransformScale(core::Transform& transform, glm::vec3& scale)
        {
            transform.scale(scale);
        }

        void cs_TransformConcatTransforms(core::Transform& t1,core::Transform& t2, core::Transform& out)
        {
            out = t1*t2;
        }

    } // scripting
} // crucible