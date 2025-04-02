#ifndef CRUCIBLE_VIRTUALUNIFORMBUFFER_H
#define CRUCIBLE_VIRTUALUNIFORMBUFFER_H
#include <vector>
#include "slag/Buffer.h"

namespace crucible
{
    namespace core
    {
        struct UniformWriteLocation
        {
            slag::Buffer* buffer = nullptr;
            size_t offset = 0;
            size_t length = 0;
        };
        class VirtualUniformBuffer
        {
        public:
            VirtualUniformBuffer()=delete;
            VirtualUniformBuffer(size_t initialSize);
            ~VirtualUniformBuffer();
            VirtualUniformBuffer(const VirtualUniformBuffer&) = delete;
            VirtualUniformBuffer& operator=(const VirtualUniformBuffer&) = delete;
            VirtualUniformBuffer(VirtualUniformBuffer&& from);
            VirtualUniformBuffer& operator=(VirtualUniformBuffer&& from);
            void reset();
            UniformWriteLocation write(void* data, size_t size);
            size_t capacity() const;
            size_t usage() const;
        private:
            void move(VirtualUniformBuffer& from);
            std::vector<slag::Buffer*> _uniformBuffers;
            size_t _minSize = 0;
            size_t _currentBuffer=0;
            size_t _currentOffset=0;
            size_t _currentCapacity=0;
            size_t _currentUsage=0;
        };
    } // core
} // crucible

#endif //CRUCIBLE_VIRTUALUNIFORMBUFFER_H
