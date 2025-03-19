#include "Mesh.h"

#include <cstring>
#include <unordered_set>
#include <execution>
#include <ranges>
#include <lz4.h>
#include <boost/endian/conversion.hpp>

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

        Mesh::Mesh(unsigned char* lz4MeshData, const unsigned char* end, uint16_t attributeCount)
        {
            uint8_t int32 = *std::bit_cast<uint8_t*>(lz4MeshData);
            lz4MeshData++;
            if constexpr (std::endian::native == std::endian::big)
            {
                boost::endian::big_to_native(int32);
            }
            slag::Buffer::IndexSize indexType = slag::Buffer::UINT16;
            if (int32)
            {
                indexType = slag::Buffer::UINT32;
            }

            uint32_t compressedSize = *std::bit_cast<uint32_t*>(lz4MeshData);
            lz4MeshData+=sizeof(uint32_t);
            uint32_t decompressedSize = *std::bit_cast<uint32_t*>(lz4MeshData);
            lz4MeshData+=sizeof(uint32_t);
            if constexpr (std::endian::native == std::endian::big)
            {
                boost::endian::big_to_native(compressedSize);
                boost::endian::big_to_native(decompressedSize);
            }
            if (!(lz4MeshData + compressedSize < end))
            {
                throw std::runtime_error("Mesh Data is corrupt");
            }
            else
            {
                std::vector<char> decompressed(decompressedSize);
                //FIXME: I'm not sure what to do here if the compressed size is greater than int capacity...
                auto result = LZ4_decompress_safe(reinterpret_cast<const char*>(lz4MeshData), decompressed.data(),compressedSize,decompressedSize);
                if (result<0)
                {
                    throw std::runtime_error("Mesh Data Decompression failure");
                }
                lz4MeshData += compressedSize;

                if constexpr (std::endian::native == std::endian::big)
                {
                    for (size_t i=0; i < decompressedSize; i+=sizeof(uint32_t))
                    {
                        boost::endian::big_to_native(*reinterpret_cast<uint32_t*>(decompressed.data() + i));
                    }
                }

                _indexBuffer = slag::Buffer::newBuffer(decompressed.data(),decompressed.size(),slag::Buffer::GPU,slag::Buffer::INDEX_BUFFER);
            }


            std::string attributeName;
            std::unordered_set<Mesh::VertexAttribute> foundAttributes;
            uint16_t attributeIndex = 0;
            _attributeStreams.resize(attributeCount);
            _definedAttributes.resize(attributeCount);
            while (lz4MeshData < end && attributeIndex < attributeCount)
            {
                uint16_t attribute = *std::bit_cast<uint16_t*>(lz4MeshData);
                lz4MeshData+=sizeof(uint32_t);
                Mesh::VertexAttribute currentAttribute = static_cast<Mesh::VertexAttribute>(attribute);
                if(foundAttributes.contains(currentAttribute))
                {
                    throw std::runtime_error("Duplicate attribute provided when creating mesh");
                }
                foundAttributes.emplace(currentAttribute);
                uint32_t compressedStreamSize = *std::bit_cast<uint32_t*>(lz4MeshData);
                lz4MeshData+=sizeof(uint32_t);
                uint32_t decompressedStreamSize = *std::bit_cast<uint32_t*>(lz4MeshData);
                lz4MeshData+=sizeof(uint32_t);
                if constexpr (std::endian::native == std::endian::big)
                {
                    boost::endian::big_to_native(compressedStreamSize);
                    boost::endian::big_to_native(decompressedStreamSize);
                }
                if (!(lz4MeshData + compressedSize <= end))
                {
                    throw std::runtime_error("Mesh Data is corrupt");
                }
                std::vector<char> decompressedStream(decompressedStreamSize);
                //FIXME: I'm not sure what to do here if the compressed size is greater than int capacity...
                LZ4_decompress_safe(reinterpret_cast<const char*>(lz4MeshData),decompressedStream.data(),compressedStreamSize,decompressedStreamSize);
                lz4MeshData += compressedStreamSize;

                if constexpr (std::endian::native == std::endian::big)
                {
                    for (size_t i=0; i < decompressedStreamSize; i+=sizeof(uint32_t))
                    {
                        boost::endian::big_to_native(*reinterpret_cast<uint32_t*>(decompressedStream.data() + i));
                    }
                }

                _definedAttributes[attributeIndex] = currentAttribute;;
                _attributeIndexes[currentAttribute] = static_cast<VertexAttribute>(attributeIndex);
                _attributeStreams[attributeIndex] = VertexAttributeStream(decompressedStream.data(),decompressedStream.size(),slag::Buffer::GPU);
                attributeIndex++;

            }
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
                case BONE_WEIGHT:
                    return (sizeof(float)*4)+(sizeof(uint32_t)*4);
                case VERTEX_ATTRIBUTE_ENUM_SIZE:
                    throw std::runtime_error("Invalid attribute provided");
            }
            throw std::runtime_error("Unable to get size of attribute");
        }

        uint16_t Mesh::attributeSerializeIndex(VertexAttribute attribute)
        {
            uint16_t index = 1;
            return index << attribute;
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