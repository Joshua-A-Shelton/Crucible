// Native modules (ADR 0074): a project's shared library, loaded and started,
// kept loaded until the process ends.

#include <crucible/Log.h>
#include <crucible/Memory.h>
#include <crucible/Module.h>
#include <crucible/Vector.h>

#include <cstdio>
#include <mutex>

#if defined(_WIN32)
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#else
#include <dlfcn.h>
#endif

namespace crucible {

namespace loop {
// Systems.cpp: systems registered meanwhile are a module's.
void markModuleSystems(bool marking);
} // namespace loop

namespace {

using StartFunction = bool (*)();
using StopFunction = void (*)();

struct LoadedModule
{
    Vector<char> path{MemoryTag::General};
    void* library = nullptr;
    StopFunction stop = nullptr;
};

struct Modules
{
    std::mutex mutex;
    Vector<LoadedModule> loaded{MemoryTag::General};
};

Modules& modules()
{
    static Modules instance;
    return instance;
}

std::string_view viewOf(const Vector<char>& text)
{
    return {text.data(), text.size()};
}

void* openLibrary(std::string_view utf8Path)
{
#if defined(_WIN32)
    const int length = MultiByteToWideChar(CP_UTF8, 0, utf8Path.data(), static_cast<int>(utf8Path.size()), nullptr, 0);
    Vector<wchar_t> wide(MemoryTag::General);
    wide.resize(static_cast<size_t>(length) + 1);
    MultiByteToWideChar(CP_UTF8, 0, utf8Path.data(), static_cast<int>(utf8Path.size()), wide.data(), length);
    wide[static_cast<size_t>(length)] = 0;
    // Its own folder searched first for what it depends on.
    return LoadLibraryExW(wide.data(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
#else
    Vector<char> path(MemoryTag::General);
    path.assign(std::span<const char>(utf8Path.data(), utf8Path.size()));
    path.pushBack(0);
    void* library = dlopen(path.data(), RTLD_NOW | RTLD_LOCAL);
    if (library == nullptr)
    {
        log::warning("%s", dlerror());
    }
    return library;
#endif
}

void* symbol(void* library, const char* name)
{
#if defined(_WIN32)
    return reinterpret_cast<void*>(GetProcAddress(static_cast<HMODULE>(library), name));
#else
    return dlsym(library, name);
#endif
}

} // namespace

Result<void> loadModule(std::string_view utf8Path)
{
    Modules& state = modules();
    std::lock_guard lock(state.mutex);
    for (const LoadedModule& module : state.loaded)
    {
        if (viewOf(module.path) == utf8Path)
        {
            return {};
        }
    }
    void* library = openLibrary(utf8Path);
    if (library == nullptr)
    {
        log::error("the module %.*s couldn't be loaded", static_cast<int>(utf8Path.size()), utf8Path.data());
        return Status::NotFound;
    }
    LoadedModule& module = state.loaded.emplaceBack();
    module.path.assign(std::span<const char>(utf8Path.data(), utf8Path.size()));
    module.library = library;
    auto start = reinterpret_cast<StartFunction>(symbol(library, "crucibleModuleStart"));
    module.stop = reinterpret_cast<StopFunction>(symbol(library, "crucibleModuleStop"));
    if (start == nullptr || module.stop == nullptr)
    {
        log::warning("%.*s has no crucibleModuleStart or crucibleModuleStop", static_cast<int>(utf8Path.size()),
                     utf8Path.data());
        module.stop = nullptr;
        return Status::InvalidData;
    }
    loop::markModuleSystems(true);
    const bool started = start();
    loop::markModuleSystems(false);
    if (!started)
    {
        log::error("%.*s failed to start", static_cast<int>(utf8Path.size()), utf8Path.data());
        return Status::InvalidData;
    }
    return {};
}

void stopModules()
{
    Modules& state = modules();
    std::lock_guard lock(state.mutex);
    for (size_t i = state.loaded.size(); i-- > 0;)
    {
        if (StopFunction stop = state.loaded[i].stop)
        {
            state.loaded[i].stop = nullptr;
            stop();
        }
    }
}

uint32_t moduleCount()
{
    Modules& state = modules();
    std::lock_guard lock(state.mutex);
    return static_cast<uint32_t>(state.loaded.size());
}

std::string_view modulePath(uint32_t index)
{
    Modules& state = modules();
    std::lock_guard lock(state.mutex);
    return index < state.loaded.size() ? viewOf(state.loaded[index].path) : std::string_view();
}

} // namespace crucible
