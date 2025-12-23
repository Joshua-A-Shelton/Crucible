#include "Mesh.h"

#include <lz4.h>

#include "BoneWeights.h"
#include "Game.h"
#include <glm/vec2.hpp>
#include <glm/vec3.hpp>
#include <glm/vec4.hpp>


namespace crucible
{
    slag::Buffer::Accessibility& Mesh::MeshBufferAccessibility::operator[](Mesh::VertexAttribute attribute)
    {
        return *(&(position) + std::countr_zero((uint16_t)attribute));
    }

    slag::Buffer::Accessibility Mesh::MeshBufferAccessibility::operator[](crucible::Mesh::VertexAttribute attribute) const
    {
        return *(&(position) + std::countr_zero((uint16_t)attribute));
    }


    Mesh::VertexAttributeFlags Mesh::MeshAttributeData::definedAttributes() const
    {
        Mesh::VertexAttributeFlags flags = static_cast<Mesh::VertexAttributeFlags>(0);
        if (positionData){flags |= Mesh::VertexAttribute::POSITION;}
        if (normalData){flags |= Mesh::VertexAttribute::NORMAL;}
        if (tangentData){flags |= Mesh::VertexAttribute::TANGENT;}
        if (colorData){flags |= Mesh::VertexAttribute::COLOR;}
        if (boneWeightData){flags |= Mesh::VertexAttribute::BONE_WEIGHT;}
        if (uvData){flags |= Mesh::VertexAttribute::UV;}
        if (uv2Data){flags |= Mesh::VertexAttribute::UV2;}
        if (uv3Data){flags |= Mesh::VertexAttribute::UV3;}
        if (uv4Data){flags |= Mesh::VertexAttribute::UV4;}
        return flags;
    }

    std::tuple<std::vector<Mesh::VertexAttribute>, std::vector<slag::Buffer*>> Mesh::MeshAttributeData::toBuffers() const
    {
        std::vector<Mesh::VertexAttribute> attributes;
        std::vector<slag::Buffer*> buffers;
        if (positionData!=nullptr)
        {
            attributes.emplace_back(Mesh::VertexAttribute::POSITION);
            buffers.emplace_back(slag::Buffer::newBuffer(positionData,Mesh::attributeSize(Mesh::VertexAttribute::POSITION)*vertexCount,slag::Buffer::Accessibility::CPU_AND_GPU));
        }
        if (normalData!=nullptr)
        {
            attributes.emplace_back(Mesh::VertexAttribute::NORMAL);
            buffers.emplace_back(slag::Buffer::newBuffer(normalData,Mesh::attributeSize(Mesh::VertexAttribute::NORMAL)*vertexCount,slag::Buffer::Accessibility::CPU_AND_GPU));
        }
        if (tangentData!=nullptr)
        {
            attributes.emplace_back(Mesh::VertexAttribute::TANGENT);
            buffers.emplace_back(slag::Buffer::newBuffer(tangentData,Mesh::attributeSize(Mesh::VertexAttribute::TANGENT)*vertexCount,slag::Buffer::Accessibility::CPU_AND_GPU));
        }
        if (colorData!=nullptr)
        {
            attributes.emplace_back(Mesh::VertexAttribute::COLOR);
            buffers.emplace_back(slag::Buffer::newBuffer(colorData,Mesh::attributeSize(Mesh::VertexAttribute::COLOR)*vertexCount,slag::Buffer::Accessibility::CPU_AND_GPU));
        }
        if (boneWeightData!=nullptr)
        {
            attributes.emplace_back(Mesh::VertexAttribute::BONE_WEIGHT);
            buffers.emplace_back(slag::Buffer::newBuffer(boneWeightData,Mesh::attributeSize(Mesh::VertexAttribute::BONE_WEIGHT)*vertexCount,slag::Buffer::Accessibility::CPU_AND_GPU));
        }
        if (uvData!=nullptr)
        {
            attributes.emplace_back(Mesh::VertexAttribute::UV);
            buffers.emplace_back(slag::Buffer::newBuffer(uvData,Mesh::attributeSize(Mesh::VertexAttribute::UV)*vertexCount,slag::Buffer::Accessibility::CPU_AND_GPU));
        }
        if (uv2Data!=nullptr)
        {
            attributes.emplace_back(Mesh::VertexAttribute::UV2);
            buffers.emplace_back(slag::Buffer::newBuffer(uv2Data,Mesh::attributeSize(Mesh::VertexAttribute::UV2)*vertexCount,slag::Buffer::Accessibility::CPU_AND_GPU));
        }
        if (uv3Data!=nullptr)
        {
            attributes.emplace_back(Mesh::VertexAttribute::UV3);
            buffers.emplace_back(slag::Buffer::newBuffer(uv3Data,Mesh::attributeSize(Mesh::VertexAttribute::UV3)*vertexCount,slag::Buffer::Accessibility::CPU_AND_GPU));
        }
        if (uv4Data!=nullptr)
        {
            attributes.emplace_back(Mesh::VertexAttribute::UV4);
            buffers.emplace_back(slag::Buffer::newBuffer(uv4Data,Mesh::attributeSize(Mesh::VertexAttribute::UV4)*vertexCount,slag::Buffer::Accessibility::CPU_AND_GPU));
        }
        return {attributes,buffers};
    }

    Mesh::Mesh(VertexAttributeFlags definedAttributes, uint32_t vertexCount,slag::Buffer::IndexSize indexType, uint32_t indexCount, const MeshBufferAccessibility& bufferAccessibility)
    {
        if (vertexCount < 3)
        {
            throw std::invalid_argument("At least 3 vertices must be defined");
        }
        if (indexCount%3 != 0)
        {
            throw std::invalid_argument("Index buffer length must be a multiple of 3");
        }
        if (!static_cast<bool>(definedAttributes & VertexAttribute::POSITION))
        {
            throw std::invalid_argument("Position attribute is required");
        }
        _indexSize = indexType;
        _vertexCount = vertexCount;
        _attributeFlags = definedAttributes;

        _positionBuffer = slag::Buffer::newBuffer(attributeSize(VertexAttribute::POSITION)*vertexCount,bufferAccessibility.position,slag::Buffer::UsageFlags::VERTEX_BUFFER);
        if (static_cast<bool>(definedAttributes & VertexAttribute::NORMAL))
        {
            _normalBuffer = slag::Buffer::newBuffer(attributeSize(VertexAttribute::NORMAL)*vertexCount,bufferAccessibility.normal,slag::Buffer::UsageFlags::VERTEX_BUFFER);
        }
        if (static_cast<bool>(definedAttributes & VertexAttribute::TANGENT))
        {
            _tangentBuffer = slag::Buffer::newBuffer(attributeSize(VertexAttribute::TANGENT)*vertexCount,bufferAccessibility.tangent,slag::Buffer::UsageFlags::VERTEX_BUFFER);
        }
        if (static_cast<bool>(definedAttributes & VertexAttribute::COLOR))
        {
            _colorBuffer = slag::Buffer::newBuffer(attributeSize(VertexAttribute::COLOR)*vertexCount,bufferAccessibility.color,slag::Buffer::UsageFlags::VERTEX_BUFFER);
        }
        if (static_cast<bool>(definedAttributes & VertexAttribute::BONE_WEIGHT))
        {
            _boneWeightBuffer = slag::Buffer::newBuffer(attributeSize(VertexAttribute::BONE_WEIGHT)*vertexCount,bufferAccessibility.boneWeight,slag::Buffer::UsageFlags::VERTEX_BUFFER);
        }
        if (static_cast<bool>(definedAttributes & VertexAttribute::UV))
        {
            _uvBuffer = slag::Buffer::newBuffer(attributeSize(VertexAttribute::UV)*vertexCount,bufferAccessibility.uv,slag::Buffer::UsageFlags::VERTEX_BUFFER);
        }
        if (static_cast<bool>(definedAttributes & VertexAttribute::UV2))
        {
            _uvBuffer2 = slag::Buffer::newBuffer(attributeSize(VertexAttribute::UV2)*vertexCount,bufferAccessibility.uv2,slag::Buffer::UsageFlags::VERTEX_BUFFER);
        }
        if (static_cast<bool>(definedAttributes & VertexAttribute::UV3))
        {
            _uvBuffer3 = slag::Buffer::newBuffer(attributeSize(VertexAttribute::UV3)*vertexCount,bufferAccessibility.uv3,slag::Buffer::UsageFlags::VERTEX_BUFFER);
        }
        if (static_cast<bool>(definedAttributes & VertexAttribute::UV4))
        {
            _uvBuffer4 = slag::Buffer::newBuffer(attributeSize(VertexAttribute::UV4)*vertexCount,bufferAccessibility.uv4,slag::Buffer::UsageFlags::VERTEX_BUFFER);
        }
        _indexBuffer = slag::Buffer::newBuffer((indexType == slag::Buffer::IndexSize::UINT16? sizeof(uint16_t) : sizeof(uint32_t))*indexCount,bufferAccessibility.index,slag::Buffer::UsageFlags::INDEX_BUFFER);
    }

    Mesh::Mesh(const MeshAttributeData& attributeBuffers, void* indexes, uint32_t indexBufferLength,slag::Buffer::IndexSize indexType, const MeshBufferAccessibility& bufferAccessibility)
    {
        if (attributeBuffers.vertexCount < 3)
        {
            throw std::invalid_argument("At least 3 vertices must be defined");
        }
        if (attributeBuffers.positionData == nullptr)
        {
            throw std::invalid_argument("Position data must be defined");
        }
        if (indexes == nullptr || indexBufferLength == 0)
        {
            throw std::invalid_argument("Index buffer Data must be provided");
        }
        if (indexBufferLength%3 != 0)
        {
            throw std::invalid_argument("Index buffer length must be a multiple of 3");
        }

        std::vector<slag::Buffer*> buffers(ATTRIBUTE_COUNT+1);
        _vertexCount = attributeBuffers.vertexCount;
        _indexSize = indexType;
        _attributeFlags = attributeBuffers.definedAttributes();
        try
        {
            //Position Buffer
            slag::Buffer* positionBuffer = slag::Buffer::newBuffer(attributeBuffers.positionData,attributeSize(VertexAttribute::POSITION)*attributeBuffers.vertexCount,bufferAccessibility.position,slag::Buffer::UsageFlags::VERTEX_BUFFER);
            buffers.push_back(positionBuffer);
            _positionBuffer = positionBuffer;
            //NormalBuffer
            if (attributeBuffers.normalData != nullptr)
            {
                slag::Buffer* normalBuffer = slag::Buffer::newBuffer(attributeBuffers.normalData,attributeSize(VertexAttribute::NORMAL)*attributeBuffers.vertexCount,bufferAccessibility.normal,slag::Buffer::UsageFlags::VERTEX_BUFFER);
                buffers.push_back(normalBuffer);
                _normalBuffer = normalBuffer;
            }
            //Tangent
            if (attributeBuffers.tangentData != nullptr)
            {
                slag::Buffer* tangentBuffer = slag::Buffer::newBuffer(attributeBuffers.tangentData,attributeSize(VertexAttribute::TANGENT)*attributeBuffers.vertexCount,bufferAccessibility.tangent,slag::Buffer::UsageFlags::VERTEX_BUFFER);
                buffers.push_back(tangentBuffer);
                _tangentBuffer = tangentBuffer;
            }
            //Color
            if (attributeBuffers.colorData != nullptr)
            {
                slag::Buffer* colorBuffer = slag::Buffer::newBuffer(attributeBuffers.colorData,attributeSize(VertexAttribute::COLOR)*attributeBuffers.vertexCount,bufferAccessibility.color,slag::Buffer::UsageFlags::VERTEX_BUFFER);
                buffers.push_back(colorBuffer);
                _colorBuffer = colorBuffer;
            }
            //Bone Weights
            if (attributeBuffers.boneWeightData != nullptr)
            {
                slag::Buffer* boneWeightBuffer = slag::Buffer::newBuffer(attributeBuffers.boneWeightData,attributeSize(VertexAttribute::BONE_WEIGHT)*attributeBuffers.vertexCount,bufferAccessibility.boneWeight,slag::Buffer::UsageFlags::VERTEX_BUFFER);
                buffers.push_back(boneWeightBuffer);
                _boneWeightBuffer = boneWeightBuffer;
            }
            //UV
            if (attributeBuffers.uvData != nullptr)
            {
                slag::Buffer* uvBuffer = slag::Buffer::newBuffer(attributeBuffers.uvData,attributeSize(VertexAttribute::UV)*attributeBuffers.vertexCount,bufferAccessibility.uv,slag::Buffer::UsageFlags::VERTEX_BUFFER);
                buffers.push_back(uvBuffer);
                _uvBuffer = uvBuffer;
            }
            //UV2
            if (attributeBuffers.uv2Data != nullptr)
            {
                slag::Buffer* uv2Buffer = slag::Buffer::newBuffer(attributeBuffers.uv2Data,attributeSize(VertexAttribute::UV2)*attributeBuffers.vertexCount,bufferAccessibility.uv2,slag::Buffer::UsageFlags::VERTEX_BUFFER);
                buffers.push_back(uv2Buffer);
                _uvBuffer2 = uv2Buffer;
            }
            //UV3
            if (attributeBuffers.uv3Data != nullptr)
            {
                slag::Buffer* uv3Buffer = slag::Buffer::newBuffer(attributeBuffers.uv3Data,attributeSize(VertexAttribute::UV3)*attributeBuffers.vertexCount,bufferAccessibility.uv3,slag::Buffer::UsageFlags::VERTEX_BUFFER);
                buffers.push_back(uv3Buffer);
                _uvBuffer3 = uv3Buffer;
            }
            //UV4
            if (attributeBuffers.uv4Data != nullptr)
            {
                slag::Buffer* uv4Buffer = slag::Buffer::newBuffer(attributeBuffers.uv4Data,attributeSize(VertexAttribute::UV4)*attributeBuffers.vertexCount,bufferAccessibility.uv4,slag::Buffer::UsageFlags::VERTEX_BUFFER);
                buffers.push_back(uv4Buffer);
                _uvBuffer4 = uv4Buffer;
            }
            
            //Index Buffer
            slag::Buffer* indexBuffer = slag::Buffer::newBuffer(indexes,indexBufferLength,bufferAccessibility.index,slag::Buffer::UsageFlags::INDEX_BUFFER);
            buffers.push_back(indexBuffer);
            _indexBuffer = indexBuffer;
        }
        catch (...)
        {
            for (auto buffer : buffers)
            {
                delete buffer;
            }
            throw;
        }
    }


    Mesh::~Mesh()
    {
        delete _positionBuffer;
        delete _normalBuffer;
        delete _tangentBuffer;
        delete _colorBuffer;
        delete _boneWeightBuffer;
        delete _uvBuffer;
        delete _uvBuffer2;
        delete _uvBuffer3;
        delete _uvBuffer4;
        delete _indexBuffer;
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

    slag::Buffer* Mesh::positionBuffer() const
    {
        return _positionBuffer;
    }

    slag::Buffer* Mesh::normalBuffer() const
    {
        return _normalBuffer;
    }

    slag::Buffer* Mesh::tangentBuffer() const
    {
        return _tangentBuffer;
    }

    slag::Buffer* Mesh::colorBuffer() const
    {
        return _colorBuffer;
    }

    slag::Buffer* Mesh::boneWeightBuffer() const
    {
        return _boneWeightBuffer;
    }

    slag::Buffer* Mesh::uvBuffer() const
    {
        return _uvBuffer;
    }

    slag::Buffer* Mesh::uvBuffer2() const
    {
        return _uvBuffer2;
    }

    slag::Buffer* Mesh::uvBuffer3() const
    {
        return _uvBuffer3;
    }

    slag::Buffer* Mesh::uvBuffer4() const
    {
        return _uvBuffer4;
    }

    slag::Buffer* Mesh::indexBuffer() const
    {
        return _indexBuffer;
    }

    slag::Buffer::IndexSize Mesh::indexSize() const
    {
        return _indexSize;
    }

    slag::Buffer* Mesh::attributeBuffer(VertexAttribute attribute) const
    {
        return *(&(_positionBuffer)+std::countr_zero(static_cast<uint16_t>(attribute)));
    }

    uint32_t Mesh::vertexCount() const
    {
        return _vertexCount;
    }

    uint32_t Mesh::indexCount() const
    {
        return _indexBuffer->size()/(_indexSize == slag::Buffer::IndexSize::UINT16? sizeof(uint16_t) : sizeof(uint32_t));
    }

    constexpr uint32_t Mesh::attributeSize(VertexAttribute attribute)
    {
        switch (attribute)
        {
        case VertexAttribute::POSITION:
        case VertexAttribute::NORMAL:
        case VertexAttribute::TANGENT:
            return sizeof(glm::vec3);
        case VertexAttribute::COLOR:
            return sizeof(unsigned char)*4;
        case VertexAttribute::BONE_WEIGHT:
            return sizeof(BoneWeights);
        case VertexAttribute::UV:
        case VertexAttribute::UV2:
        case VertexAttribute::UV3:
        case VertexAttribute::UV4:
            return sizeof(glm::vec2);
        }
        return 0;
    }

    Mesh::VertexAttributeFlags Mesh::definedAttributes() const
    {
        return  _attributeFlags;
    }

    std::vector<Mesh::VertexAttribute> Mesh::toList(VertexAttributeFlags definedAttributes)
    {
        std::vector<Mesh::VertexAttribute> result(std::popcount((uint16_t)definedAttributes));
        if ((definedAttributes & Mesh::VertexAttribute::POSITION) == static_cast<Mesh::VertexAttributeFlags>(Mesh::VertexAttribute::POSITION))
        {
            result.push_back(Mesh::VertexAttribute::POSITION);
        }
        if ((definedAttributes & Mesh::VertexAttribute::NORMAL) == static_cast<Mesh::VertexAttributeFlags>(Mesh::VertexAttribute::NORMAL))
        {
            result.push_back(Mesh::VertexAttribute::NORMAL);
        }
        if ((definedAttributes & Mesh::VertexAttribute::TANGENT) == static_cast<Mesh::VertexAttributeFlags>(Mesh::VertexAttribute::TANGENT))
        {
            result.push_back(Mesh::VertexAttribute::TANGENT);
        }
        if ((definedAttributes & Mesh::VertexAttribute::COLOR) == static_cast<Mesh::VertexAttributeFlags>(Mesh::VertexAttribute::COLOR))
        {
            result.push_back(Mesh::VertexAttribute::COLOR);
        }
        if ((definedAttributes & Mesh::VertexAttribute::BONE_WEIGHT) == static_cast<Mesh::VertexAttributeFlags>(Mesh::VertexAttribute::BONE_WEIGHT))
        {
            result.push_back(Mesh::VertexAttribute::BONE_WEIGHT);
        }
        if ((definedAttributes & Mesh::VertexAttribute::UV) == static_cast<Mesh::VertexAttributeFlags>(Mesh::VertexAttribute::UV))
        {
            result.push_back(Mesh::VertexAttribute::UV);
        }
        if ((definedAttributes & Mesh::VertexAttribute::UV2) == static_cast<Mesh::VertexAttributeFlags>(Mesh::VertexAttribute::UV2))
        {
            result.push_back(Mesh::VertexAttribute::UV2);
        }
        if ((definedAttributes & Mesh::VertexAttribute::UV3) == static_cast<Mesh::VertexAttributeFlags>(Mesh::VertexAttribute::UV3))
        {
            result.push_back(Mesh::VertexAttribute::UV3);
        }
        if ((definedAttributes & Mesh::VertexAttribute::UV4) == static_cast<Mesh::VertexAttributeFlags>(Mesh::VertexAttribute::UV4))
        {
            result.push_back(Mesh::VertexAttribute::UV4);
        }
        return result;
    }

    slag::VertexDescription Mesh::createVertexDescription(VertexAttributeFlags attributes)
    {
        slag::VertexDescription vertexDescription(std::popcount((uint16_t)attributes));
        auto channel = 0;
        if ((attributes & Mesh::VertexAttribute::POSITION) == (Mesh::VertexAttributeFlags)Mesh::VertexAttribute::POSITION)
        {
            vertexDescription.add(slag::VertexAttribute(slag::GraphicsType::VECTOR3,0),channel);
            channel++;
        }
        if ((attributes & Mesh::VertexAttribute::NORMAL) == (Mesh::VertexAttributeFlags)Mesh::VertexAttribute::NORMAL)
        {
            vertexDescription.add(slag::VertexAttribute(slag::GraphicsType::VECTOR3,0),channel);
            channel++;
        }
        if ((attributes & Mesh::VertexAttribute::TANGENT) == (Mesh::VertexAttributeFlags)Mesh::VertexAttribute::TANGENT)
        {
            vertexDescription.add(slag::VertexAttribute(slag::GraphicsType::VECTOR3,0),channel);
            channel++;
        }
        if ((attributes & Mesh::VertexAttribute::COLOR) == (Mesh::VertexAttributeFlags)Mesh::VertexAttribute::COLOR)
        {
            vertexDescription.add(slag::VertexAttribute(slag::GraphicsType::BOOLEAN_VECTOR4,0),channel);
            channel++;
        }
        if ((attributes & Mesh::VertexAttribute::BONE_WEIGHT) == (Mesh::VertexAttributeFlags)Mesh::VertexAttribute::BONE_WEIGHT)
        {
            auto offset = 0;
            for (auto i=0; i< 4; i++)
            {
                vertexDescription.add(slag::VertexAttribute(slag::GraphicsType::UNSIGNED_INTEGER,offset),channel);
                offset+=sizeof(uint32_t);
                vertexDescription.add(slag::VertexAttribute(slag::GraphicsType::FLOAT,offset),channel);
                offset+=sizeof(float);
            }

            channel++;
        }
        if ((attributes & Mesh::VertexAttribute::UV) == (Mesh::VertexAttributeFlags)Mesh::VertexAttribute::UV)
        {
            vertexDescription.add(slag::VertexAttribute(slag::GraphicsType::VECTOR2,0),channel);
            channel++;
        }
        if ((attributes & Mesh::VertexAttribute::UV2) == (Mesh::VertexAttributeFlags)Mesh::VertexAttribute::UV2)
        {
            vertexDescription.add(slag::VertexAttribute(slag::GraphicsType::VECTOR2,0),channel);
            channel++;
        }
        if ((attributes & Mesh::VertexAttribute::UV3) == (Mesh::VertexAttributeFlags)Mesh::VertexAttribute::UV3)
        {
            vertexDescription.add(slag::VertexAttribute(slag::GraphicsType::VECTOR2,0),channel);
            channel++;
        }
        if ((attributes & Mesh::VertexAttribute::UV4) == (Mesh::VertexAttributeFlags)Mesh::VertexAttribute::UV4)
        {
            vertexDescription.add(slag::VertexAttribute(slag::GraphicsType::VECTOR2,0),channel);
            channel++;
        }
        return vertexDescription;
    }

    void Mesh::move(Mesh& from)
    {
        std::swap(_positionBuffer,from._positionBuffer);
        std::swap(_normalBuffer,from._normalBuffer);
        std::swap(_tangentBuffer,from._tangentBuffer);
        std::swap(_colorBuffer,from._colorBuffer);
        std::swap(_boneWeightBuffer,from._boneWeightBuffer);
        std::swap(_uvBuffer,from._uvBuffer);
        std::swap(_uvBuffer2,from._uvBuffer2);
        std::swap(_uvBuffer3,from._uvBuffer3);
        std::swap(_uvBuffer4,from._uvBuffer4);
        std::swap(_indexBuffer,from._indexBuffer);
        _vertexCount = from._vertexCount;
        _indexSize = from._indexSize;
        _attributeFlags=from._attributeFlags;
    }

    slag::Buffer*& Mesh::attributeBuffer(int attributeIndex)
    {
        return *((&_positionBuffer)+attributeIndex);
    }
} // crucible