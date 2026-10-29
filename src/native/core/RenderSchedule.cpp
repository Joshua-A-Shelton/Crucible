#include "RenderInternal.h"

#include <crucible/Memory.h>
#include <crucible/RenderSchedule.h>
#include <crucible/Vector.h>

#include <cstdio>
#include <cstring>

namespace crucible::render {

namespace {

// Registered keys' names, back to back.
struct KeyRegistry
{
    Vector<char> names{MemoryTag::Render};
    Vector<uint32_t> ends{MemoryTag::Render};

    std::string_view name(uint32_t index) const
    {
        const uint32_t begin = index == 0 ? 0 : ends[index - 1];
        return {names.data() + begin, ends[index] - begin};
    }
};

KeyRegistry& keys()
{
    static KeyRegistry registry;
    return registry;
}

} // namespace

// One frame's blackboard and the step running.
struct FrameState
{
    enum class Kind : uint8_t
    {
        None,
        Texture,
        Buffer,
    };
    struct Entry
    {
        Kind kind = Kind::None;
        uint32_t index = UINT32_MAX;
    };
    Vector<Entry> entries{MemoryTag::Render};
    std::string_view step;
    bool failed = false;
};

BlackboardKey registerBlackboardKey(std::string_view name)
{
    KeyRegistry& registry = keys();
    for (uint32_t i = 0; i < registry.ends.size(); ++i)
    {
        if (registry.name(i) == name)
        {
            return {i};
        }
    }
    for (char c : name)
    {
        registry.names.pushBack(c);
    }
    registry.ends.pushBack(static_cast<uint32_t>(registry.names.size()));
    return {static_cast<uint32_t>(registry.ends.size() - 1)};
}

std::string_view blackboardKeyName(BlackboardKey key)
{
    return key.value < keys().ends.size() ? keys().name(key.value) : std::string_view();
}

BlackboardKey backbufferKey()
{
    static const BlackboardKey key = registerBlackboardKey("Backbuffer");
    return key;
}

// ---------------------------------------------------------------------------
// RenderStepContext
// ---------------------------------------------------------------------------

namespace {

FrameState::Entry* slot(void* frameState, BlackboardKey key)
{
    FrameState& state = *static_cast<FrameState*>(frameState);
    if (key.value >= keys().ends.size())
    {
        return nullptr;
    }
    if (state.entries.size() <= key.value)
    {
        state.entries.resize(keys().ends.size());
    }
    return &state.entries[key.value];
}

void reportMissing(FrameState& state, BlackboardKey key, const char* kind)
{
    const std::string_view name = blackboardKeyName(key);
    std::fprintf(stderr,
                 "crucible: render step %.*s reads the %s %.*s from the blackboard, which no earlier step "
                 "published\n",
                 static_cast<int>(state.step.size()), state.step.data(), kind, static_cast<int>(name.size()),
                 name.data());
    state.failed = true;
}

} // namespace

void RenderStepContext::publish(BlackboardKey key, slag::rendergraph::RenderGraphTexture texture)
{
    if (FrameState::Entry* entry = slot(frameState, key))
    {
        *entry = {FrameState::Kind::Texture, texture.index};
    }
}

void RenderStepContext::publish(BlackboardKey key, slag::rendergraph::RenderGraphBuffer buffer)
{
    if (FrameState::Entry* entry = slot(frameState, key))
    {
        *entry = {FrameState::Kind::Buffer, buffer.index};
    }
}

slag::rendergraph::RenderGraphTexture RenderStepContext::texture(BlackboardKey key)
{
    const FrameState::Entry* entry = slot(frameState, key);
    if (entry == nullptr || entry->kind != FrameState::Kind::Texture)
    {
        reportMissing(*static_cast<FrameState*>(frameState), key, "texture");
        return {};
    }
    return {entry->index};
}

slag::rendergraph::RenderGraphBuffer RenderStepContext::buffer(BlackboardKey key)
{
    const FrameState::Entry* entry = slot(frameState, key);
    if (entry == nullptr || entry->kind != FrameState::Kind::Buffer)
    {
        reportMissing(*static_cast<FrameState*>(frameState), key, "buffer");
        return {};
    }
    return {entry->index};
}

std::string_view RenderStepContext::stepName() const
{
    return static_cast<const FrameState*>(frameState)->step;
}

// Runs every step for one frame, with the backbuffer published.
struct FrameBuilder
{
    static Result<void> run(const FrameInputs& inputs)
    {
        FrameState state;
        RenderStepContext context(inputs.graph, inputs.device);
        context.width = inputs.width;
        context.height = inputs.height;
        context.frame = inputs.frame;
        std::memcpy(context.clearColor, inputs.clearColor, sizeof(context.clearColor));
        context.frameState = &state;
        context.publish(backbufferKey(), inputs.backbuffer);
        const RenderSchedule& schedule = renderSchedule();
        for (uint32_t i = 0; i < schedule.stepCount() && !state.failed; ++i)
        {
            const RenderStep& step = schedule.step(i);
            state.step = schedule.stepName(i);
            step.addPasses(context, step.userData);
        }
        return state.failed ? Result<void>(Status::InvalidData) : Result<void>();
    }
};

Result<void> addStepPasses(const FrameInputs& inputs)
{
    return FrameBuilder::run(inputs);
}

// ---------------------------------------------------------------------------
// RenderSchedule
// ---------------------------------------------------------------------------

RenderSchedule::~RenderSchedule()
{
    for (Entry* entry : entries)
    {
        destroy(MemoryTag::Render, entry);
    }
}

int32_t RenderSchedule::find(std::string_view name) const
{
    for (uint32_t i = 0; i < entries.size(); ++i)
    {
        if (stepName(i) == name)
        {
            return static_cast<int32_t>(i);
        }
    }
    return -1;
}

Result<void> RenderSchedule::insert(uint32_t index, const RenderStep& step)
{
    if (step.name == nullptr || step.addPasses == nullptr || find(step.name) >= 0)
    {
        return Status::InvalidData;
    }
    if (device != nullptr && step.prepare != nullptr)
    {
        if (Result<void> prepared = step.prepare(*device, step.userData); !prepared)
        {
            return prepared;
        }
    }
    Entry* entry = create<Entry>(MemoryTag::Render);
    entry->name.assign(std::span<const char>(step.name, std::strlen(step.name) + 1));
    entry->step = step;
    entry->step.name = entry->name.data();
    entries.pushBack(nullptr);
    for (uint32_t i = static_cast<uint32_t>(entries.size()) - 1; i > index; --i)
    {
        entries[i] = entries[i - 1];
    }
    entries[index] = entry;
    return {};
}

Result<void> RenderSchedule::add(const RenderStep& step)
{
    return insert(static_cast<uint32_t>(entries.size()), step);
}

Result<void> RenderSchedule::insertBefore(std::string_view existing, const RenderStep& step)
{
    const int32_t at = find(existing);
    return at < 0 ? Result<void>(Status::NotFound) : insert(static_cast<uint32_t>(at), step);
}

Result<void> RenderSchedule::insertAfter(std::string_view existing, const RenderStep& step)
{
    const int32_t at = find(existing);
    return at < 0 ? Result<void>(Status::NotFound) : insert(static_cast<uint32_t>(at) + 1, step);
}

Result<void> RenderSchedule::replace(std::string_view name, const RenderStep& step)
{
    const int32_t at = find(name);
    if (at < 0)
    {
        return Status::NotFound;
    }
    if (step.name == nullptr || (name != step.name && find(step.name) >= 0))
    {
        return Status::InvalidData;
    }
    Entry* old = entries[static_cast<uint32_t>(at)];
    // Out of the way first, so the replacement may reuse the name.
    for (uint32_t i = static_cast<uint32_t>(at); i + 1 < entries.size(); ++i)
    {
        entries[i] = entries[i + 1];
    }
    entries.resize(entries.size() - 1);
    if (Result<void> inserted = insert(static_cast<uint32_t>(at), step); !inserted)
    {
        // Put the old one back where it was.
        entries.pushBack(nullptr);
        for (uint32_t i = static_cast<uint32_t>(entries.size()) - 1; i > static_cast<uint32_t>(at); --i)
        {
            entries[i] = entries[i - 1];
        }
        entries[static_cast<uint32_t>(at)] = old;
        return inserted;
    }
    if (device != nullptr && old->step.release != nullptr)
    {
        old->step.release(*device, old->step.userData);
    }
    destroy(MemoryTag::Render, old);
    return {};
}

Result<void> RenderSchedule::remove(std::string_view name)
{
    const int32_t at = find(name);
    if (at < 0)
    {
        return Status::NotFound;
    }
    Entry* old = entries[static_cast<uint32_t>(at)];
    for (uint32_t i = static_cast<uint32_t>(at); i + 1 < entries.size(); ++i)
    {
        entries[i] = entries[i + 1];
    }
    entries.resize(entries.size() - 1);
    if (device != nullptr && old->step.release != nullptr)
    {
        old->step.release(*device, old->step.userData);
    }
    destroy(MemoryTag::Render, old);
    return {};
}

uint32_t RenderSchedule::stepCount() const
{
    return static_cast<uint32_t>(entries.size());
}

std::string_view RenderSchedule::stepName(uint32_t index) const
{
    return index < entries.size() ? std::string_view(entries[index]->step.name) : std::string_view();
}

const RenderStep& RenderSchedule::step(uint32_t index) const
{
    return entries[index]->step;
}

Result<void> RenderSchedule::prepareAll(slag::GraphicsDevice& renderDevice)
{
    for (uint32_t i = 0; i < entries.size(); ++i)
    {
        const RenderStep& step = entries[i]->step;
        if (step.prepare == nullptr)
        {
            continue;
        }
        if (Result<void> prepared = step.prepare(renderDevice, step.userData); !prepared)
        {
            // Undo the ones already prepared.
            for (uint32_t j = 0; j < i; ++j)
            {
                if (entries[j]->step.release != nullptr)
                {
                    entries[j]->step.release(renderDevice, entries[j]->step.userData);
                }
            }
            return prepared;
        }
    }
    device = &renderDevice;
    return {};
}

void RenderSchedule::releaseAll()
{
    if (device == nullptr)
    {
        return;
    }
    for (Entry* entry : entries)
    {
        if (entry->step.release != nullptr)
        {
            entry->step.release(*device, entry->step.userData);
        }
    }
    device = nullptr;
}

RenderSchedule& renderSchedule()
{
    static RenderSchedule schedule;
    static const bool defaults = [] {
        addDefaultSteps(schedule);
        return true;
    }();
    (void)defaults;
    return schedule;
}

} // namespace crucible::render
