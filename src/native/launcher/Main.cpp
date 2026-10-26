// The launcher: owns main(), platform setup, and shutdown, and hands the
// frame to the engine's C# layer (ADR 0011). In M1 there is no platform setup
// yet; it only starts .NET and runs the engine's entry point.

#include "EngineHost.h"

#include <cstdio>
#include <cstring>
#include <filesystem>
#include <system_error>

#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#endif

namespace {

// Shipped games keep their managed assemblies next to the executable.
std::filesystem::path executableDirectory()
{
#if defined(_WIN32)
    wchar_t buffer[MAX_PATH];
    const DWORD length = ::GetModuleFileNameW(nullptr, buffer, MAX_PATH);
    return std::filesystem::path(std::wstring(buffer, length)).parent_path();
#else
    std::error_code error;
    const std::filesystem::path executable = std::filesystem::read_symlink("/proc/self/exe", error);
    return error ? std::filesystem::current_path(error) : executable.parent_path();
#endif
}

} // namespace

int main(int argc, char** argv)
{
    // --managed-dir <dir> overrides where the engine's C# assemblies are,
    // for development builds whose assemblies live in the build tree.
    std::filesystem::path managedDirectory = executableDirectory();
    for (int i = 1; i + 1 < argc; ++i)
    {
        if (std::strcmp(argv[i], "--managed-dir") == 0)
        {
            managedDirectory = argv[i + 1];
        }
    }

    // hostfxr's load_assembly requires a fully qualified path.
    std::error_code error;
    const std::filesystem::path absoluteDirectory = std::filesystem::absolute(managedDirectory, error);
    if (!error)
    {
        managedDirectory = absoluteDirectory;
    }

    const crucible::Result<int> result = crucible::launcher::runEngine(managedDirectory);
    if (!result)
    {
        std::fprintf(stderr, "crucible: the engine could not start (status %d)\n", static_cast<int>(result.status()));
        return 1;
    }
    return result.value();
}
