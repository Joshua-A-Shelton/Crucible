#include "DeferredJobQueue.h"

#include "scripting/BindingFlags.h"
#include "scripting/ScriptingEngine.h"

namespace crucible
{
    scripting::ManagedFunctionDelegate<void,void*>* DEFERRED_JOB_QUEUE_callDeferredInitialize = nullptr;

    DeferredJob::DeferredJob(std::vector<slag::Buffer*>&& buffers)
    {
        _buffers.swap(buffers);
    }

    DeferredJob::DeferredJob(std::vector<slag::Texture*>&& textures)
    {
        _textures.swap(textures);
    }

    DeferredJob::DeferredJob(std::vector<slag::Buffer*>&& buffers, std::vector<slag::Texture*>&& textures)
    {
        _buffers.swap(buffers);
        _textures.swap(textures);
    }

    DeferredJob::DeferredJob(std::vector<slag::Buffer*>&& buffers, scripting::ManagedInstance&& instance)
    {
        _buffers.swap(buffers);
        _IDeferredInitInstance = std::move(instance);
    }

    DeferredJob::DeferredJob(std::vector<slag::Texture*>&& textures, scripting::ManagedInstance&& instance)
    {
        _textures.swap(textures);
        _IDeferredInitInstance = std::move(instance);
    }

    DeferredJob::DeferredJob(std::vector<slag::Buffer*>&& buffers, std::vector<slag::Texture*>&& textures,scripting::ManagedInstance&& instance)
    {
        _buffers.swap(buffers);
        _textures.swap(textures);
        _IDeferredInitInstance = std::move(instance);
    }

    DeferredJob::~DeferredJob()
    {
        for (auto buffer : _buffers)
        {
            delete buffer;
        }
        for (auto texture : _textures)
        {
            delete texture;
        }
    }

    DeferredJob::DeferredJob(DeferredJob&& from)
    {
        move(from);
    }

    DeferredJob& DeferredJob::operator=(DeferredJob&& from)
    {
        move(from);
        return *this;
    }

    void DeferredJob::move(DeferredJob& from)
    {
        _buffers.swap(from._buffers);
        _textures.swap(from._textures);
        _IDeferredInitInstance = std::move(from._IDeferredInitInstance);
    }

    DeferredJobQueue::DeferredJobQueue()
    {
        _commandBuffer->begin();
    }

    DeferredJobQueue::~DeferredJobQueue()
    {
        if (!_deferredJobs.empty())
        {
            process();
        }
        delete _commandBuffer;
        delete _finished;
    }

    slag::ICommandBuffer* DeferredJobQueue::commandBuffer()
    {
        std::lock_guard<std::mutex> lock(_commandBufferMutex);
        return _commandBuffer;
    }

    void DeferredJobQueue::enqueue(DeferredJob&& job)
    {
        _deferredJobs.push(std::move(job));
    }

    void DeferredJobQueue::process()
    {
        if (_deferredJobs.empty())
        {
            return;
        }
        std::lock_guard<std::mutex> lock(_commandBufferMutex);
        _commandBuffer->end();

        slag::SemaphoreValue signal{.semaphore = _finished, .value = 1};
        slag::QueueSubmissionBatch submissionBatch
        {
            .waitSemaphores = nullptr,
            .waitSemaphoreCount = 0,
            .commandBuffers = &_commandBuffer,
            .commandBufferCount = 1,
            .signalSemaphores = &signal,
            .signalSemaphoreCount = 1,
        };
        slag::slagGraphicsCard()->transferQueue()->submit(&submissionBatch,1);
        _finished->waitForValue(1);
        delete _finished;
        _finished = slag::Semaphore::newSemaphore(0);
        _commandBuffer->begin();

    }

    bool DeferredJobQueue::systemInitialize()
    {
        auto type = scripting::ScriptingEngine::getManagedType("Crucible.Core.IDeferredInit");
        auto IntPtrType = scripting::ScriptingEngine::getManagedType("System.IntPtr");
        DEFERRED_JOB_QUEUE_callDeferredInitialize = new scripting::ManagedFunctionDelegate<void,void*>( scripting::ScriptingEngine::getManagedFunctionDelegate<void,void*>(type,"CallDeferredInitialize",scripting::BindingFlags::STATIC | scripting::BindingFlags::NON_PUBLIC,&IntPtrType,1));
        if (DEFERRED_JOB_QUEUE_callDeferredInitialize->rawFunctionPointer() == nullptr)
        {
            delete DEFERRED_JOB_QUEUE_callDeferredInitialize;
            DEFERRED_JOB_QUEUE_callDeferredInitialize = nullptr;
            return false;
        }
        return true;
    }

    void DeferredJobQueue::systemCleanup()
    {
        delete DEFERRED_JOB_QUEUE_callDeferredInitialize;
        DEFERRED_JOB_QUEUE_callDeferredInitialize = nullptr;
    }
} // crucible
