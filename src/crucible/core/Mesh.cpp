#include "Mesh.h"

#include <cstring>
#include <unordered_set>
#include <execution>
#include <ranges>
#include <lz4.h>
#include <boost/endian/conversion.hpp>
#include <crucible/core/serialization/Serializer.h>

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
            _indexBuffer = slag::Buffer::newBuffer(indexes,indexBufferLength,slag::Buffer::GPU,slag::Buffer::INDEX_BUFFER);
            _indexSize = indexType;
        }

        Mesh::Mesh(unsigned char* lz4MeshData, uint64_t meshDataLength)
        {
            const unsigned char* lastByte = lz4MeshData + meshDataLength;
            uint8_t int32 = *std::bit_cast<uint8_t*>(lz4MeshData);
            lz4MeshData++;
            if constexpr (std::endian::native == std::endian::big)
            {
                boost::endian::big_to_native_inplace(int32);
            }
            slag::Buffer::IndexSize indexType = slag::Buffer::UINT16;
            if (int32)
            {
                indexType = slag::Buffer::UINT32;
            }

            int32_t compressedSize = *std::bit_cast<int32_t*>(lz4MeshData);
            lz4MeshData+=sizeof(int32_t);
            int32_t decompressedSize = *std::bit_cast<int32_t*>(lz4MeshData);
            lz4MeshData+=sizeof(int32_t);
            if constexpr (std::endian::native == std::endian::big)
            {
                boost::endian::big_to_native_inplace(compressedSize);
                boost::endian::big_to_native_inplace(decompressedSize);
            }
            if (!(lz4MeshData + compressedSize <= lastByte))
            {
                throw std::runtime_error("Mesh Data is corrupt");
            }
            else
            {
                std::vector<char> decompressed(decompressedSize);
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
                        boost::endian::big_to_native_inplace(*reinterpret_cast<uint32_t*>(decompressed.data() + i));
                    }
                }

                _indexBuffer = slag::Buffer::newBuffer(decompressed.data(),decompressed.size(),slag::Buffer::GPU,slag::Buffer::INDEX_BUFFER);
            }

            uint8_t attributeCount = *std::bit_cast<uint8_t*>(lz4MeshData);
            lz4MeshData+=sizeof(uint8_t);

            std::string attributeName;
            std::unordered_set<Mesh::VertexAttribute> foundAttributes;
            uint16_t attributeIndex = 0;
            _attributeStreams.resize(attributeCount);
            _definedAttributes.resize(attributeCount);
            while (lz4MeshData <= lastByte && attributeIndex < attributeCount)
            {
                uint16_t attribute = *std::bit_cast<uint16_t*>(lz4MeshData);
                lz4MeshData+=sizeof(uint16_t);
                Mesh::VertexAttribute currentAttribute = serializeIndexAttribute(attribute);
                if(foundAttributes.contains(currentAttribute))
                {
                    throw std::runtime_error("Duplicate attribute provided when creating mesh");
                }
                foundAttributes.emplace(currentAttribute);
                int32_t compressedStreamSize = *std::bit_cast<int32_t*>(lz4MeshData);
                lz4MeshData+=sizeof(int32_t);
                int32_t decompressedStreamSize = *std::bit_cast<int32_t*>(lz4MeshData);
                lz4MeshData+=sizeof(int32_t);
                if constexpr (std::endian::native == std::endian::big)
                {
                    boost::endian::big_to_native_inplace(compressedStreamSize);
                    boost::endian::big_to_native_inplace(decompressedStreamSize);
                }

                if (!(lz4MeshData + compressedStreamSize <= lastByte))
                {
                    throw std::runtime_error("Mesh Data is corrupt");
                }
                std::vector<char> decompressedStream(decompressedStreamSize);

                auto result = LZ4_decompress_safe(reinterpret_cast<const char*>(lz4MeshData),decompressedStream.data(),compressedStreamSize,decompressedStreamSize);
                if (result<0)
                {
                    throw std::runtime_error("Mesh Data Decompression failure");
                }
                lz4MeshData += compressedStreamSize;

                if constexpr (std::endian::native == std::endian::big)
                {
                    for (size_t i=0; i < decompressedStreamSize; i+=sizeof(uint32_t))
                    {
                        boost::endian::big_to_native_inplace(*reinterpret_cast<uint32_t*>(decompressedStream.data() + i));
                    }
                }

                _definedAttributes[attributeIndex] = currentAttribute;
                _attributeIndexes[currentAttribute] = static_cast<VertexAttribute>(attributeIndex);
                _attributeStreams[attributeIndex] = VertexAttributeStream(decompressedStream.data(),decompressedStream.size(),slag::Buffer::GPU);
                attributeIndex++;

            }

            size_t vertexCount = 0;
            for (auto vertexAttribute : _definedAttributes)
            {
                auto index = _attributeIndexes[vertexAttribute];
                auto attributeSize = Mesh::attributeSize(vertexAttribute);
                auto size = _attributeStreams[index].data()->size()/attributeSize;
                if (vertexCount == 0)
                {
                    vertexCount = size;
                }
                else if (vertexCount != size)
                {
                    throw std::runtime_error("Number of vertex attributes do not match");
                }
            }
            _vertexCount = vertexCount;
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
                    return (sizeof(float)*4)+(sizeof(uint16_t)*4);
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

        Mesh::VertexAttribute Mesh::serializeIndexAttribute(uint16_t index)
        {
            if (index == 0)
            {
                throw std::runtime_error("Vertex attribute\"0\" does not correspond to known attribute");
            }

            uint16_t position = 0;
            while ((index & 1) == 0)
            {
                index >>= 1;
                ++position;
            }
            return static_cast<Mesh::VertexAttribute>(position);
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

        slag::Buffer* Mesh::indexBuffer()
        {
            return _indexBuffer;
        }

        slag::Buffer::IndexSize Mesh::indexSize() const
        {
            return _indexSize;
        }

        size_t Mesh::vertexCount() const
        {
            return _vertexCount;
        }

        size_t Mesh::indexCount() const
        {
            return _indexBuffer->size()/(_indexSize == slag::Buffer::UINT16? sizeof(uint16_t) : sizeof(uint32_t));
        }

        std::vector<unsigned char> Mesh::toData() const
        {
            return toData(definedAttributes().data(),definedAttributes().size());
        }

        std::vector<unsigned char> Mesh::toData(const VertexAttribute* attributes, uint8_t attributeCount) const
        {
            std::vector<unsigned char> data;
            {
                uint8_t int32 = 0;
                if (_indexSize == slag::Buffer::UINT32)
                {
                    int32 = 1;
                }
                //indexSize
                data.push_back(int32);
                auto idxs = _indexBuffer->downloadData();
                int32_t compressedSize = LZ4_compressBound(static_cast<int32_t>(idxs.size()));
                int32_t decompressedSize = static_cast<int32_t>(idxs.size());
                std::vector<char> compressedData(compressedSize);
                compressedSize = LZ4_compress_fast(reinterpret_cast<const char*>(idxs.data()),compressedData.data(),static_cast<int32_t>(idxs.size()),compressedSize,1);

                if constexpr (std::endian::native == std::endian::big)
                {
                    boost::endian::native_to_little_inplace(compressedSize);
                    boost::endian::native_to_little_inplace(decompressedSize);
                }
                //compressed size
                bufferInsert(data,compressedSize);
                //decompressed size
                bufferInsert(data,decompressedSize);
                //index data
                data.insert(data.end(),compressedData.begin(),compressedData.begin()+compressedSize);
            }

            bufferInsert(data,attributeCount);

            for(size_t i=0; i < attributeCount; i++)
            {
                auto attribute = attributes[i];
                uint16_t attributeValue = attributeSerializeIndex(attribute);

                auto index = _attributeIndexes[attribute];
                auto& stream = _attributeStreams[index];
                auto streamData = stream.data()->downloadData();
                int32_t compressedSize = LZ4_compressBound(static_cast<int32_t>(streamData.size()));
                int32_t decompressedSize = static_cast<int32_t>(streamData.size());
                std::vector<char> compressedData(compressedSize);
                compressedSize =LZ4_compress_fast(reinterpret_cast<const char*>(streamData.data()),compressedData.data(),static_cast<int32_t>(streamData.size()),compressedSize,1);
                if constexpr (std::endian::native == std::endian::big)
                {
                    boost::endian::native_to_little_inplace(attributeValue);
                    boost::endian::native_to_little_inplace(compressedSize);
                    boost::endian::native_to_little_inplace(decompressedSize);
                }
                bufferInsert(data,attributeValue);
                //compressed size
                bufferInsert(data,compressedSize);
                //decompressed size
                bufferInsert(data,decompressedSize);
                //index data
                data.insert(data.end(),compressedData.begin(),compressedData.begin()+compressedSize);
            }
            return data;
        }
    } // core
} // crucible