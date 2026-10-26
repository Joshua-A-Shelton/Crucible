#pragma once

#include <crucible/Result.h>

#include <filesystem>

namespace crucible::launcher {

// Starts .NET, loads the engine's C# assembly from managedDirectory into the
// default load context, and calls its single entry point (ADR 0011). On
// success, the value is the entry point's exit code.
Result<int> runEngine(const std::filesystem::path& managedDirectory);

} // namespace crucible::launcher
