#include "EngineHost.h"

// Exported by the engine's C# layer, compiled with NativeAOT into a static
// library and linked into this executable (ADR 0011): no .NET runtime to
// start, and no assemblies to load.
extern "C" int crucible_engine_run(crucible::LaunchOptions options);

namespace crucible::launcher {

Result<int> runEngine(const std::filesystem::path&, const LaunchOptions& options)
{
    return crucible_engine_run(options);
}

} // namespace crucible::launcher
