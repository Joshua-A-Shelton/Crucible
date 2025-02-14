#include "Mesh.h"

#include <cstring>
#include <unordered_set>
#include <execution>
#include <ranges>

namespace crucible
{
    namespace core
    {
        Mesh::Mesh(const std::vector<VertexAttributeInputInfo>& attributeInfo,void* indexes, size_t indexBufferLength, slag::Buffer::IndexSize indexType)
        {
            std::unordered_set<VertexAttribute> foundAttributes;
            size_t index = 0;
            size_t vertexCount = 0;
            for(auto& info: attributeInfo)
            {
                if(foundAttributes.contains(info.attribute))
                {
                    throw std::runtime_error("Duplicate vertex attribute provided when creating mesh");
                }
                else if(info.attribute >= VERTEX_ATTRIBUTE_ENUM_SIZE)
                {
                    throw std::runtime_error("Invalid attribute type provided when creating mesh");
                }
                foundAttributes.emplace(info.attribute);
                _attributeIndexes[info.attribute] = static_cast<VertexAttribute>(index);
                _definedAttributes.push_back(info.attribute);
                index++;

                auto attSize = attributeSize(info.attribute);
                if(vertexCount == 0)
                {
                    vertexCount = info.dataLength/attSize;
                }
                else if(info.dataLength/attSize != vertexCount)
                {
                    throw std::runtime_error("different amount of vertices supplied in attributes for mesh");
                }
            }
            if(vertexCount == 0)
            {
                throw std::runtime_error("No vertex data was provided for mesh");
            }
            _vertexCount = vertexCount;
            std::sort(_definedAttributes.begin(), _definedAttributes.end());
            _attributeStreams.resize(_definedAttributes.size());
            std::ranges::iota_view idxs((size_t)0,attributeInfo.size());
            std::for_each(std::execution::par, idxs.begin(), idxs.end(), [&](size_t i)
            {
                auto& info = attributeInfo[i];
                _attributeStreams[i] = VertexAttributeStream(info.data,info.dataLength,info.accessibility);
            });
            _indexBuffer = slag::Buffer::newBuffer(indexes,indexBufferLength,slag::Buffer::GPU,slag::Buffer::VERTEX_BUFFER);
            _indexSize = indexType;
        }

        size_t Mesh::attributeSize(Mesh::VertexAttribute attribute)
        {
            switch(attribute)
            {
                case POSITION_3D:
                    return sizeof(float)*3;
                case POSITION_2D:
                    return sizeof(float)*2;
                case NORMAL:
                    return sizeof(float)*3;
                case UV:
                    return sizeof(float)*2;
                case COLOR:
                    return sizeof(char)*4;
                case VERTEX_ATTRIBUTE_ENUM_SIZE:
                    throw std::runtime_error("Invalid attribute provided");
            }
            throw std::runtime_error("Unable to get size of attribute");
        }

        Mesh::~Mesh()
        {
            if(_indexBuffer)
            {
                delete _indexBuffer;
            }
        }

        Mesh::Mesh(Mesh&& from)
        {
            move(from);
        }

        Mesh& Mesh::operator=(Mesh&& from)
        {
            move(from);
            return *this;
        }

        void Mesh::move(Mesh& from)
        {
            _attributeStreams.swap(from._attributeStreams);

            memcpy(_attributeIndexes,from._attributeIndexes,VERTEX_ATTRIBUTE_ENUM_SIZE);

            _definedAttributes.swap(from._definedAttributes);
            std::swap(_indexBuffer,from._indexBuffer);
            _indexSize = from._indexSize;
            _vertexCount = from._vertexCount;
        }

        const std::vector<Mesh::VertexAttribute> Mesh::definedAttributes() const
        {
            return _definedAttributes;
        }

        slag::Buffer* Mesh::getBuffer(Mesh::VertexAttribute forAttribute)
        {
            auto index = _attributeIndexes[forAttribute];
            if(index == VERTEX_ATTRIBUTE_ENUM_SIZE)
            {
                return nullptr;
            }
            return _attributeStreams[index].data();
        }

        size_t Mesh::vertexCount() const
        {
            return _vertexCount;
        }

        size_t Mesh::indexCount() const
        {
            return _indexBuffer->size()/(_indexSize == slag::Buffer::UINT16? sizeof(uint16_t) : sizeof(uint32_t));
        }


    } // core
} // crucible