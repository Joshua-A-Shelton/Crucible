#ifndef CRUCIBLE_INTEROP_H
#define CRUCIBLE_INTEROP_H
#include "../Crucible.h"
#include <glm/vec3.hpp>
#include <nethost/hostfxr.h>
#include <vector>
#include <string>
#include "ManagedObjectGCHandle.h"
#include "ManagedType.h"
#include "../Graphics/Mesh.h"

namespace crucible
{

    struct CRUCIBLE_EXPORT FunctionMapping
    {
        const char_t* CSharpClassName = nullptr;
        const char_t* CSharpDelegateName = nullptr;
        void** NativeFunctionPointer = nullptr;
    };

    struct CRUCIBLE_EXPORT ManagedFunctionPointers
    {
        void* (*registerUnmanagedFunction)(FunctionMapping&) = nullptr;
        void* (*freeUnmanagedGCHandle)(void*) = nullptr;
        void* (*getFunctionPointer)(ManagedType&, const char* FunctionName, void**)= nullptr;
        void* (*getType)(const char* assemblyQualifiedName, ManagedType& type)= nullptr;
        void* (*loadLibrary)(const char* contextName,const char* path)=nullptr;
        void* (*unloadLibrary)(const char* path)= nullptr;
        void* (*unloadAllContexts)()= nullptr;
    };

    struct CRUCIBLE_EXPORT Interop
    {
        static inline ManagedFunctionPointers managedFunctionPointers;
    };


    extern "C"
    {

    //Matrix
    CRUCIBLE_EXPORT void cs_MatrixMatrixMultiply(glm::mat4& m1, glm::mat4& m2, glm::mat4& result);
    CRUCIBLE_EXPORT void cs_MatrixFromTRS(glm::vec3& translation, glm::quat& rotation, glm::vec3& scale, glm::mat4& result);

    //Meshes
    CRUCIBLE_EXPORT Mesh* cs_CreateMesh();
    CRUCIBLE_EXPORT void cs_DeleteMesh(Mesh* mesh);

    //Textures
    CRUCIBLE_EXPORT slag::Texture* cs_CreateColorTexture(int width, int height, glm::vec4 defaultColor, int mipLevels = 1, bool renderTargetable = false);
    CRUCIBLE_EXPORT slag::Texture* cs_CreateDepthTexture(int width, int height);
    CRUCIBLE_EXPORT void cs_DeleteTexture(slag::Texture* texture);
    CRUCIBLE_EXPORT int cs_TextureWidth(slag::Texture* texture);
    CRUCIBLE_EXPORT int cs_TextureHeight(slag::Texture* texture);


    }
}





#endif //CRUCIBLE_INTEROP_H
