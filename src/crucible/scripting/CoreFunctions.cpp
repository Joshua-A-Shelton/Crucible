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

        void cs_nodePointerRemoveChildByValue(core::Node* node, core::Node* child)
        {
            node->killChildByReference(child);
        }
//Matrix

        void cs_Matrix4x4Multiply(glm::mat4& m1, glm::mat4& m2, glm::mat4& result)
        {
            result = m1 * m2;
        }

//Transform

        void cs_TransformUpdateMatrix(core::Transform& transform)
        {
            transform.updateMatrix();
        }

    } // scripting
} // crucible