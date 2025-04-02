#include "VirtualUniformBuffer.h"

namespace crucible
{
    namespace core
    {
        VirtualUniformBuffer::VirtualUniformBuffer(size_t initialSize)
        {
            _uniformBuffers.push_back(slag::Buffer::newBuffer(initialSize,slag::Buffer::CPU_AND_GPU,slag::Buffer::UNIFORM_BUFFER));
            _currentCapacity = initialSize;
            _minSize = initialSize;
        }

        VirtualUniformBuffer::~VirtualUniformBuffer()
        {
            for (auto buffer : _uniformBuffers)
            {
                delete buffer;
            }
        }

        VirtualUniformBuffer::VirtualUniformBuffer(VirtualUniformBuffer&& from)
        {
            move(from);
        }

        VirtualUniformBuffer& VirtualUniformBuffer::operator=(VirtualUniformBuffer&& from)
        {
            move(from);
            return *this;
        }

        void VirtualUniformBuffer::reset()
        {
            if (_currentUsage == 0)
            {
                return;
            }
            if (_uniformBuffers.size() > 1)
            {
                for (auto buffer : _uniformBuffers)
                {
                    delete buffer;
                }
                _uniformBuffers.clear();
                _uniformBuffers.push_back(slag::Buffer::newBuffer(_currentCapacity,slag::Buffer::CPU_AND_GPU,slag::Buffer::UNIFORM_BUFFER));
            }
            else if (_currentUsage < _currentCapacity/2 && _currentCapacity > _minSize)
            {
                for (auto buffer : _uniformBuffers)
                {
                    delete buffer;
                }
                _uniformBuffers.clear();
                _currentCapacity = std::max((size_t)(_currentCapacity/1.5),_minSize);
                _uniformBuffers.push_back(slag::Buffer::newBuffer(_currentCapacity,slag::Buffer::CPU_AND_GPU,slag::Buffer::UNIFORM_BUFFER));
            }
            _currentUsage = 0;
            _currentBuffer = 0;
            _currentOffset = 0;
        }

        UniformWriteLocation VirtualUniformBuffer::write(void* data, size_t size)
        {
            if (_currentOffset + size > _currentCapacity)
            {
                size_t newBufferSize = std::max(_currentCapacity/2,size);
                _uniformBuffers.push_back(slag::Buffer::newBuffer(newBufferSize,slag::Buffer::CPU_AND_GPU,slag::Buffer::UNIFORM_BUFFER));
                _currentBuffer++;
                _currentOffset = 0;
                //just set to zero and recalculate it
                _currentUsage = 0;
                for (auto buffer : _uniformBuffers)
                {
                    _currentUsage += buffer->size();
                }
            }
            auto buffer = _uniformBuffers[_currentBuffer];
            buffer->update(_currentOffset,data,size);

            size_t updateSize = 0;
            if (size < 64)
            {
                updateSize = 64;
            }
            else
            {
                updateSize = size + (size%64);
            }
            _currentUsage += updateSize;
            UniformWriteLocation writeLocation{.buffer = buffer,.offset = _currentOffset,.length = size};
            _currentOffset += updateSize;
            return writeLocation;
        }

        void VirtualUniformBuffer::move(VirtualUniformBuffer& from)
        {
            _uniformBuffers.swap(from._uniformBuffers);
            _currentBuffer= from._currentBuffer;
            _currentOffset = from._currentOffset;
            _currentCapacity = from._currentCapacity;
            _currentUsage = from._currentUsage;
        }
    } // core
} // crucible
