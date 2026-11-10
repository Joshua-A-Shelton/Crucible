// Debug lines' list (ADR 0079); the DebugLines step (RenderSteps.cpp) draws it.

#include <crucible/DebugLines.h>
#include <crucible/Systems.h>
#include <crucible/Vector.h>

namespace crucible::debug {

namespace {

Vector<DebugLine> lines{MemoryTag::Render};

void clearEachFrame(const loop::SystemContext&, void*)
{
    clearDebugLines();
}

} // namespace

void drawLine(Vector3 from, Vector3 to, Vector4 color)
{
    if (lines.size() < maxDebugLines)
    {
        lines.pushBack({from, to, color});
    }
}

std::span<const DebugLine> debugLines()
{
    return lines.span();
}

uint32_t debugLineCount()
{
    return static_cast<uint32_t>(lines.size());
}

void clearDebugLines()
{
    lines.clear();
}

void registerDebugLines()
{
    loop::registerDefaultSystem({.name = clearSystemName, .phase = "BeginFrame", .function = clearEachFrame});
}

} // namespace crucible::debug
