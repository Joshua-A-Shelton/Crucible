#pragma once

// State machines (ADR 0081) as the pose runtime holds them: a
// .crstatemachine read and checked, and the two choices made from it each
// frame: which clips a state plays at its parameters (a blend space's
// weights), and which transition to take. Playing the clips is the pose
// runtime's (Animation.cpp).

#include <crucible/BinaryFile.h>
#include <crucible/StateMachineFile.h>
#include <crucible/Vector.h>

#include <cstdint>
#include <cstring>
#include <span>
#include <string_view>

namespace crucible::animation {

// Clips one state plays at once, at most: a 1D blend space blends two, a 2D
// one would blend three.
inline constexpr uint32_t maxStateClips = 4;

// A section of a file as a table of T; false if it's missing or unreadable.
template <class T>
bool readTable(const binary::File& file, const char (&name)[5], Vector<T>& out)
{
    const int32_t index = binary::findSection(file, binary::tag(name));
    if (index < 0)
    {
        return false;
    }
    const binary::SectionInfo info = binary::sectionInfo(file, static_cast<uint32_t>(index));
    Vector<uint8_t> bytes(MemoryTag::Animation);
    bytes.resize(static_cast<size_t>(info.size));
    if (!binary::readSection(file, static_cast<uint32_t>(index), bytes.span()))
    {
        return false;
    }
    out.resize(bytes.size() / sizeof(T));
    std::memcpy(out.data(), bytes.data(), out.size() * sizeof(T));
    return true;
}

struct StateMachine
{
    assets::StateMachineInfo info{};
    Vector<assets::StateParameter> parameters{MemoryTag::Animation};
    Vector<assets::StateEntry> states{MemoryTag::Animation};
    Vector<assets::StateMotion> motions{MemoryTag::Animation};
    Vector<assets::StateTransition> transitions{MemoryTag::Animation};
    Vector<assets::TransitionCondition> conditions{MemoryTag::Animation};
    Vector<char> names{MemoryTag::Animation};

    std::string_view name(uint32_t offset, uint32_t length) const
    {
        return {names.data() + offset, length};
    }
    int32_t findParameter(std::string_view name) const;
    int32_t findState(std::string_view name) const;
};

// Reads a STAT file, every index checked; null if it isn't a valid one.
StateMachine* parseStateMachine(std::span<const uint8_t> bytes);
void destroyStateMachine(StateMachine* machine);

struct ClipWeight
{
    uint32_t motion; // into StateMachine::motions
    float weight;
};

// The clips `state` plays at these parameter values, their weights summing
// to 1; returns how many (at least 1, for a state with a clip).
uint32_t stateClips(const StateMachine& machine, uint32_t state, std::span<const float> parameters,
                    ClipWeight (&out)[maxStateClips]);

// How far a state played in the last update, as shares of its length.
struct StateProgress
{
    float before;
    float after;
    uint32_t wraps; // times a looping state went past its end
    bool loop;
    float raw;  // where it would be, not wrapped or held at its end
    float rate; // shares a second
};

// The first transition, in order, from `state` (or from any state but its
// target) whose conditions hold and whose exit time, if it has one, the
// state just passed (once a loop for a looping state; a state that doesn't
// loop stays past it); -1 for none.
int32_t chooseTransition(const StateMachine& machine, uint32_t state, std::span<const float> parameters,
                         const StateProgress& progress);

// Clears the Triggers a transition tested, now that it was taken.
void consumeTriggers(const StateMachine& machine, uint32_t transition, std::span<float> parameters);

} // namespace crucible::animation
