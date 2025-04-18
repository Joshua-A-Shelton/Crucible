#ifndef CRUCIBLE_MESH_H
#define CRUCIBLE_MESH_H
#include <vector>
#include <glm/fwd.hpp>

#include "VertexAttributeStream.h"

namespace crucible
{
    namespace core
    {
        /**
         * Holds the vertex and index data required for drawing on the GPU
         */
        class Mesh
        {
        public:
            ///What kind of data is represented
            enum VertexAttribute
            {
                ///X, Y, and Z position (floats)
                POSITION_3D,
                ///X and Y position (floats)
                POSITION_2D,
                ///Normal vector of vertex (X, Y, and Z floats)
                NORMAL,
                ///UV texture coordinates
                UV,
                ///Tangent vector of vertex (X,Y, and X floats)
                TANGENT,
                ///R, G, B, A values (byte)
                COLOR,
                ///Bone ID (UINT_16) Bone weight (float) (x4)
                BONE_WEIGHT,
                ///Size of the VertexAttribute enum
                VERTEX_ATTRIBUTE_ENUM_SIZE
            };
            /**
             * Gets the size of a single attribute
             * @param attribute
             * @return
             */
            static size_t attributeSize(VertexAttribute attribute);
            ///Helper struct for sending vertex data to mesh creation
            struct VertexAttributeInputInfo
            {
                ///Which attribute the data is
                VertexAttribute attribute;
                ///Pointer to attribute data
                void* data;
                ///Size of attribute data in bytes
                size_t dataLength;
                ///Level of accessibility for the data in the final mesh
                slag::Buffer::Accessibility accessibility;
            };

            /**
             * Creates a mesh
             * @param attributeInfo contains all the data required to build the vertex buffers
             * @param indexes pointer to index data
             * @param indexBufferLength size of index data in bytes
             * @param indexType whether or not each index is 16 or 32 bytes
             */
            Mesh(const std::vector<VertexAttributeInputInfo>& attributeInfo,void* indexes, size_t indexBufferLength, slag::Buffer::IndexSize indexType);
            Mesh(unsigned char* lz4MeshData, uint64_t meshDataLength);
            ~Mesh();
            Mesh(const Mesh&)=delete;
            Mesh& operator=(const Mesh&)=delete;
            Mesh(Mesh&& from);
            Mesh& operator=(Mesh&& from);

            static uint16_t attributeSerializeIndex(VertexAttribute attribute);
            static VertexAttribute serializeIndexAttribute(uint16_t index);
            ///Which vertex attributes are defined for this mesh
            const std::vector<VertexAttribute> definedAttributes()const;
            /**
             * Acquire the data buffer to feed to the gpu for the given attribute
             * @param forAttribute
             * @return
             */
            slag::Buffer* getBuffer(VertexAttribute forAttribute);

            /**
             * Acquire the data buffer to feed to the gpu for the indices
             * @return
             */
            slag::Buffer* indexBuffer();

            slag::Buffer::IndexSize indexSize()const;

            ///Total number of vertices in the mesh
            size_t vertexCount()const;
            ///Total number of indices in the mesh
            size_t indexCount()const;

            std::vector<unsigned char> toData()const;
            std::vector<unsigned char> toData(const VertexAttribute* attributes, uint8_t attributeCount)const;
        private:
            void move(Mesh& from);
            std::vector<VertexAttributeStream> _attributeStreams;

            VertexAttribute _attributeIndexes[VERTEX_ATTRIBUTE_ENUM_SIZE] =
            {
                VERTEX_ATTRIBUTE_ENUM_SIZE,//POSITION_3D
                VERTEX_ATTRIBUTE_ENUM_SIZE,//POSITION_2D
                VERTEX_ATTRIBUTE_ENUM_SIZE,//NORMAL
                VERTEX_ATTRIBUTE_ENUM_SIZE,//UV
                VERTEX_ATTRIBUTE_ENUM_SIZE,//TANGENT
                VERTEX_ATTRIBUTE_ENUM_SIZE,//COLOR
                VERTEX_ATTRIBUTE_ENUM_SIZE,//BONE_WEIGHT
            };
            std::vector<VertexAttribute> _definedAttributes;
            slag::Buffer* _indexBuffer= nullptr;
            slag::Buffer::IndexSize _indexSize = slag::Buffer::UINT16;
            size_t _vertexCount = 0;

        };

    } // core
} // crucible

#endif //CRUCIBLE_MESH_H
