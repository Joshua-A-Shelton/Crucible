#ifndef CRUCIBLE_COREFUNCTIONS_H
#define CRUCIBLE_COREFUNCTIONS_H
#include "ScriptingEngine.h"
#include <glm/glm.hpp>
#include <boost/uuid/uuid.hpp>
#include "crucible/core/Transform.h"
#include "crucible/core/scenes/Node.h"
//DEFINITION(C# Class, function return type, C# delegate name, C++ function name, C++ parameters)
#define CRUCIBLE_CORE_DELEGATES \
DELEGATE(Crucible.Core.NodeReference, crucible::core::Node*, _nodeReferenceFromUUID_ptr, cs_NodeReferenceFromUUID,boost::uuids::uuid& id)\
DELEGATE(Crucible.Core.NodePointer, void, _nodePointerLockFamily, cs_NodePointerLockFamily,crucible::core::Node*)\
DELEGATE(Crucible.Core.NodePointer, void, _nodePointerUnlockFamily, cs_nodePointerUnlockFamily,crucible::core::Node*)\
DELEGATE(Crucible.Core.NodePointer, boost::uuids::uuid, _nodePointerUUID_ptr, cs_nodePointerUUID,crucible::core::Node*)\
DELEGATE(Crucible.Core.NodePointer, crucible::core::Node*, _nodePointerParent_ptr, cs_nodePointerParent,crucible::core::Node*)\
DELEGATE(Crucible.Core.NodePointer, void, _nodePointerSetParent_ptr, cs_nodePointerSetParent,crucible::core::Node*, crucible::core::Node*)\
DELEGATE(Crucible.Core.NodePointer, int32_t, _nodePointerChildCount_ptr, cs_nodePointerChildCount,crucible::core::Node*)\
DELEGATE(Crucible.Core.NodePointer, crucible::core::Node*, _nodePointerGetChild_ptr, cs_nodePointerGetChild,crucible::core::Node*, int32_t)\
DELEGATE(Crucible.Core.NodePointer, crucible::core::Node*, _nodePointerAddChild_ptr, cs_nodePointerAddChild,crucible::core::Node*)\
DELEGATE(Crucible.Core.NodePointer, void, _nodePointerRemoveChildByIndex_ptr, cs_nodePointerRemoveChildByIndex,crucible::core::Node*, int32_t index)\
                                \
DELEGATE(Crucible.Core.UUID, int32_t, _uuidHash_ptr, cs_UUIDHash, boost::uuids::uuid& id)\
                                \
DELEGATE(Crucible.Core.Matrix4x4, void, _matrixMultiply_ptr, cs_Matrix4x4Multiply, glm::mat4& m1, glm::mat4& m2, glm::mat4& result)\
                                \
DELEGATE(Crucible.Core.Transform, void, _transformUpdateMatrix_ptr, cs_TransformUpdateMatrix, crucible::core::Transform& transform)\



namespace crucible
{
    namespace scripting
    {

        extern "C"
        {
#define DELEGATE(className, returnValue, delegateName, functionName, ...) returnValue functionName(__VA_ARGS__);
        CRUCIBLE_CORE_DELEGATES
#undef DELEGATE
        }
        class CoreFunctionsInitializer
        {
        public:
            CoreFunctionsInitializer()
            {
#define DELEGATE(className, returnValue, delegateName, functionName, ...) ScriptingEngine::registerUnmanagedFunction(#className,#delegateName,(void**)functionName);
                CRUCIBLE_CORE_DELEGATES
#undef DELEGATE
            }
        };

    } // scripting
} // crucible

#endif //CRUCIBLE_COREFUNCTIONS_H
