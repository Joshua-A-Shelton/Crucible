#pragma once

#include <crucible/Launch.h>
#include <crucible/Result.h>

#include <filesystem>

namespace crucible::launcher {

// Runs the engine's single managed entry point (ADR 0011). On success, the
// value is the entry point's exit code.
//
// JitHost.cpp starts .NET and loads the engine's C# assembly from
// managedDirectory; AotHost.cpp calls the entry point compiled into the
// executable and ignores managedDirectory.
// application is a package's game assembly (ADR 0100), whose own runtime
// starts; empty, the installed runtime starts for the engine's.
Result<int> runEngine(const std::filesystem::path& managedDirectory, const std::filesystem::path& application,
                      const LaunchOptions& options);

} // namespace crucible::launcher
