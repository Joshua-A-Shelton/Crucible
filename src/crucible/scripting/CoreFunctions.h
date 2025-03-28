#ifndef CRUCIBLE_COREFUNCTIONS_H
#define CRUCIBLE_COREFUNCTIONS_H
#include "ScriptingEngine.h"
#include <glm/glm.hpp>
#include <boost/uuid/uuid.hpp>
#include "crucible/core/Transform.h"
#include "crucible/core/scenes/Node.h"
#include "crucible/core/Camera.h"
#include "crucible/core/Material.h"
#include <slag/SlagLib.h>
//DEFINITION(C# Class, function return type, C# delegate name, C++ function name, C++ parameters)
#define CRUCIBLE_CORE_DELEGATES \
DELEGATE(Crucible.Core.GameManager, void, _gameManagerGetRootNode_ptr, cs_gameManagerGetRootNode,crucible::core::Node**)\
\
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
\
DELEGATE(Crucible.Core.Camera, void, _cameraSetNearPlane_ptr, cs_cameraSetNearPlane, crucible::core::Camera& camera, float nearPlane)\
DELEGATE(Crucible.Core.Camera, void, _cameraSetFarPlane_ptr, cs_cameraSetFarPlane, crucible::core::Camera& camera, float farPlane)\
DELEGATE(Crucible.Core.Camera, void, _cameraSetFOV_ptr, cs_cameraSetFOV, crucible::core::Camera& camera, float fov)\
DELEGATE(Crucible.Core.Camera, void, _cameraSetWidth_ptr, cs_cameraSetWidth, crucible::core::Camera& camera, float width)\
DELEGATE(Crucible.Core.Camera, void, _cameraSetHeight_ptr, cs_cameraSetHeight, crucible::core::Camera& camera, float height)\
DELEGATE(Crucible.Core.Camera, void, _cameraSetPerspective_ptr, cs_cameraSetPerspective, crucible::core::Camera& camera, bool isPerspective)\
\
DELEGATE(Crucible.Core.MeshInstance, void, _meshInstanceInitializeSerialized_ptr, cs_meshInstanceInitializeSerialized, core::Mesh**, unsigned char*, uint64_t)\
DELEGATE(Crucible.Core.MeshInstance, void, _meshInstanceFromFile_ptr, cs_meshInstanceFromFile, core::Mesh**, const char*)\
DELEGATE(Crucible.Core.MeshInstance, void, _meshInstanceCleanup_ptr, cs_meshInstanceCleanup, core::Mesh*)\
\
DELEGATE(Crucible.Core.Texture, void, _textureInitFromPath_ptr, cs_textureInitFromPath, slag::Texture**, const char*, slag::Pixels::Format, uint32_t)\
DELEGATE(Crucible.Core.Texture, void, _textureInitFromRaw_ptr, cs_textureInitFromRaw, slag::Texture**, unsigned char*, slag::Pixels::Format, uint32_t,uint32_t,uint32_t)\
DELEGATE(Crucible.Core.Texture, void, _textureCleanResources_ptr, cs_textureCleanResources, slag::Texture*)\
DELEGATE(Crucible.Core.Texture, slag::Pixels::Format, _texturePixelFormat_ptr, cs_texturePixelFormat, slag::Texture*)\
DELEGATE(Crucible.Core.Texture, slag::Texture::Type, _textureType_ptr, cs_textureType, slag::Texture*)\
DELEGATE(Crucible.Core.Texture, uint32_t, _textureWidth_ptr, cs_textureWidth, slag::Texture*)\
DELEGATE(Crucible.Core.Texture, uint32_t, _textureHeight_ptr, cs_textureHeight, slag::Texture*)\
DELEGATE(Crucible.Core.Texture, uint32_t, _textureDepth_ptr, cs_textureDepth, slag::Texture*)\
DELEGATE(Crucible.Core.Texture, uint32_t, _textureMipMapLevels_ptr, cs_textureMipMapLevels, slag::Texture*)\
\
DELEGATE(Crucible.Core.Material, void, _materialInitialize_ptr, cs_materialInitialize, core::Material**, const char*)\
DELEGATE(Crucible.Core.Material, void, _materialCleanup_ptr, cs_materialCleanup, core::Material*)\
DELEGATE(Crucible.Core.Material, bool, _materialHasUniform_ptr, cs_materialHasUniform, core::Material*, const char*)\
DELEGATE(Crucible.Core.Material, bool, _materialHasTexture_ptr, cs_materialHasTexture, core::Material*, const char*)\
DELEGATE(Crucible.Core.Material, void, _materialSetUniform_ptr, cs_materialSetUniform, core::Material*, const char*, unsigned char*, uint32_t)\
DELEGATE(Crucible.Core.Material, void, _materialSetTexture_ptr, cs_materialSetTexture, core::Material*, const char*, slag::Texture*)\
DELEGATE(Crucible.Core.Material, uint32_t, _materialGetUniformCount_ptr, cs_materialGetUniformCount, core::Material*)\
DELEGATE(Crucible.Core.Material, uint32_t, _materialGetTextureCount_ptr, cs_materialGetTextureCount, core::Material*)\
DELEGATE(Crucible.Core.Material, int32_t, _materialGetUniformNameSize_ptr, cs_materialGetUniformNameSize, core::Material*, uint32_t)\
DELEGATE(Crucible.Core.Material, int32_t, _materialGetTextureNameSize_ptr, cs_materialGetTextureNameSize, core::Material*, uint32_t)\
DELEGATE(Crucible.Core.Material, void, _materialGetUniformNamePtr_ptr, cs_materialGetUniformNamePtr, core::Material*, uint32_t,char*, int32_t)\
DELEGATE(Crucible.Core.Material, void, _materialGetTextureNamePtr_ptr, cs_materialGetTextureNamePtr, core::Material*, uint32_t,char*, int32_t)\



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
