#ifndef CRUCIBLE_MESH_H
#define CRUCIBLE_MESH_H
#include <slag/Slag.h>
#include <vector>
#include <glm/glm.hpp>
namespace crucible
{
    namespace rendering
    {
        enum class MeshLoadResult
        {
            SUCCESS,
            FAILURE,
        };
        enum class VertexAttributeFlags
        {
            NONE = 0,
            POSITION = 1,
            NORMAL = 1<<1,
            UV = 1<<2,
            TANGENT = 1<<3,
            COLOR = 1<<4,
            BONE_WEIGHTS = 1<<5,
        };

        inline VertexAttributeFlags operator|(VertexAttributeFlags lhs, VertexAttributeFlags rhs)
        {
            return static_cast<VertexAttributeFlags>(static_cast<int>(lhs) | static_cast<int>(rhs));
        }
        inline VertexAttributeFlags operator&(VertexAttributeFlags lhs, VertexAttributeFlags rhs)
        {
            return static_cast<VertexAttributeFlags>(static_cast<int>(lhs) & static_cast<int>(rhs));
        }
        inline VertexAttributeFlags operator~(VertexAttributeFlags flags)
        {
            return static_cast<VertexAttributeFlags>(~static_cast<int>(flags));
        }
        inline VertexAttributeFlags operator^(VertexAttributeFlags lhs, VertexAttributeFlags rhs)
        {
            return static_cast<VertexAttributeFlags>(static_cast<int>(lhs) ^ static_cast<int>(rhs));
        }
        inline VertexAttributeFlags& operator|=(VertexAttributeFlags& lhs, VertexAttributeFlags rhs)
        {
            lhs = lhs | rhs;
            return lhs;
        }
        inline VertexAttributeFlags& operator&=(VertexAttributeFlags& lhs, VertexAttributeFlags rhs)
        {
            lhs = lhs & rhs;
            return lhs;
        }
        inline VertexAttributeFlags& operator^=(VertexAttributeFlags& lhs, VertexAttributeFlags rhs)
        {
            lhs = lhs ^ rhs;
            return lhs;
        }



        struct BoneWeights
        {
            float weights[4];
            uint16_t boneIndices[4];
        };
        struct VertexDataStreams
        {
            glm::vec3* positions = nullptr;
            glm::vec3* normals = nullptr;
            glm::vec2* uvs = nullptr;
            glm::vec4* tangents = nullptr;
            glm::vec4* colors = nullptr;
            BoneWeights* boneWeights = nullptr;
            void* indices = nullptr;
            uint32_t vertexCount = 0;
            slag::IndexBufferType indexBufferType = slag::IndexBufferType::UINT_32;
        };

        class Mesh
        {
        public:
            Mesh(const VertexDataStreams& vertexStreams, VertexAttributeFlags cpuAccessibleAttributes);
            ~Mesh();
            Mesh(const Mesh& from) = delete;
            Mesh& operator=(const Mesh& from) = delete;
            Mesh(Mesh&& from);
            Mesh& operator=(Mesh&& from);

            uint32_t vertexCount() const;
        private:
            void move(Mesh& from);

            slag::Buffer* _positionBuffer=nullptr;
            slag::Buffer* _normalBuffer=nullptr;
            slag::Buffer* _uvBuffer=nullptr;
            slag::Buffer* _tangentBuffer=nullptr;
            slag::Buffer* _colorBuffer=nullptr;
            slag::Buffer* _boneWeightBuffer=nullptr;

            slag::Buffer* _indexBuffer=nullptr;
            slag::IndexBufferType indexBufferType = slag::IndexBufferType::UINT_32;
        };
    } // rendering
} // crucible

#endif //CRUCIBLE_MESH_H