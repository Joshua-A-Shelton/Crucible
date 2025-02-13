#ifndef CRUCIBLE_MESH_H
#define CRUCIBLE_MESH_H
#include <vector>
#include "VertexAttributeStream.h"

namespace crucible
{
    namespace core
    {
        class Mesh
        {
        public:
            enum VertexAttribute
            {
                POSITION_3D,
                POSITION_2D,
                NORMAL,
                UV,
                COLOR,
                VERTEX_ATTRIBUTE_ENUM_SIZE
            };
            static size_t attributeSize(VertexAttribute attribute);
            struct VertexAttributeInputInfo
            {
                VertexAttribute attribute;
                void* data;
                size_t dataLength;
                slag::Buffer::Accessibility accessibility;
            };


            Mesh(const std::vector<VertexAttributeInputInfo>& attributeInfo,void* indexes, size_t indexBufferLength, slag::Buffer::IndexSize indexType);
            ~Mesh();
            Mesh(const Mesh&)=delete;
            Mesh& operator=(const Mesh&)=delete;
            Mesh(Mesh&& from);
            Mesh& operator=(Mesh&& from);
            const std::vector<VertexAttribute> definedAttributes()const;
            slag::Buffer* getBuffer(VertexAttribute forAttribute);
            size_t vertexCount()const;
            size_t indexCount()const;
        private:
            void move(Mesh& from);
            std::vector<VertexAttributeStream> _attributeStreams;

            VertexAttribute _attributeIndexes[VERTEX_ATTRIBUTE_ENUM_SIZE] =
            {
                VERTEX_ATTRIBUTE_ENUM_SIZE,//POSITION_3D
                VERTEX_ATTRIBUTE_ENUM_SIZE,//POSITION_2D
                VERTEX_ATTRIBUTE_ENUM_SIZE,//NORMAL
                VERTEX_ATTRIBUTE_ENUM_SIZE,//UV
                VERTEX_ATTRIBUTE_ENUM_SIZE,//COLOR
            };
            std::vector<VertexAttribute> _definedAttributes;
            slag::Buffer* _indexBuffer= nullptr;
            slag::Buffer::IndexSize _indexSize = slag::Buffer::UINT16;
            size_t _vertexCount = 0;

        };

    } // core
} // crucible

#endif //CRUCIBLE_MESH_H
