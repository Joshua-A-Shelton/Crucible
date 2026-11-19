// The launcher: owns main(), platform and renderer setup, and shutdown, and
// hands the frame to the engine's C# layer (ADR 0011). Built twice: as the
// game launcher, and as the editor launcher (CRUCIBLE_EDITOR_LAUNCHER, ADR
// 0016, 0066), which loads the editor's assembly as its game.

#include "CpuCheck.h"
#include "EngineHost.h"

#include <crucible/Jobs.h>
#include <crucible/Launch.h>
#include <crucible/Log.h>
#include <crucible/Module.h>
#include <crucible/Platform.h>
#include <crucible/Render.h>
#include <crucible/Text.h>

#include <slag/Instance.h>

#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <filesystem>
#include <string>
#include <string_view>
#include <system_error>
#include <vector>

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

#if defined(_WIN32)
constexpr const wchar_t* moduleExtension = L".dll";
#else
constexpr const char* moduleExtension = ".so";
#endif

// Whether `text` is an asset ID as C# writes one (a GUID with hyphens).
bool isAssetId(std::u8string_view text)
{
    if (text.size() != 36)
    {
        return false;
    }
    for (size_t i = 0; i < text.size(); ++i)
    {
        const char8_t c = text[i];
        const bool hyphen = i == 8 || i == 13 || i == 18 || i == 23;
        const bool hex = (c >= u8'0' && c <= u8'9') || (c >= u8'a' && c <= u8'f') || (c >= u8'A' && c <= u8'F');
        if (hyphen ? c != u8'-' : !hex)
        {
            return false;
        }
    }
    return true;
}

// A package's executable is named after its game (ADR 0100); the editor's
// install keeps the engine's assemblies beside its executables.
std::filesystem::path executablePath()
{
#if defined(_WIN32)
    wchar_t buffer[MAX_PATH];
    const DWORD length = ::GetModuleFileNameW(nullptr, buffer, MAX_PATH);
    return std::filesystem::path(std::wstring(buffer, length));
#else
    std::error_code error;
    const std::filesystem::path executable = std::filesystem::read_symlink("/proc/self/exe", error);
    return error ? std::filesystem::current_path(error) / "crucible" : executable;
#endif
}

std::filesystem::path executableDirectory()
{
    return executablePath().parent_path();
}

// Where a game keeps its player's files (ADR 0103): %LOCALAPPDATA% on
// Windows, $XDG_DATA_HOME or ~/.local/share elsewhere; empty if unknown.
[[maybe_unused]] std::filesystem::path playerDataFolder()
{
#if defined(_WIN32)
    const wchar_t* local = _wgetenv(L"LOCALAPPDATA");
    return local != nullptr && *local != 0 ? std::filesystem::path(local) : std::filesystem::path();
#else
    if (const char* data = std::getenv("XDG_DATA_HOME"); data != nullptr && *data == '/')
    {
        return data;
    }
    const char* home = std::getenv("HOME");
    return home != nullptr && *home != 0 ? std::filesystem::path(home) / ".local" / "share" : std::filesystem::path();
#endif
}

[[maybe_unused]] void addPathArgument(std::u8string_view key, const std::filesystem::path& path)
{
    const std::u8string utf8 = std::u8string(key) + path.u8string();
    crucible::addGameArgument({reinterpret_cast<const char*>(utf8.data()), utf8.size()});
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
    // --fixed-clock (ADR 0105) makes the clock move on exactly one
    // --frame-rate period a frame, never waiting: a check's run takes as
    // long as its frames do, its times the same on every machine.
    // --frames N, --headless and --no-pacing are passed to the engine
    // (LaunchOptions); --game <file> names the game assembly to load and
    // --game-arg key=value (repeatable) passes it an option; --latency <file>
    // turns on the latency harness and
    // --present-mode fifo|mailbox overrides ADR 0003's default, both for
    // measuring. --msaa 1|2|4|8 sets the MSAA sample count (ADR 0051);
    // --shadow-map-size N and --shadow-atlas-size N the shadow maps' sizes
    // (ADR 0054). --project <file> and --scene <file> name the project and
    // scene to open (the game arguments project= and scene=): the engine
    // opens them for a game (ADR 0073), the editor for itself. --module <file>
    // (repeatable) loads a project's native module (ADR 0074) once the
    // engine's native side is up, before the C# engine builds its schedule.
    // --build <folder> (the game argument build=) has the editor build the
    // project's package into the folder and quit (ADR 0100). --archive <file>
    // (the game argument archive=) runs a game from its cooked archive,
    // starting the scene the archive names, or the one --scene names by ID
    // (ADR 0099). Beside a Data folder, the launcher is a package's (ADR
    // 0100): with none of these options it runs the game named after it from
    // Data.
    std::filesystem::path managedDirectory = executableDirectory();
    std::filesystem::path application; // a package's game: its own runtime starts
    std::vector<std::u8string> modules;
    bool managedGiven = false;
    bool gameGiven = false;
    bool assetsGiven = false; // --project or --archive
    slag::Backend backend = slag::InstanceDesc{}.backend;
    crucible::LaunchOptions options{};
#if defined(CRUCIBLE_EDITOR_LAUNCHER)
    options.editor = true;
#endif
    crucible::render::PresentPreference presentMode = crucible::render::PresentPreference::Default;
    uint32_t workers = 0;
    uint32_t msaa = 0; // 0: the renderer's default
    uint32_t shadowMap = 0;
    uint32_t shadowAtlas = 0;
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
        if (option == CRUCIBLE_ARGUMENT("--fixed-clock"))
        {
            options.fixedClock = true;
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
            managedGiven = true;
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
        else if (option == CRUCIBLE_ARGUMENT("--frame-rate"))
        {
            valid = parseCount(value, options.frameRate) && options.frameRate > 0 && options.frameRate <= 1000;
        }
        else if (option == CRUCIBLE_ARGUMENT("--msaa"))
        {
            valid = parseCount(value, msaa) && (msaa == 1 || msaa == 2 || msaa == 4 || msaa == 8);
        }
        else if (option == CRUCIBLE_ARGUMENT("--shadow-map-size"))
        {
            valid = parseCount(value, shadowMap) && crucible::render::setShadowMapSize(shadowMap);
        }
        else if (option == CRUCIBLE_ARGUMENT("--shadow-atlas-size"))
        {
            valid = parseCount(value, shadowAtlas) && crucible::render::setShadowAtlasSize(shadowAtlas);
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
            gameGiven = true;
        }
        else if (option == CRUCIBLE_ARGUMENT("--project") || option == CRUCIBLE_ARGUMENT("--scene") ||
                 option == CRUCIBLE_ARGUMENT("--build") || option == CRUCIBLE_ARGUMENT("--archive"))
        {
            const std::filesystem::path given(value);
            std::error_code error;
            // A scene in an archive is named by its ID, which stays as it is.
            const bool sceneId = option == CRUCIBLE_ARGUMENT("--scene") && isAssetId(given.u8string());
            const std::filesystem::path path = sceneId ? given : std::filesystem::absolute(given, error);
            const std::u8string key = option == CRUCIBLE_ARGUMENT("--project")   ? u8"project="
                                      : option == CRUCIBLE_ARGUMENT("--scene")   ? u8"scene="
                                      : option == CRUCIBLE_ARGUMENT("--archive") ? u8"archive="
                                                                                 : u8"build=";
            const std::u8string utf8 = key + (error ? given : path).u8string();
            crucible::addGameArgument({reinterpret_cast<const char*>(utf8.data()), utf8.size()});
            assetsGiven =
                assetsGiven || option == CRUCIBLE_ARGUMENT("--project") || option == CRUCIBLE_ARGUMENT("--archive");
        }
        else if (option == CRUCIBLE_ARGUMENT("--module"))
        {
            std::error_code error;
            const std::filesystem::path path = std::filesystem::absolute(std::filesystem::path(value), error);
            modules.push_back((error ? std::filesystem::path(value) : path).u8string());
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
#if defined(CRUCIBLE_EDITOR_LAUNCHER)
    (void)managedGiven;
    // The editor is the game: beside the engine's assemblies when shipped,
    // else in the build tree's artifacts beside the engine's folder
    // (bin/Crucible.Editor/<configuration>/).
    if (!gameGiven)
    {
        const std::filesystem::path shipped = managedDirectory / "Crucible.Editor.dll";
        const std::filesystem::path built = managedDirectory.parent_path().parent_path() / "Crucible.Editor" /
                                            managedDirectory.filename() / "Crucible.Editor.dll";
        const std::filesystem::path editor = std::filesystem::exists(shipped, error) ? shipped : built;
        if (!std::filesystem::exists(editor, error))
        {
            std::fprintf(stderr, "crucible: the editor's assembly isn't beside the engine's (Crucible.Editor.dll)\n");
            return 1;
        }
        const std::u8string utf8 = editor.u8string();
        crucible::setGamePath({reinterpret_cast<const char*>(utf8.data()), utf8.size()});
    }
#else
    // A package (ADR 0100, 0101): Data beside the executable holds the
    // archive, Data/Game.crpak, and for a JIT game the engine's assemblies,
    // its own .NET runtime, and the game named after the executable: Walker
    // runs Data/Walker.dll and its C++ module Data/Walker.Core if it has one.
    const std::filesystem::path data = executableDirectory() / "Data";
    if (!managedGiven && std::filesystem::is_directory(data, error))
    {
        const std::filesystem::path name = executablePath().stem();
        managedDirectory = data;
        // A NativeAOT package has its game compiled in, so no assembly.
        const std::filesystem::path game = data / (name.native() + CRUCIBLE_ARGUMENT(".dll"));
        if (!gameGiven && std::filesystem::exists(game, error))
        {
            application = game;
            const std::u8string utf8 = application.u8string();
            crucible::setGamePath({reinterpret_cast<const char*>(utf8.data()), utf8.size()});
        }
        const std::filesystem::path module = data / (name.native() + CRUCIBLE_ARGUMENT(".Core") + moduleExtension);
        if (modules.empty() && std::filesystem::exists(module, error))
        {
            modules.push_back(module.u8string());
        }
        if (!assetsGiven && std::filesystem::exists(data / "Game.crpak", error))
        {
            addPathArgument(u8"archive=", data / "Game.crpak");
        }
        // Its log file, in the player's data folder (ADR 0103).
        if (const std::filesystem::path folder = playerDataFolder(); !folder.empty())
        {
            const std::filesystem::path game = folder / name;
            const std::u8string log = (game / (name.native() + CRUCIBLE_ARGUMENT(".log"))).u8string();
            const std::u8string previous = (game / (name.native() + CRUCIBLE_ARGUMENT("-previous.log"))).u8string();
            crucible::log::setLogFile({reinterpret_cast<const char*>(log.data()), log.size()},
                                      {reinterpret_cast<const char*>(previous.data()), previous.size()});
        }
    }
#endif

#if defined(CRUCIBLE_EDITOR_LAUNCHER)
    // A click on an editor window acts even when it also focuses the window
    // (ADR 0071): SDL drops it otherwise, so after a drag between windows the
    // first press in the other one did nothing. Games keep SDL's default. An
    // environment variable is a hint SDL reads at startup; one already set wins.
#if defined(_WIN32)
    if (std::getenv("SDL_MOUSE_FOCUS_CLICKTHROUGH") == nullptr)
    {
        _putenv_s("SDL_MOUSE_FOCUS_CLICKTHROUGH", "1");
    }
#else
    setenv("SDL_MOUSE_FOCUS_CLICKTHROUGH", "1", 0);
#endif
#endif

    // --fixed-clock steps by --frame-rate's period (ADR 0105).
    if (options.fixedClock && options.frameRate == 0)
    {
        std::fprintf(stderr, "crucible: --fixed-clock needs --frame-rate, which says how long a frame is\n");
        return 1;
    }

    // Platform, job system, and renderer setup and shutdown belong to the
    // launcher (ADR 0011), in that order and in reverse.
    if (!crucible::platform::initialize())
    {
        std::fprintf(stderr, "crucible: the platform layer could not start\n");
        return 1;
    }
    if (options.fixedClock)
    {
        crucible::platform::useFixedClock(1'000'000'000ull / options.frameRate);
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

    // A NativeAOT game's C++ module, linked in (ADR 0101), then any loaded.
    if (!crucible::startLinkedModules())
    {
        crucible::render::shutdown();
        crucible::jobs::shutdown();
        crucible::platform::shutdown();
        return 1;
    }
    for (const std::u8string& module : modules)
    {
        if (!crucible::loadModule({reinterpret_cast<const char*>(module.data()), module.size()}))
        {
            crucible::render::shutdown();
            crucible::jobs::shutdown();
            crucible::platform::shutdown();
            return 1;
        }
    }
    const crucible::Result<int> result = crucible::launcher::runEngine(managedDirectory, application, options);
    crucible::stopModules();
    crucible::render::shutdown();
    crucible::text::shutdown();
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
