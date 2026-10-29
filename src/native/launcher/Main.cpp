// The launcher: owns main(), platform and renderer setup, and shutdown, and
// hands the frame to the engine's C# layer (ADR 0011).

#include "CpuCheck.h"
#include "EngineHost.h"

#include <crucible/Jobs.h>
#include <crucible/Launch.h>
#include <crucible/Platform.h>
#include <crucible/Render.h>

#include <slag/Instance.h>

#include <cstdint>
#include <cstdio>
#include <filesystem>
#include <string>
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

// For error messages: options are ASCII in practice, so a lossy narrowing of
// Windows' UTF-16 is enough.
std::string printable(ArgumentView text)
{
    std::string result;
    for (ArgumentChar c : text)
    {
        result.push_back(c >= 0x20 && c < 0x7f ? static_cast<char>(c) : '?');
    }
    return result;
}

bool parseCount(ArgumentView text, uint32_t& count)
{
    if (text.empty() || text.size() > 9)
    {
        return false;
    }
    uint32_t parsed = 0;
    for (ArgumentChar c : text)
    {
        if (c < '0' || c > '9')
        {
            return false;
        }
        parsed = parsed * 10 + static_cast<uint32_t>(c - '0');
    }
    count = parsed;
    return true;
}

int run(int argc, ArgumentChar** argv)
{
    crucible::launcher::checkCpu();

    // --managed-dir <dir> overrides where the engine's C# assemblies are,
    // for development builds whose assemblies live in the build tree.
    // --backend d3d12|vulkan overrides Slag's default for the platform (D3D12
    // on Windows, Vulkan elsewhere), so both can be tested on Windows.
    // --workers N overrides the job system's worker count (ADR 0014).
    // --frames N, --headless and --no-pacing are passed to the engine
    // (LaunchOptions); --game <file> names the game assembly to load and
    // --game-arg key=value (repeatable) passes it an option; --latency <file>
    // turns on the latency harness and
    // --present-mode fifo|mailbox overrides ADR 0003's default, both for
    // measuring. --msaa 1|2|4|8 sets the MSAA sample count (ADR 0051).
    std::filesystem::path managedDirectory = executableDirectory();
    slag::Backend backend = slag::InstanceDesc{}.backend;
    crucible::LaunchOptions options{};
    crucible::render::PresentPreference presentMode = crucible::render::PresentPreference::Default;
    uint32_t workers = 0;
    uint32_t msaa = 0; // 0: the renderer's default
    for (int i = 1; i < argc; ++i)
    {
        const ArgumentView option = argv[i];
        if (option == CRUCIBLE_ARGUMENT("--headless"))
        {
            options.headless = true;
            continue;
        }
        if (option == CRUCIBLE_ARGUMENT("--no-pacing"))
        {
            options.noPacing = true;
            continue;
        }
        if (i + 1 >= argc)
        {
            std::fprintf(stderr, "crucible: unknown option or missing value: %s\n", printable(option).c_str());
            return 1;
        }
        const ArgumentView value = argv[++i];
        bool valid = true;
        if (option == CRUCIBLE_ARGUMENT("--managed-dir"))
        {
            managedDirectory = value;
        }
        else if (option == CRUCIBLE_ARGUMENT("--backend"))
        {
            valid = value == CRUCIBLE_ARGUMENT("d3d12") || value == CRUCIBLE_ARGUMENT("vulkan");
            backend = value == CRUCIBLE_ARGUMENT("d3d12") ? slag::Backend::D3D12 : slag::Backend::Vulkan;
        }
        else if (option == CRUCIBLE_ARGUMENT("--workers"))
        {
            valid = parseCount(value, workers) && workers > 0;
        }
        else if (option == CRUCIBLE_ARGUMENT("--frames"))
        {
            valid = parseCount(value, options.frameLimit);
        }
        else if (option == CRUCIBLE_ARGUMENT("--msaa"))
        {
            valid = parseCount(value, msaa) && (msaa == 1 || msaa == 2 || msaa == 4 || msaa == 8);
        }
        else if (option == CRUCIBLE_ARGUMENT("--present-mode"))
        {
            valid = value == CRUCIBLE_ARGUMENT("fifo") || value == CRUCIBLE_ARGUMENT("mailbox");
            presentMode = value == CRUCIBLE_ARGUMENT("fifo") ? crucible::render::PresentPreference::Fifo
                                                             : crucible::render::PresentPreference::Mailbox;
        }
        else if (option == CRUCIBLE_ARGUMENT("--game"))
        {
            // hostfxr and C# want a fully qualified path, read back as UTF-8.
            std::error_code error;
            std::filesystem::path path = std::filesystem::absolute(std::filesystem::path(value), error);
            const std::u8string utf8 = (error ? std::filesystem::path(value) : path).u8string();
            crucible::setGamePath({reinterpret_cast<const char*>(utf8.data()), utf8.size()});
        }
        else if (option == CRUCIBLE_ARGUMENT("--game-arg"))
        {
            const std::u8string utf8 = std::filesystem::path(value).u8string();
            const std::string_view argument(reinterpret_cast<const char*>(utf8.data()), utf8.size());
            valid = argument.find('=') != std::string_view::npos && argument.front() != '=';
            if (valid)
            {
                crucible::addGameArgument(argument);
            }
        }
        else if (option == CRUCIBLE_ARGUMENT("--latency"))
        {
            // The engine reads it back as UTF-8 (C# strings).
            const std::u8string path = std::filesystem::path(value).u8string();
            crucible::setLatencyLogPath({reinterpret_cast<const char*>(path.data()), path.size()});
        }
        else
        {
            valid = false;
        }
        if (!valid)
        {
            std::fprintf(stderr, "crucible: bad option: %s %s\n", printable(option).c_str(), printable(value).c_str());
            return 1;
        }
    }

    // hostfxr's load_assembly requires a fully qualified path.
    std::error_code error;
    const std::filesystem::path absoluteDirectory = std::filesystem::absolute(managedDirectory, error);
    if (!error)
    {
        managedDirectory = absoluteDirectory;
    }

    // Platform, job system, and renderer setup and shutdown belong to the
    // launcher (ADR 0011), in that order and in reverse.
    if (!crucible::platform::initialize())
    {
        std::fprintf(stderr, "crucible: the platform layer could not start\n");
        return 1;
    }
    if (!crucible::jobs::initialize(workers))
    {
        std::fprintf(stderr, "crucible: the job system could not start\n");
        crucible::platform::shutdown();
        return 1;
    }
    if (!crucible::render::initialize(backend, presentMode))
    {
        std::fprintf(stderr, "crucible: the renderer could not start\n");
        crucible::jobs::shutdown();
        crucible::platform::shutdown();
        return 1;
    }
    if (msaa != 0 && !crucible::render::setMsaaSamples(msaa))
    {
        std::fprintf(stderr, "crucible: this device can't render with %ux MSAA\n", msaa);
        crucible::render::shutdown();
        crucible::jobs::shutdown();
        crucible::platform::shutdown();
        return 1;
    }

    const crucible::Result<int> result = crucible::launcher::runEngine(managedDirectory, options);
    crucible::render::shutdown();
    crucible::jobs::shutdown();
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
