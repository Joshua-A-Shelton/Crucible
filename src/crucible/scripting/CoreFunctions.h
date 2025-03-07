#ifndef CRUCIBLE_COREFUNCTIONS_H
#define CRUCIBLE_COREFUNCTIONS_H
#include "ScriptingEngine.h"
#include <glm/glm.hpp>
#include <boost/uuid/uuid.hpp>
#include "crucible/core/Transform.h"
#include "crucible/core/scenes/Node.h"
#include "crucible/scripting/InteropString.h"
//DEFINITION(C# Class, function return type, C# delegate name, C++ function name, C++ parameters)
#define CRUCIBLE_CORE_DELEGATES \
DELEGATE(Crucible.Core.NodeReference, crucible::core::Node*, _nodeReferenceFromUUID_ptr, cs_NodeReferenceFromUUID,boost::uuids::uuid& id)\
DELEGATE(Crucible.Core.NodePointer, void, _nodePointerLockFamily_ptr, cs_NodePointerLockFamily,crucible::core::Node*)\
DELEGATE(Crucible.Core.NodePointer, void, _nodePointerUnlockFamily_ptr, cs_nodePointerUnlockFamily,crucible::core::Node*)\
DELEGATE(Crucible.Core.NodePointer, boost::uuids::uuid, _nodePointerUUID_ptr, cs_nodePointerUUID,crucible::core::Node*)\
DELEGATE(Crucible.Core.NodePointer, int32_t, _nodePointerGetNameLength, cs_nodePointerGetNameLength,crucible::core::Node*)\
DELEGATE(Crucible.Core.NodePointer, void, _nodePointerGetName, cs_nodePointerGetName,crucible::core::Node*, char*, int32_t)\
DELEGATE(Crucible.Core.NodePointer, void, _nodePointerSetName, cs_nodePointerSetName,crucible::core::Node*, const char*)\
DELEGATE(Crucible.Core.NodePointer, crucible::core::Node*, _nodePointerParent_ptr, cs_nodePointerParent,crucible::core::Node*)\
DELEGATE(Crucible.Core.NodePointer, void, _nodePointerSetParent_ptr, cs_nodePointerSetParent,crucible::core::Node*, crucible::core::Node*)\
DELEGATE(Crucible.Core.NodePointer, int32_t, _nodePointerChildCount_ptr, cs_nodePointerChildCount,crucible::core::Node*)\
DELEGATE(Crucible.Core.NodePointer, crucible::core::Node*, _nodePointerGetChild_ptr, cs_nodePointerGetChild,crucible::core::Node*, int32_t)\
DELEGATE(Crucible.Core.NodePointer, crucible::core::Node*, _nodePointerAddChild_ptr, cs_nodePointerAddChild,crucible::core::Node*)\
DELEGATE(Crucible.Core.NodePointer, void, _nodePointerRemoveChildByIndex_ptr, cs_nodePointerRemoveChildByIndex,crucible::core::Node*, int32_t index) \
DELEGATE(Crucible.Core.NodePointer, void, _nodePointerAddDataComponent_ptr, cs_nodePointerAddDataComponent,crucible::core::Node*,const char*, uint64_t, uint64_t, void*) \
DELEGATE(Crucible.Core.NodePointer, void, _nodePointerRemoveDataComponent_ptr, cs_nodePointerRemoveDataComponent,crucible::core::Node*,const char*, uint64_t, uint64_t)  \
DELEGATE(Crucible.Core.NodePointer, void*, _nodePointerGetDataComponent_ptr, cs_nodePointerGetDataComponent,crucible::core::Node*,const char*, uint64_t, uint64_t)       \
DELEGATE(Crucible.Core.NodePointer, void, _nodePointerAddReferenceComponent_ptr, cs_nodePointerAddReferenceComponent,crucible::core::Node*,const char*, crucible::scripting::ManagedType, void*) \
DELEGATE(Crucible.Core.NodePointer, void, _nodePointerRemoveReferenceComponent_ptr, cs_nodePointerRemoveReferenceComponent,crucible::core::Node*,const char*)  \
DELEGATE(Crucible.Core.NodePointer, void*, _nodePointerGetReferenceComponent_ptr, cs_nodePointerGetReferenceComponent,crucible::core::Node*,const char*)\
DELEGATE(Crucible.Core.NodePointer, void, _nodePointerGetCumulativeTransform_ptr, cs_nodePointerCumulativeTransform,crucible::core::Node*, crucible::core::Transform&)\
\
DELEGATE(Crucible.Core.UUID, int32_t, _uuidHash_ptr, cs_UUIDHash, boost::uuids::uuid& id)\
\
DELEGATE(Crucible.Core.Quaternion, void, _quaternionFromAngleAxis_ptr, cs_quaternionFromAngleAxis, glm::quat& quaternion, float angle, glm::vec3& axis)\
\
DELEGATE(Crucible.Core.Transform, void, _transformTranslate_ptr, cs_TransformTranslate, crucible::core::Transform& transform, glm::vec3& translation)\
DELEGATE(Crucible.Core.Transform, void, _transformRotate_ptr, cs_TransformRotate, crucible::core::Transform& transform, glm::quat& rotation)\
DELEGATE(Crucible.Core.Transform, void, _transformSetRotationEuler_ptr, cs_TransformSetRotationEuler, crucible::core::Transform& transform, glm::vec3& newRotation)\
DELEGATE(Crucible.Core.Transform, void, _transformRotateEuler_ptr, cs_TransformRotateEuler, crucible::core::Transform& transform, glm::vec3& rotation)    \
DELEGATE(Crucible.Core.Transform, void, _transformRotateAxisAngle_ptr, cs_TransformRotateAxisAngle, crucible::core::Transform& transform, float, glm::vec3& rotation) \
DELEGATE(Crucible.Core.Transform, void, _transformScale_ptr, cs_TransformScale, crucible::core::Transform& transform, glm::vec3& scale)\
DELEGATE(Crucible.Core.Transform, void, _transformConcatTransforms_ptr, cs_TransformConcatTransforms, crucible::core::Transform& transform1, crucible::core::Transform& transform2, crucible::core::Transform& out)\
DELEGATE(Crucible.Core.Transform, void, _transformDecatTransforms_ptr, cs_TransformDecatTransforms, crucible::core::Transform& transform1, crucible::core::Transform& transform2, crucible::core::Transform& out)\
DELEGATE(Crucible.Core.Transform, void, _transformToGlobal_ptr, cs_TransformToGlobal, crucible::core::Transform& transform1, crucible::core::Node* relativeTo, crucible::core::Transform& out)\
DELEGATE(Crucible.Core.Transform, void, _transformInverse_ptr, cs_TransformInverse, crucible::core::Transform& transform, crucible::core::Transform& out)\



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
