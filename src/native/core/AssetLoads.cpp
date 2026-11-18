// Loading assets on background jobs (ADR 0099): each load's read and prepare
// on a Background job, its publish on the main thread.

#include <crucible/AssetLoads.h>
#include <crucible/Jobs.h>
#include <crucible/Memory.h>
#include <crucible/Systems.h>
#include <crucible/Vector.h>

#include <mutex>
#include <thread>

namespace crucible::assets {

namespace {

constexpr MemoryTag tag = MemoryTag::Assets;

struct Load
{
    LoadTask task;
    jobs::JobCounter counter;
    Vector<uint8_t> bytes{tag};
    bool found = false;
    bool read = false; // read already, on the main thread
};

Vector<Load*>& pending()
{
    static Vector<Load*> loads(tag);
    return loads;
}

std::mutex startersLock;

Vector<LoadStarter>& starters()
{
    static Vector<LoadStarter> registered(tag);
    return registered;
}

void readInto(Load& load)
{
    auto deliver = [](void* context, const uint8_t* data, uint64_t size) {
        static_cast<Vector<uint8_t>*>(context)->assign(std::span<const uint8_t>(data, static_cast<size_t>(size)));
    };
    load.found = assetSource().read(&load.task.id, &load.bytes, deliver) == 0;
    load.read = true;
}

void prepare(void* data)
{
    Load& load = *static_cast<Load*>(data);
    if (!load.read)
    {
        readInto(load);
    }
    load.task.prepare(load.task.context, load.bytes.span(), load.found);
    load.bytes = Vector<uint8_t>(tag); // freed now, not at publish
}

bool finished(Load& load, bool wait)
{
    return load.task.finished == nullptr || load.task.finished(load.task.context, wait);
}

void publish(Load* load)
{
    load->task.publish(load->task.context);
    destroy(tag, load);
}

void publishSystem(const loop::SystemContext&, void*)
{
    publishLoads();
}

} // namespace

void startLoad(const LoadTask& task)
{
    Load* load = create<Load>(tag);
    load->task = task;
    if (jobs::workerCount() == 0)
    {
        prepare(load);
        finished(*load, true);
        publish(load);
        return;
    }
    if (!assetSource().threadSafe)
    {
        readInto(*load);
    }
    const jobs::Job job{prepare, load};
    jobs::run({&job, 1}, jobs::JobPriority::Background, load->counter);
    pending().pushBack(load);
}

void publishLoads()
{
    // Publishing can start loads (a model's textures): those wait for the next call.
    Vector<Load*>& loads = pending();
    const size_t count = loads.size();
    size_t kept = 0;
    Vector<Load*> done(tag);
    for (size_t i = 0; i < count; ++i)
    {
        Load* load = loads[i];
        if (jobs::isDone(load->counter) && finished(*load, false))
        {
            done.pushBack(load);
        }
        else
        {
            loads[kept++] = load;
        }
    }
    for (size_t i = count; i < loads.size(); ++i)
    {
        loads[kept++] = loads[i];
    }
    loads.resize(kept);
    for (Load* load : done)
    {
        publish(load);
    }
}

uint32_t pendingLoads()
{
    return static_cast<uint32_t>(pending().size());
}

void waitForLoads()
{
    while (!pending().empty())
    {
        Load* first = pending()[0];
        jobs::wait(first->counter);
        finished(*first, true);
        publishLoads();
        if (!pending().empty() && pending()[0] == first)
        {
            std::this_thread::yield();
        }
    }
}

void registerLoadStarter(LoadStarter starter)
{
    std::lock_guard lock(startersLock);
    for (LoadStarter registered : starters())
    {
        if (registered == starter)
        {
            return;
        }
    }
    starters().pushBack(starter);
}

uint32_t startLoads(ecs::World& world)
{
    Vector<LoadStarter> each(tag);
    {
        std::lock_guard lock(startersLock);
        each.assign(starters().span());
    }
    uint32_t waiting = 0;
    for (LoadStarter starter : each)
    {
        waiting += starter(world);
    }
    return waiting;
}

void registerAssetLoads()
{
    loop::registerSystem({.name = publishSystemName, .phase = "BeginFrame", .function = publishSystem});
}

} // namespace crucible::assets
