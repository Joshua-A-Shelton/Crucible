#ifndef CRUCIBLE_VIRTUALUNIFORMBUFFER_H
#define CRUCIBLE_VIRTUALUNIFORMBUFFER_H
#include <vector>
#include "slag/Buffer.h"

namespace crucible
{
    namespace core
    {
        struct BufferWriteLocation
        {
            slag::Buffer* buffer = nullptr;
            size_t offset = 0;
            size_t length = 0;
        };
        class VirtualBuffer
        {
        public:
            VirtualBuffer()=delete;
            VirtualBuffer(size_t initialSize, slag::Buffer::Usage usage);
            ~VirtualBuffer();
            VirtualBuffer(const VirtualBuffer&) = delete;
            VirtualBuffer& operator=(const VirtualBuffer&) = delete;
            VirtualBuffer(VirtualBuffer&& from);
            VirtualBuffer& operator=(VirtualBuffer&& from);
            void reset();
            BufferWriteLocation write(void* data, size_t size);
            size_t capacity() const;
            size_t usage() const;
        private:
            void move(VirtualBuffer& from);
            std::vector<slag::Buffer*> _uniformBuffers;
            size_t _minSize = 0;
            size_t _currentBuffer=0;
            size_t _currentOffset=0;
            size_t _currentCapacity=0;
            size_t _currentUsage=0;
            size_t _alignment=0;
            slag::Buffer::Usage _usage;
        };
    } // core
} // crucible

#endif //CRUCIBLE_VIRTUALUNIFORMBUFFER_H
