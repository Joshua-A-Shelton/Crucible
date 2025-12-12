#ifndef CRUCIBLE_DEFERREDJOBQUEUE_H
#define CRUCIBLE_DEFERREDJOBQUEUE_H
#include <slag/Slag.h>
#include "scripting/ManagedInstance.h"
#include <mutex>
#include <queue>

namespace crucible
{
    class DeferredJob
    {
    public:
        DeferredJob(std::vector<slag::Buffer*>&& buffers);
        DeferredJob(std::vector<slag::Texture*>&& textures);
        DeferredJob(std::vector<slag::Buffer*>&& buffers,std::vector<slag::Texture*>&& textures);
        DeferredJob(std::vector<slag::Buffer*>&& buffers, scripting::ManagedInstance&& instance);
        DeferredJob(std::vector<slag::Texture*>&& textures, scripting::ManagedInstance&& instance);
        DeferredJob(std::vector<slag::Buffer*>&& buffers,std::vector<slag::Texture*>&& textures, scripting::ManagedInstance&& instance);
        DeferredJob(scripting::ManagedInstance&& instance);
        ~DeferredJob();
        DeferredJob(const DeferredJob&) = delete;
        DeferredJob& operator=(const DeferredJob&) = delete;
        DeferredJob(DeferredJob&& from);
        DeferredJob& operator=(DeferredJob&& from);
        void execute();

    private:
        void move(DeferredJob& from);
        std::vector<slag::Buffer*> _buffers;
        std::vector<slag::Texture*> _textures;
        scripting::ManagedInstance _IDeferredInitInstance{};
    };

    class DeferredJobQueue
    {
    public:
        DeferredJobQueue();
        ~DeferredJobQueue();
        DeferredJobQueue(const DeferredJobQueue&) = delete;
        DeferredJobQueue& operator=(const DeferredJobQueue&) = delete;
        DeferredJobQueue(DeferredJobQueue&& from);
        DeferredJobQueue& operator=(DeferredJobQueue&& from);
        slag::ICommandBuffer* commandBuffer();
        void enqueue(DeferredJob&& job);
        void process();
        static bool systemInitialize();
        static void systemCleanup();
    private:
        std::mutex _commandBufferMutex;
        slag::CommandBuffer* _commandBuffer = slag::CommandBuffer::newCommandBuffer(slag::GPUQueue::QueueType::TRANSFER);
        slag::Semaphore* _finished = slag::Semaphore::newSemaphore(0);
        std::queue<DeferredJob> _deferredJobs;
    };
} // crucible

#endif //CRUCIBLE_DEFERREDJOBQUEUE_H
