#pragma once

// Debug lines (ADR 0079): lines anyone adds for one frame, drawn over the
// finished image by the DebugLines step (render::steps::debugLines), after
// Tonemap, with no depth test, in every view that frame. The list is
// cleared at the start of each frame by the "DebugLines.Clear" system
// (BeginFrame). Main thread.

#include <crucible/Export.h>
#include <crucible/Expose.h>
#include <crucible/Math.h>

#include <cstdint>
#include <span>
#include <string_view>

namespace crucible::debug {

inline constexpr std::string_view clearSystemName = "DebugLines.Clear";

// Lines past this many in a frame are dropped.
inline constexpr uint32_t maxDebugLines = 32768;

// A line's ends, in world space, and its color (linear, with alpha).
struct DebugLine
{
    Vector3 from;
    Vector3 to;
    Vector4 color;
};

// Adds a line for this frame.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void drawLine(Vector3 from, Vector3 to, Vector4 color);

// The frame's lines so far.
CRUCIBLE_EXPORT std::span<const DebugLine> debugLines();
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE uint32_t debugLineCount();

// Forgets the frame's lines; "DebugLines.Clear" calls it.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE void clearDebugLines();

// Registers "DebugLines.Clear". The engine calls it at start-up.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE_INTERNAL void registerDebugLines();

} // namespace crucible::debug
