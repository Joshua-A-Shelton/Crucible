#include "MeshRenderer.h"

#include <cstring>

namespace crucible
{
    namespace core
    {
        MeshRenderer::MeshRenderer(unsigned char* meshData, uint64_t meshDataLength, const ShaderReference& shader, uint8_t priority): _shader(shader)
        {
            _priority = priority;
            _mesh = new Mesh(meshData, meshDataLength);
            //need to get buffer size of the descriptor group I'm setting
            //also need to double it to allow for buffering
            auto description = _shader.pipeline()->uniformBufferLayout(2,0);
            if (!description)
            {
                throw std::runtime_error("Shader is in the wrong format for mesh rendering");
            }
            _rawData = new char[description->size()];
            _dataBuffer = slag::Buffer::newBuffer(description->size()*2,slag::Buffer::CPU_AND_GPU,slag::Buffer::DATA_BUFFER);
        }

        MeshRenderer::~MeshRenderer()
        {
            if(_mesh)
            {
                delete _mesh;
                delete _dataBuffer;
                delete[] _rawData;
            }
        }

        MeshRenderer::MeshRenderer(MeshRenderer&& from): _shader(std::move(from._shader))
        {
            _priority = from._priority;
            std::swap(_mesh,from._mesh);
            std::swap(_shader,from._shader);
            std::swap(_rawData,from._rawData);
            _rawDataLength = from._rawDataLength;
        }

        MeshRenderer& MeshRenderer::operator=(MeshRenderer&& from)
        {
            _shader = std::move(from._shader);
            _priority = from._priority;
            std::swap(_mesh,from._mesh);
            std::swap(_shader,from._shader);
            std::swap(_rawData,from._rawData);
            _rawDataLength = from._rawDataLength;

            return *this;
        }

        uint8_t MeshRenderer::priority()
        {
            return _priority;
        }

        Mesh* MeshRenderer::mesh() const
        {
            return _mesh;
        }

        ShaderReference& MeshRenderer::shader()
        {
            return _shader;
        }

        void MeshRenderer::setData(uint32_t offset, void* data, uint32_t length)
        {
            if (_rawDataLength<offset+length)
            {
                memcpy(_rawData+offset, data, length);
            }
            else
            {
                throw std::runtime_error("Attempted to write out of bounds for data");
            }
        }

        DrawDescriptorData MeshRenderer::getDrawDescriptorDataAndFlipBufferSides()
        {
            auto halfSize = _dataBuffer->size()/2;
            DrawDescriptorData drawData
            {
                .buffer = _dataBuffer,
                .offset = _firstHalf? 0 : halfSize,
                .length = halfSize,
            };
            _dataBuffer->update(drawData.offset,_rawData,drawData.length);
            _firstHalf = !_firstHalf;
            return drawData;
        }
    } // core
} // crucible
