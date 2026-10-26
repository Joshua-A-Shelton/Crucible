#include "DotnetHost.h"

#include <coreclr_delegates.h>
#include <hostfxr.h>
#include <nethost.h>

#include <cstdio>
#include <string>

#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#define CRUCIBLE_HOST_TEXT(text) L##text
#else
#include <dlfcn.h>
#define CRUCIBLE_HOST_TEXT(text) text
#endif

namespace crucible::launcher {

namespace {

// hostfxr's char_t is wchar_t on Windows and char elsewhere: the same as
// std::filesystem::path's native character type, so paths pass straight
// through with c_str().

constexpr const char_t* engineAssemblyFile = CRUCIBLE_HOST_TEXT("Crucible.Engine.dll");
constexpr const char_t* engineRuntimeConfigFile = CRUCIBLE_HOST_TEXT("Crucible.Engine.runtimeconfig.json");
constexpr const char_t* entryPointType = CRUCIBLE_HOST_TEXT("Crucible.Hosting.Host, Crucible.Engine");
constexpr const char_t* entryPointMethod = CRUCIBLE_HOST_TEXT("Run");

using EntryPointFn = int(CORECLR_DELEGATE_CALLTYPE*)();

struct Hostfxr
{
    hostfxr_initialize_for_runtime_config_fn initialize = nullptr;
    hostfxr_get_runtime_delegate_fn getRuntimeDelegate = nullptr;
    hostfxr_close_fn close = nullptr;
};

std::string displayPath(const std::filesystem::path& path)
{
    const std::u8string text = path.u8string();
    return std::string(reinterpret_cast<const char*>(text.data()), text.size());
}

void* loadLibrary(const char_t* path)
{
#if defined(_WIN32)
    return ::LoadLibraryW(path);
#else
    return ::dlopen(path, RTLD_NOW | RTLD_LOCAL);
#endif
}

void* findExport(void* library, const char* name)
{
#if defined(_WIN32)
    return reinterpret_cast<void*>(::GetProcAddress(static_cast<HMODULE>(library), name));
#else
    return ::dlsym(library, name);
#endif
}

Result<Hostfxr> loadHostfxr(const std::filesystem::path& assemblyPath)
{
    // Passing the assembly path lets nethost prefer a hostfxr shipped next to
    // the game (a self-contained build) over the installed one.
    get_hostfxr_parameters parameters{sizeof(get_hostfxr_parameters), assemblyPath.c_str(), nullptr};
    char_t buffer[4096];
    size_t bufferSize = sizeof(buffer) / sizeof(buffer[0]);
    if (get_hostfxr_path(buffer, &bufferSize, &parameters) != 0)
    {
        std::fprintf(stderr, "crucible: could not find hostfxr; is the .NET runtime installed?\n");
        return Status::NotFound;
    }

    void* library = loadLibrary(buffer);
    if (library == nullptr)
    {
        std::fprintf(stderr, "crucible: could not load %s\n", displayPath(buffer).c_str());
        return Status::NotFound;
    }

    Hostfxr hostfxr;
    hostfxr.initialize = reinterpret_cast<hostfxr_initialize_for_runtime_config_fn>(
        findExport(library, "hostfxr_initialize_for_runtime_config"));
    hostfxr.getRuntimeDelegate =
        reinterpret_cast<hostfxr_get_runtime_delegate_fn>(findExport(library, "hostfxr_get_runtime_delegate"));
    hostfxr.close = reinterpret_cast<hostfxr_close_fn>(findExport(library, "hostfxr_close"));
    if (hostfxr.initialize == nullptr || hostfxr.getRuntimeDelegate == nullptr || hostfxr.close == nullptr)
    {
        std::fprintf(stderr, "crucible: hostfxr is missing a hosting function\n");
        return Status::Unsupported;
    }
    return hostfxr;
}

} // namespace

Result<int> runEngine(const std::filesystem::path& managedDirectory)
{
    const std::filesystem::path assemblyPath = managedDirectory / engineAssemblyFile;
    const std::filesystem::path runtimeConfigPath = managedDirectory / engineRuntimeConfigFile;

    Result<Hostfxr> loaded = loadHostfxr(assemblyPath);
    if (!loaded)
    {
        return loaded.status();
    }
    const Hostfxr& hostfxr = loaded.value();

    // hostfxr returns HRESULT-style codes: success codes are 0 to 2, failures
    // are negative.
    hostfxr_handle context = nullptr;
    const int32_t initialized = hostfxr.initialize(runtimeConfigPath.c_str(), nullptr, &context);
    if (initialized < 0 || context == nullptr)
    {
        std::fprintf(stderr, "crucible: could not start .NET from %s (0x%08x)\n",
                     displayPath(runtimeConfigPath).c_str(), static_cast<unsigned>(initialized));
        if (context != nullptr)
        {
            hostfxr.close(context);
        }
        return Status::Unsupported;
    }

    // load_assembly puts the engine in the default load context; only game
    // assemblies go into collectible contexts (ADR 0001, 0011).
    void* loadAssemblyDelegate = nullptr;
    void* getFunctionPointerDelegate = nullptr;
    hostfxr.getRuntimeDelegate(context, hdt_load_assembly, &loadAssemblyDelegate);
    hostfxr.getRuntimeDelegate(context, hdt_get_function_pointer, &getFunctionPointerDelegate);
    // The runtime stays loaded; the context handle is only needed to get the
    // delegates.
    hostfxr.close(context);
    if (loadAssemblyDelegate == nullptr || getFunctionPointerDelegate == nullptr)
    {
        std::fprintf(stderr, "crucible: .NET did not provide the hosting delegates\n");
        return Status::Unsupported;
    }

    const auto loadAssembly = reinterpret_cast<load_assembly_fn>(loadAssemblyDelegate);
    const auto getFunctionPointer = reinterpret_cast<get_function_pointer_fn>(getFunctionPointerDelegate);

    if (loadAssembly(assemblyPath.c_str(), nullptr, nullptr) != 0)
    {
        std::fprintf(stderr, "crucible: could not load %s\n", displayPath(assemblyPath).c_str());
        return Status::NotFound;
    }

    void* entryPoint = nullptr;
    if (getFunctionPointer(entryPointType, entryPointMethod, UNMANAGEDCALLERSONLY_METHOD, nullptr, nullptr,
                           &entryPoint) != 0 ||
        entryPoint == nullptr)
    {
        std::fprintf(stderr, "crucible: entry point Crucible.Hosting.Host.Run not found\n");
        return Status::NotFound;
    }

    return reinterpret_cast<EntryPointFn>(entryPoint)();
}

} // namespace crucible::launcher
