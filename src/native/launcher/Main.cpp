// The launcher: owns main(), platform and renderer setup, and shutdown, and
// hands the frame to the engine's C# layer (ADR 0011).

#include "EngineHost.h"

#include <crucible/Platform.h>
#include <crucible/Render.h>

#include <slag/Instance.h>

#include <cstdio>
#include <filesystem>
#include <string_view>
#include <system_error>

#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#endif

namespace {

// Windows passes the real UTF-16 arguments only to wmain; main's char** use
// the legacy code page, which can't represent every path. Elsewhere,
// arguments are bytes (UTF-8 in practice). Either way they go straight into
// std::filesystem::path, whose native character type matches.
#if defined(_WIN32)
using ArgumentChar = wchar_t;
#define CRUCIBLE_ARGUMENT(text) L##text
#else
using ArgumentChar = char;
#define CRUCIBLE_ARGUMENT(text) text
#endif
using ArgumentView = std::basic_string_view<ArgumentChar>;

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

int run(int argc, ArgumentChar** argv)
{
    // --managed-dir <dir> overrides where the engine's C# assemblies are,
    // for development builds whose assemblies live in the build tree.
    // --backend d3d12|vulkan overrides Slag's default for the platform (D3D12
    // on Windows, Vulkan elsewhere), so both can be tested on Windows.
    std::filesystem::path managedDirectory = executableDirectory();
    slag::Backend backend = slag::InstanceDesc{}.backend;
    for (int i = 1; i + 1 < argc; ++i)
    {
        const ArgumentView option = argv[i];
        const ArgumentView value = argv[i + 1];
        if (option == CRUCIBLE_ARGUMENT("--managed-dir"))
        {
            managedDirectory = argv[i + 1];
        }
        else if (option == CRUCIBLE_ARGUMENT("--backend"))
        {
            if (value == CRUCIBLE_ARGUMENT("d3d12"))
            {
                backend = slag::Backend::D3D12;
            }
            else if (value == CRUCIBLE_ARGUMENT("vulkan"))
            {
                backend = slag::Backend::Vulkan;
            }
            else
            {
                std::fprintf(stderr, "crucible: --backend takes d3d12 or vulkan\n");
                return 1;
            }
        }
    }

    // hostfxr's load_assembly requires a fully qualified path.
    std::error_code error;
    const std::filesystem::path absoluteDirectory = std::filesystem::absolute(managedDirectory, error);
    if (!error)
    {
        managedDirectory = absoluteDirectory;
    }

    // Platform and renderer setup and shutdown belong to the launcher (ADR
    // 0011), in that order and in reverse.
    if (!crucible::platform::initialize())
    {
        std::fprintf(stderr, "crucible: the platform layer could not start\n");
        return 1;
    }
    if (!crucible::render::initialize(backend))
    {
        std::fprintf(stderr, "crucible: the renderer could not start\n");
        crucible::platform::shutdown();
        return 1;
    }

    const crucible::Result<int> result = crucible::launcher::runEngine(managedDirectory);
    crucible::render::shutdown();
    crucible::platform::shutdown();
    if (!result)
    {
        std::fprintf(stderr, "crucible: the engine could not start (status %d)\n", static_cast<int>(result.status()));
        return 1;
    }
    return result.value();
}

} // namespace

#if defined(_WIN32)
int wmain(int argc, wchar_t** argv)
{
    return run(argc, argv);
}
#else
int main(int argc, char** argv)
{
    return run(argc, argv);
}
#endif
