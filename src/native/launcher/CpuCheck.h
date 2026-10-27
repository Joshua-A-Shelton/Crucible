#pragma once

namespace crucible::launcher {

// Exits with an explanation if the CPU lacks the x64 level Crucible was
// compiled for (ADR 0024's amendment). Also runs on its own before the
// executable's other static initializers (CpuCheck.cpp).
void checkCpu();

} // namespace crucible::launcher
