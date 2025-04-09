#include "VirtualBuffer.h"

namespace crucible
{
    namespace core
    {
        VirtualBuffer::VirtualBuffer(size_t initialSize, slag::Buffer::Usage usage)
        {
            if (usage & slag::Buffer::UNIFORM_BUFFER)
            {
                _alignment = 64;
            }
            else
            {
                _alignment = 1;
            }

            _minSize = 64;
            auto offAlign = _minSize%_alignment;
            _minSize+=offAlign;

            if (initialSize < _minSize)
            {
                _currentCapacity = _minSize;
            }
            else
            {
                auto offAlign = (initialSize%_alignment);
                if (offAlign)
                {
                    offAlign = _alignment-offAlign;
                }
                _currentCapacity = initialSize + offAlign;
            }
            _usage = usage;
            _uniformBuffers.push_back(slag::Buffer::newBuffer(_currentCapacity,slag::Buffer::CPU_AND_GPU,usage));

        }

        VirtualBuffer::~VirtualBuffer()
        {
            for (auto buffer : _uniformBuffers)
            {
                delete buffer;
            }
        }

        VirtualBuffer::VirtualBuffer(VirtualBuffer&& from)
        {
            move(from);
        }

        VirtualBuffer& VirtualBuffer::operator=(VirtualBuffer&& from)
        {
            move(from);
            return *this;
        }

        void VirtualBuffer::reset()
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
                _uniformBuffers.push_back(slag::Buffer::newBuffer(_currentCapacity+(_currentCapacity%_alignment),slag::Buffer::CPU_AND_GPU,_usage));
            }
            else if (_currentUsage < _currentCapacity/2 && _currentCapacity > _minSize)
            {
                for (auto buffer : _uniformBuffers)
                {
                    delete buffer;
                }
                _uniformBuffers.clear();
                _currentCapacity = std::max((size_t)(_currentUsage*1.5),_minSize);
                _currentCapacity = _currentCapacity + (_currentCapacity%_alignment);
                _uniformBuffers.push_back(slag::Buffer::newBuffer(_currentCapacity,slag::Buffer::CPU_AND_GPU,_usage));
            }
            _currentUsage = 0;
            _currentBuffer = 0;
            _currentOffset = 0;
        }

        BufferWriteLocation VirtualBuffer::write(void* data, size_t size)
        {
            if (_currentOffset + size > _uniformBuffers[_currentBuffer]->size())
            {
                size_t newBufferSize = std::max(_currentCapacity/2,size);
                newBufferSize = newBufferSize + (newBufferSize % _alignment);
                _uniformBuffers.push_back(slag::Buffer::newBuffer(newBufferSize,slag::Buffer::CPU_AND_GPU,_usage));
                _currentBuffer++;
                _currentCapacity+=newBufferSize;
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
            if (size < _alignment)
            {
                updateSize = _alignment;
            }
            else if (_alignment!=0)
            {
                updateSize = size + (size%_alignment);
            }
            else
            {
                updateSize = size;
            }
            _currentUsage += updateSize;
            BufferWriteLocation writeLocation{.buffer = buffer,.offset = _currentOffset,.length = size};
            _currentOffset += updateSize;
            return writeLocation;
        }

        size_t VirtualBuffer::capacity() const
        {
            return _currentCapacity;
        }

        size_t VirtualBuffer::usage() const
        {
            //we can overflow the usage because we increase usage by multiples of alignment, not just the last write size
            return std::min(_currentUsage,_currentCapacity);
        }

        void VirtualBuffer::move(VirtualBuffer& from)
        {
            _uniformBuffers.swap(from._uniformBuffers);
            _currentBuffer= from._currentBuffer;
            _currentOffset = from._currentOffset;
            _currentCapacity = from._currentCapacity;
            _currentUsage = from._currentUsage;
            _alignment = from._alignment;
            _usage = from._usage;
        }
    } // core
} // crucible
