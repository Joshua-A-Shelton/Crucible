#include "GPUUtils.h"

#include <cstring>
#include <slag/Slag.h>

namespace crucible
{
    namespace utils
    {
        void copyBufferToArray(slag::Buffer* buffer, void* array)
        {
            if (buffer->accessibility() == slag::Buffer::Accessibility::CPU_AND_GPU)
            {
                memcpy(array,buffer->cpuHandle(),buffer->size());
            }
            else
            {
                slag::CommandBuffer* commandBuffer = slag::CommandBuffer::newCommandBuffer(slag::GPUQueue::QueueType::TRANSFER);
                slag::Semaphore* finished = slag::Semaphore::newSemaphore(0);
                slag::Buffer* tempBuffer = slag::Buffer::newBuffer(buffer->size(),slag::Buffer::Accessibility::CPU_AND_GPU);
                commandBuffer->begin();
                commandBuffer->copyBufferToBuffer(buffer,0,buffer->size(),tempBuffer,0);
                commandBuffer->end();
                slag::SemaphoreValue signal{.semaphore = finished, .value = 1};
                slag::QueueSubmissionBatch batch
                {
                    .waitSemaphores = nullptr,
                    .waitSemaphoreCount = 0,
                    .commandBuffers = &commandBuffer,
                    .commandBufferCount = 1,
                    .signalSemaphores = &signal,
                    .signalSemaphoreCount = 1,
                };
                slag::slagGraphicsCard()->transferQueue()->submit(&batch,1);
                finished->waitForValue(1);
                delete commandBuffer;
                delete finished;
                memcpy(array,tempBuffer->cpuHandle(),buffer->size());
                delete tempBuffer;
            }
        }
    } // utils
} // crucible
