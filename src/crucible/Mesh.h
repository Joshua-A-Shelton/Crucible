#ifndef CRUCIBLE_MESH_H
#define CRUCIBLE_MESH_H
#include <slag/Slag.h>

#include <glm/vec3.hpp>
#include <glm/vec4.hpp>
#include <glm/vec2.hpp>

#include "BoneWeights.h"


namespace crucible
{
    class DeferredJobQueue;
    /**
     * Holds the vertex and index data required for drawing on the GPU
     */
    class Mesh
    {
     public:
        ///What kind of data is represented
        enum class VertexAttribute: uint16_t
        {
            ///X, Y, and Z position (floats)
            POSITION = 1,
            ///Normal vector of vertex (X, Y, and Z floats)
            NORMAL = 1 << 1,
            ///Tangent vector of vertex (X,Y, and X floats)
            TANGENT = 1 << 2,
            ///R, G, B, A values (byte)
            COLOR = 1 << 3,
            ///(Bone ID (UINT_32), Bone weight (float))(x4)
            BONE_WEIGHT = (1 << 4),
            ///Primary UV texture coordinates (U and V floats)
            UV = (1 << 5),
            ///Second set of UV texture coordinates (U and V floats)
            UV2 = (1 << 6),
            ///Third set of UV texture coordinates (U and V floats)
            UV3 = (1 << 7),
            ///Fourth set of UV texture coordinates (U and V floats)
            UV4 = (1 << 8),
        };
        enum class VertexAttributeFlags: uint16_t
        {};
    private:
        static constexpr uint8_t ATTRIBUTE_COUNT = 13;
    public:

        struct MeshBufferAccessibility
        {
        public:
            slag::Buffer::Accessibility
            index = slag::Buffer::Accessibility::GPU,
            position = slag::Buffer::Accessibility::GPU,
            normal = slag::Buffer::Accessibility::GPU,
            tangent = slag::Buffer::Accessibility::GPU,
            color = slag::Buffer::Accessibility::GPU,
            boneWeight = slag::Buffer::Accessibility::GPU,
            uv = slag::Buffer::Accessibility::GPU,
            uv2 = slag::Buffer::Accessibility::GPU,
            uv3 = slag::Buffer::Accessibility::GPU,
            uv4 = slag::Buffer::Accessibility::GPU;
            slag::Buffer::Accessibility& operator[](crucible::Mesh::VertexAttribute attribute);
            slag::Buffer::Accessibility operator[](crucible::Mesh::VertexAttribute attribute)const;
        };

        struct MeshAttributeData
        {
        public:
            glm::vec3* positionData=nullptr;
            glm::vec3* normalData=nullptr;
            glm::vec3* tangentData=nullptr;
            glm::vec4* colorData=nullptr;
            BoneWeights* boneWeightData=nullptr;
            glm::vec2* uvData=nullptr;
            glm::vec2* uv2Data=nullptr;
            glm::vec2* uv3Data=nullptr;
            glm::vec2* uv4Data=nullptr;
            uint32_t vertexCount=0;
            VertexAttributeFlags definedAttributes() const;
            std::tuple<std::vector<VertexAttribute>,std::vector<slag::Buffer*>> toBuffers() const;
        };

        /**
         * Create an uninitialized mesh
         * @param definedAttributes Attributes this mesh contains
         * @param vertexCount Number of vertexes in this mesh
         * @param indexType If indexes contains uint16 or uint32 data
         * @param indexCount Number of indices in this mesh
         * @param bufferAccessibility Accessibility of each buffer in this mesh
         */
        Mesh(VertexAttributeFlags definedAttributes, uint32_t vertexCount,slag::Buffer::IndexSize indexType, uint32_t indexCount, const MeshBufferAccessibility& bufferAccessibility);

        /**
         * Immediately create a mesh from the given data
         * @param attributeBuffers structure containing pointers to vertex attribute data, and total number of vertexes
         * @param indexes Array of either uint16 or uint32 triangle indexes
         * @param indexBufferLength Size in bytes of indexes array
         * @param indexType If indexes contains uint16 or uint32 data
         * @param bufferAccessibility Accessibility of each buffer in this mesh
         */
        Mesh(const MeshAttributeData& attributeBuffers,void* indexes, uint32_t indexBufferLength, slag::Buffer::IndexSize indexType, const MeshBufferAccessibility& bufferAccessibility);

        ~Mesh();
        Mesh(const Mesh&)=delete;
        Mesh& operator=(const Mesh&)=delete;
        Mesh(Mesh&& from);
        Mesh& operator=(Mesh&& from);

        /**
         * Gets the size of a single attribute, or 0 for the custom attributes
         * @param attribute
         * @return
         */
        constexpr static uint32_t attributeSize(VertexAttribute attribute);

        slag::Buffer* positionBuffer() const;
        slag::Buffer* normalBuffer() const;
        slag::Buffer* tangentBuffer() const;
        slag::Buffer* colorBuffer() const;
        slag::Buffer* boneWeightBuffer() const;
        slag::Buffer* uvBuffer() const;
        slag::Buffer* uvBuffer2() const;
        slag::Buffer* uvBuffer3() const;
        slag::Buffer* uvBuffer4() const;

        /**
         * Acquire the data buffer to feed to the gpu for the indices
         * @return
         */
        slag::Buffer* indexBuffer() const;
        ///Size of an index within the index buffer
        slag::Buffer::IndexSize indexSize()const;
        /**
         * Get a vertex attribute buffer by its attribute type
         * @param attribute
         * @return
         */
        slag::Buffer* attributeBuffer(VertexAttribute attribute) const;

        ///Total number of vertices in the mesh
        uint32_t vertexCount()const;
        ///Total number of indices in the mesh
        uint32_t indexCount()const;
        ///Vertex attributes this mesh contains
        Mesh::VertexAttributeFlags definedAttributes()const;
        static std::vector<Mesh::VertexAttribute> toList(VertexAttributeFlags definedAttributes);
        static slag::VertexDescription createVertexDescription(VertexAttributeFlags attributes);

    private:
        void move(Mesh& from);
        slag::Buffer*& attributeBuffer(int attributeIndex);

        slag::Buffer* _positionBuffer=nullptr;
        slag::Buffer* _normalBuffer=nullptr;
        slag::Buffer* _tangentBuffer=nullptr;
        slag::Buffer* _colorBuffer=nullptr;
        slag::Buffer* _boneWeightBuffer=nullptr;
        slag::Buffer* _uvBuffer=nullptr;
        slag::Buffer* _uvBuffer2=nullptr;
        slag::Buffer* _uvBuffer3=nullptr;
        slag::Buffer* _uvBuffer4=nullptr;
        slag::Buffer* _indexBuffer= nullptr;
        uint32_t _vertexCount = 0;
        slag::Buffer::IndexSize _indexSize = slag::Buffer::IndexSize::UINT16;
        VertexAttributeFlags _attributeFlags=static_cast<VertexAttributeFlags>(0);

    };

    inline Mesh::VertexAttributeFlags operator|(const Mesh::VertexAttributeFlags& a, const Mesh::VertexAttributeFlags& b)
    {
        return static_cast<Mesh::VertexAttributeFlags>(static_cast<uint16_t>(a) | static_cast<uint16_t>(b));
    }

    inline Mesh::VertexAttributeFlags operator&(const Mesh::VertexAttributeFlags& a, const Mesh::VertexAttributeFlags& b)
    {
        return static_cast<Mesh::VertexAttributeFlags>(static_cast<uint16_t>(a) & static_cast<uint16_t>(b));
    }

    inline Mesh::VertexAttributeFlags operator~(const Mesh::VertexAttributeFlags& a)
    {
        return static_cast<Mesh::VertexAttributeFlags>(~static_cast<uint16_t>(a));
    }

    inline Mesh::VertexAttributeFlags operator^(const Mesh::VertexAttributeFlags& a, const Mesh::VertexAttributeFlags& b)
    {
        return static_cast<Mesh::VertexAttributeFlags>(static_cast<uint16_t>(a) ^ static_cast<uint16_t>(b));
    }

    inline Mesh::VertexAttributeFlags operator|=(Mesh::VertexAttributeFlags& a, const Mesh::VertexAttributeFlags& b)
    {
        a = a | b;
        return a;
    }

    inline Mesh::VertexAttributeFlags operator&=(Mesh::VertexAttributeFlags& a, const Mesh::VertexAttributeFlags& b)
    {
        a = a & b;
        return a;
    }

    inline Mesh::VertexAttributeFlags operator|(const Mesh::VertexAttribute& a, const Mesh::VertexAttribute& b)
    {
        return static_cast<Mesh::VertexAttributeFlags>(static_cast<uint16_t>(a) | static_cast<uint16_t>(b));
    }

    inline Mesh::VertexAttributeFlags operator&(const Mesh::VertexAttribute& a, const Mesh::VertexAttribute& b)
    {
        return static_cast<Mesh::VertexAttributeFlags>(static_cast<uint16_t>(a) & static_cast<uint16_t>(b));
    }

    inline Mesh::VertexAttributeFlags operator|(const Mesh::VertexAttributeFlags& a, const Mesh::VertexAttribute& b)
    {
        return static_cast<Mesh::VertexAttributeFlags>(static_cast<uint16_t>(a) | static_cast<uint16_t>(b));
    }

    inline Mesh::VertexAttributeFlags operator&(const Mesh::VertexAttributeFlags& a, const Mesh::VertexAttribute& b)
    {
        return static_cast<Mesh::VertexAttributeFlags>(static_cast<uint16_t>(a) & static_cast<uint16_t>(b));
    }

    inline Mesh::VertexAttributeFlags operator|(const Mesh::VertexAttribute& a, const Mesh::VertexAttributeFlags& b)
    {
        return static_cast<Mesh::VertexAttributeFlags>(static_cast<uint16_t>(a) | static_cast<uint16_t>(b));
    }

    inline Mesh::VertexAttributeFlags operator&(const Mesh::VertexAttribute& a, const Mesh::VertexAttributeFlags& b)
    {
        return static_cast<Mesh::VertexAttributeFlags>(static_cast<uint16_t>(a) & static_cast<uint16_t>(b));
    }


    inline Mesh::VertexAttributeFlags operator^(const Mesh::VertexAttributeFlags& a, const Mesh::VertexAttribute& b)
    {
        return static_cast<Mesh::VertexAttributeFlags>(static_cast<uint16_t>(a) ^ static_cast<uint16_t>(b));
    }

    inline Mesh::VertexAttributeFlags operator|=(Mesh::VertexAttributeFlags& a, const Mesh::VertexAttribute& b)
    {
        a = a | b;
        return a;
    }

    inline Mesh::VertexAttributeFlags operator&=(Mesh::VertexAttributeFlags& a, const Mesh::VertexAttribute& b)
    {
        a = a & b;
        return a;
    }

    inline Mesh::VertexAttributeFlags operator^=(Mesh::VertexAttributeFlags& a, const Mesh::VertexAttribute& b)
    {
        a = a ^ b;
        return a;
    }
    
} // crucible

#endif //CRUCIBLE_MESH_H
