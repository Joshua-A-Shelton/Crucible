#include <crucible/Systems.h>
#include <crucible/Vector.h>

#include <atomic>
#include <mutex>

namespace crucible::loop {

namespace {

Vector<char> copyOf(std::string_view text)
{
    Vector<char> copy(MemoryTag::General);
    copy.assign(std::span<const char>(text.data(), text.size()));
    return copy;
}

std::string_view viewOf(const Vector<char>& text)
{
    return {text.data(), text.size()};
}

struct SystemRecord
{
    Vector<char> name{MemoryTag::General};
    Vector<char> phase{MemoryTag::General};
    SystemFunction function = nullptr;
    void* userData = nullptr;
    Vector<Vector<char>> before{MemoryTag::General};
    Vector<Vector<char>> after{MemoryTag::General};
};

struct Registry
{
    std::mutex mutex;
    Vector<SystemRecord> systems{MemoryTag::General};
};

Registry& registry()
{
    static Registry instance;
    return instance;
}

// The caller holds the lock.
SystemRecord* find(Registry& systems, std::string_view name)
{
    for (SystemRecord& system : systems.systems)
    {
        if (viewOf(system.name) == name)
        {
            return &system;
        }
    }
    return nullptr;
}

const SystemRecord& at(uint32_t index)
{
    return registry().systems[index];
}

} // namespace

void registerSystem(const SystemDesc& desc)
{
    Registry& systems = registry();
    std::lock_guard lock(systems.mutex);
    SystemRecord* record = find(systems, desc.name);
    if (record == nullptr)
    {
        record = &systems.systems.emplaceBack();
    }
    record->name = copyOf(desc.name);
    record->phase = copyOf(desc.phase);
    record->function = desc.function;
    record->userData = desc.userData;
    record->before.clear();
    record->after.clear();
    for (const std::string_view name : desc.before)
    {
        record->before.pushBack(copyOf(name));
    }
    for (const std::string_view name : desc.after)
    {
        record->after.pushBack(copyOf(name));
    }
}

void unregisterSystem(std::string_view name)
{
    Registry& systems = registry();
    std::lock_guard lock(systems.mutex);
    for (size_t i = 0; i < systems.systems.size(); ++i)
    {
        if (viewOf(systems.systems[i].name) == name)
        {
            systems.systems.removeSwap(i);
            return;
        }
    }
}

// The readers take no lock: the C# builder reads on one thread, after
// registration (see the header).

uint32_t nativeSystemCount()
{
    return static_cast<uint32_t>(registry().systems.size());
}

std::string_view nativeSystemName(uint32_t index)
{
    return viewOf(at(index).name);
}

std::string_view nativeSystemPhase(uint32_t index)
{
    return viewOf(at(index).phase);
}

uint32_t nativeSystemConstraintCount(uint32_t index, bool before)
{
    const SystemRecord& system = at(index);
    return static_cast<uint32_t>((before ? system.before : system.after).size());
}

std::string_view nativeSystemConstraint(uint32_t index, bool before, uint32_t constraint)
{
    const SystemRecord& system = at(index);
    return viewOf((before ? system.before : system.after)[constraint]);
}

uint64_t nativeSystemFunction(uint32_t index)
{
    return reinterpret_cast<uint64_t>(at(index).function);
}

void* nativeSystemUserData(uint32_t index)
{
    return at(index).userData;
}

// ---------------------------------------------------------------------------
// Test probe
// ---------------------------------------------------------------------------

namespace {

std::atomic<uint64_t> probeCalls{0};
SystemContext lastContext{};

void countSystem(const SystemContext& context, void* userData)
{
    static_cast<std::atomic<uint64_t>*>(userData)->fetch_add(1);
    lastContext = context;
}

void spawnSystem(const SystemContext& context, void*)
{
    if (context.commands != nullptr)
    {
        ecs::recordCreate(*context.commands);
    }
}

} // namespace

void registerProbeSystems(std::string_view phase)
{
    probeCalls = 0;
    lastContext = {};
    registerSystem({.name = "crucible.probe.Count", .phase = phase, .function = countSystem, .userData = &probeCalls});
    const std::string_view after[] = {"crucible.probe.Count"};
    registerSystem({.name = "crucible.probe.Spawn", .phase = phase, .function = spawnSystem, .after = after});
}

void unregisterProbeSystems()
{
    unregisterSystem("crucible.probe.Count");
    unregisterSystem("crucible.probe.Spawn");
}

uint64_t probeSystemCalls()
{
    return probeCalls.load();
}

SystemContext probeLastContext()
{
    return lastContext;
}

} // namespace crucible::loop
