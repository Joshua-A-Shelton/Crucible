#include "StateMachine.h"

#include <algorithm>

namespace crucible::animation {

namespace {

bool nameFits(uint32_t offset, uint32_t length, size_t size)
{
    return uint64_t{offset} + length <= size;
}

// Every index in range, so the runtime needn't check.
bool valid(const StateMachine& machine)
{
    const size_t names = machine.names.size();
    const uint32_t parameterCount = static_cast<uint32_t>(machine.parameters.size());
    const uint32_t stateCount = static_cast<uint32_t>(machine.states.size());
    if (stateCount == 0 || machine.info.start >= stateCount)
    {
        return false;
    }
    for (const assets::StateParameter& parameter : machine.parameters)
    {
        if (!nameFits(parameter.nameOffset, parameter.nameLength, names) ||
            static_cast<uint32_t>(parameter.type) > static_cast<uint32_t>(assets::ParameterType::Trigger))
        {
            return false;
        }
    }
    for (const assets::StateEntry& state : machine.states)
    {
        if (!nameFits(state.nameOffset, state.nameLength, names) || state.motionCount == 0 ||
            uint64_t{state.firstMotion} + state.motionCount > machine.motions.size() ||
            state.dimensions > assets::maxBlendDimensions)
        {
            return false;
        }
        for (uint32_t d = 0; d < state.dimensions; ++d)
        {
            if (state.blendParameters[d] < 0 || static_cast<uint32_t>(state.blendParameters[d]) >= parameterCount)
            {
                return false;
            }
        }
    }
    for (const assets::StateTransition& transition : machine.transitions)
    {
        if (transition.from >= static_cast<int32_t>(stateCount) || transition.from < -1 ||
            transition.to >= stateCount ||
            uint64_t{transition.firstCondition} + transition.conditionCount > machine.conditions.size())
        {
            return false;
        }
    }
    for (const assets::TransitionCondition& condition : machine.conditions)
    {
        if (condition.parameter >= parameterCount ||
            static_cast<uint32_t>(condition.op) > static_cast<uint32_t>(assets::ConditionOp::Set))
        {
            return false;
        }
    }
    return true;
}

bool holds(const assets::TransitionCondition& condition, float value)
{
    switch (condition.op)
    {
    case assets::ConditionOp::Greater:
        return value > condition.value;
    case assets::ConditionOp::Less:
        return value < condition.value;
    case assets::ConditionOp::Equal:
        return value == condition.value;
    case assets::ConditionOp::NotEqual:
        return value != condition.value;
    case assets::ConditionOp::Set:
        return value != 0;
    }
    return false;
}

bool passed(float exitTime, const StateProgress& progress)
{
    if (!progress.loop)
    {
        return progress.after >= exitTime;
    }
    if (progress.wraps == 0)
    {
        return progress.before < exitTime && exitTime <= progress.after;
    }
    // Past the end and round again: it passed if it was before the exit
    // time when it left, or past it now, or went round more than once.
    return progress.wraps > 1 || progress.before < exitTime || exitTime <= progress.after;
}

} // namespace

int32_t StateMachine::findParameter(std::string_view wanted) const
{
    for (size_t p = 0; p < parameters.size(); ++p)
    {
        if (name(parameters[p].nameOffset, parameters[p].nameLength) == wanted)
        {
            return static_cast<int32_t>(p);
        }
    }
    return -1;
}

int32_t StateMachine::findState(std::string_view wanted) const
{
    for (size_t s = 0; s < states.size(); ++s)
    {
        if (name(states[s].nameOffset, states[s].nameLength) == wanted)
        {
            return static_cast<int32_t>(s);
        }
    }
    return -1;
}

StateMachine* parseStateMachine(std::span<const uint8_t> bytes)
{
    Result<binary::File*> opened =
        binary::parseFile(bytes, assets::stateMachineFileKind, assets::stateMachineFileVersion);
    if (!opened)
    {
        return nullptr;
    }
    binary::File* file = opened.value();
    StateMachine* machine = create<StateMachine>(MemoryTag::Animation);
    Vector<assets::StateMachineInfo> info(MemoryTag::Animation);
    const bool read = readTable(*file, "INFO", info) && readTable(*file, "PARM", machine->parameters) &&
                      readTable(*file, "STAT", machine->states) && readTable(*file, "MOTN", machine->motions) &&
                      readTable(*file, "TRAN", machine->transitions) && readTable(*file, "COND", machine->conditions) &&
                      readTable(*file, "NAME", machine->names);
    binary::closeFile(file);
    if (read && !info.empty())
    {
        machine->info = info[0];
    }
    if (!read || info.empty() || !valid(*machine))
    {
        destroyStateMachine(machine);
        return nullptr;
    }
    return machine;
}

void destroyStateMachine(StateMachine* machine)
{
    destroy(MemoryTag::Animation, machine);
}

uint32_t stateClips(const StateMachine& machine, uint32_t state, std::span<const float> parameters,
                    ClipWeight (&out)[maxStateClips])
{
    const assets::StateEntry& entry = machine.states[state];
    const uint32_t first = entry.firstMotion;
    if (entry.dimensions != 1 || entry.motionCount == 1)
    {
        // One clip; 2D blend spaces aren't read yet (the importer refuses
        // them), so one would play its first.
        out[0] = {first, 1.0f};
        return 1;
    }
    // 1D: the two clips either side of the value, by how near each is.
    // Positions increase (the importer sorts them).
    const float value = parameters[static_cast<uint32_t>(entry.blendParameters[0])];
    const uint32_t last = first + entry.motionCount - 1;
    if (value <= machine.motions[first].position[0])
    {
        out[0] = {first, 1.0f};
        return 1;
    }
    if (value >= machine.motions[last].position[0])
    {
        out[0] = {last, 1.0f};
        return 1;
    }
    uint32_t below = first;
    while (below + 1 < last && machine.motions[below + 1].position[0] <= value)
    {
        ++below;
    }
    const float from = machine.motions[below].position[0];
    const float to = machine.motions[below + 1].position[0];
    const float t = to > from ? (value - from) / (to - from) : 0.0f;
    out[0] = {below, 1.0f - t};
    out[1] = {below + 1, t};
    return 2;
}

int32_t chooseTransition(const StateMachine& machine, uint32_t state, std::span<const float> parameters,
                         const StateProgress& progress)
{
    for (size_t t = 0; t < machine.transitions.size(); ++t)
    {
        const assets::StateTransition& transition = machine.transitions[t];
        const bool from =
            transition.from < 0 ? transition.to != state : static_cast<uint32_t>(transition.from) == state;
        if (!from || (transition.exitTime >= 0 && !passed(transition.exitTime, progress)))
        {
            continue;
        }
        bool all = true;
        for (uint32_t c = 0; c < transition.conditionCount && all; ++c)
        {
            const assets::TransitionCondition& condition = machine.conditions[transition.firstCondition + c];
            all = holds(condition, parameters[condition.parameter]);
        }
        if (all)
        {
            return static_cast<int32_t>(t);
        }
    }
    return -1;
}

void consumeTriggers(const StateMachine& machine, uint32_t transition, std::span<float> parameters)
{
    const assets::StateTransition& taken = machine.transitions[transition];
    for (uint32_t c = 0; c < taken.conditionCount; ++c)
    {
        const uint32_t parameter = machine.conditions[taken.firstCondition + c].parameter;
        if (machine.parameters[parameter].type == assets::ParameterType::Trigger)
        {
            parameters[parameter] = 0;
        }
    }
}

} // namespace crucible::animation
