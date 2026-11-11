#pragma once

// The imported state machine, .crstatemachine (ADR 0081): written by the
// StateMachine importer from an authored .crstates (JSON), read by the pose
// runtime, which plays it on a character's base.
//
// A STAT file (kind tag("STAT"), version 1):
//   INFO  StateMachineInfo
//   PARM  StateParameter, one per parameter
//   STAT  StateEntry, one per state
//   MOTN  StateMotion: the clips the states play, each state's in a run
//   TRAN  StateTransition, in the order they're checked
//   COND  TransitionCondition, each transition's in a run
//   NAME  UTF-8 names, referenced by offset and length
//
// A state plays one clip (dimensions 0) or blends its clips by parameters
// (a blend space): dimensions 1 places each clip at position[0] on one
// parameter, in increasing order. Positions and blend parameters have room
// for two dimensions, so 2D blend spaces (docs/open-items.md) need no new
// layout.

#include <crucible/AssetSource.h>
#include <crucible/BinaryFile.h>
#include <crucible/Expose.h>

#include <cstdint>

namespace crucible::assets {

inline constexpr uint32_t stateMachineFileKind = binary::tag("STAT");
inline constexpr uint32_t stateMachineFileVersion = 1;

// Parameters a blend space can blend by, at most.
inline constexpr uint32_t maxBlendDimensions = 2;

struct CRUCIBLE_EXPOSE StateMachineInfo
{
    uint32_t start; // the state a character starts in
    uint32_t parameterCount;
    uint32_t stateCount;
    uint32_t transitionCount;
};

enum class CRUCIBLE_EXPOSE ParameterType : uint32_t
{
    Float,
    Bool,    // 0 or 1
    Trigger, // set until a transition that tests it is taken
};

struct CRUCIBLE_EXPOSE StateParameter
{
    uint32_t nameOffset;
    uint32_t nameLength;
    ParameterType type;
    float defaultValue; // a Bool's or Trigger's as 0 or 1
};

struct CRUCIBLE_EXPOSE StateEntry
{
    uint32_t nameOffset;
    uint32_t nameLength;
    uint32_t firstMotion; // into MOTN
    uint32_t motionCount;
    uint32_t dimensions;                         // 0: one clip; 1: a 1D blend space
    int32_t blendParameters[maxBlendDimensions]; // into PARM, -1 past the dimensions
    float speed;
    uint32_t loop; // 0 or 1
};

struct CRUCIBLE_EXPOSE StateMotion
{
    AssetId clip;
    float position[maxBlendDimensions]; // where it sits in the blend space
};

struct CRUCIBLE_EXPOSE StateTransition
{
    int32_t from; // a state, or -1 from any state (but `to` itself)
    uint32_t to;
    float blendSeconds;
    float exitTime;          // the share of `from` played first (0 to 1), or below 0 for none
    uint32_t firstCondition; // into COND
    uint32_t conditionCount;
};

enum class CRUCIBLE_EXPOSE ConditionOp : uint32_t
{
    Greater,
    Less,
    Equal,
    NotEqual,
    Set, // a Trigger is set
};

struct CRUCIBLE_EXPOSE TransitionCondition
{
    uint32_t parameter; // into PARM
    ConditionOp op;
    float value; // a Bool's as 0 or 1
};

} // namespace crucible::assets
