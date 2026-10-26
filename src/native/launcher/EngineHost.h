#pragma once

#include <crucible/Result.h>

#include <filesystem>

namespace crucible::launcher {

// Runs the engine's single managed entry point (ADR 0011). On success, the
// value is the entry point's exit code.
//
// JitHost.cpp starts .NET and loads the engine's C# assembly from
// managedDirectory; AotHost.cpp calls the entry point compiled into the
// executable and ignores managedDirectory.
Result<int> runEngine(const std::filesystem::path& managedDirectory);

} // namespace crucible::launcher
