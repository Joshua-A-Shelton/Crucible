#include "ScriptingEngine.h"
#include <nethost/hostfxr.h>
#include <nethost/nethost.h>
#include <nethost/coreclr_delegates.h>
#include <boost/filesystem.hpp>

#ifdef WIN32
#include <Windows.h>

#define STR(s) L ## s
#define CH(c) L ## c
#define DIR_SEPARATOR "\\"

#define string_compare wcscmp

#else
#include <dlfcn.h>
#include <limits.h>
#include <cassert>
#include <stdexcept>

#define STR(s) s
#define CH(c) c
#define DIR_SEPARATOR '/'
#define MAX_PATH PATH_MAX

#define string_compare strcmp

#endif

namespace crucible
{

    hostfxr_initialize_for_dotnet_command_line_fn init_for_cmd_line_fptr = nullptr;
    hostfxr_initialize_for_runtime_config_fn init_for_config_fptr = nullptr;
    hostfxr_get_runtime_delegate_fn get_delegate_fptr = nullptr;
    hostfxr_run_app_fn run_app_fptr = nullptr;
    hostfxr_close_fn close_fptr = nullptr;


    // Forward declarations
    void *load_library(const char_t *);
    void *get_export(void *, const char *);

#ifdef WIN32
    void *load_library(const char_t *path)
    {
        HMODULE h = ::LoadLibraryW(path);
        assert(h != nullptr);
        return (void*)h;
    }
    void *get_export(void *h, const char *name)
    {
        void *f = ::GetProcAddress((HMODULE)h, name);
        assert(f != nullptr);
        return f;
    }
    std::wstring platformString(const std::string &multi)
    {
        std::wstring wide; wchar_t w; mbstate_t mb {};
        size_t n = 0, len = multi.length () + 1;
        while (auto res = mbrtowc (&w, multi.c_str () + n, len - n, &mb)) {
            if (res == size_t (-1) || res == size_t (-2))
                throw "invalid encoding";

            n += res;
            wide += w;
        }
        return wide;
    }
#else
    void *load_library(const char_t *path)
    {
        void *h = dlopen(path, RTLD_LAZY | RTLD_LOCAL);
        assert(h != nullptr);
        return h;
    }
    void *get_export(void *h, const char *name)
    {
        void *f = dlsym(h, name);
        assert(f != nullptr);
        return f;
    }

    std::string& platformString(std::string& multi)
    {
        return multi;
    }
#endif


    void ScriptingEngine::initialize()
    {
        // This sample assumes the managed assembly to load and its runtime configuration file are next to the host
        auto executableDirectory = boost::filesystem::current_path();

        loadHostFXR();
        load_assembly_and_get_function_pointer_fn load_assembly_and_get_function_pointer = get_dotnet_load_assembly(platformString(executableDirectory.string() + DIR_SEPARATOR+ "config"+DIR_SEPARATOR+"CSharpConfig.json").c_str());

        component_entry_point_fn hello = nullptr;
        load_assembly_and_get_function_pointer(
            platformString(executableDirectory.string() + DIR_SEPARATOR + "Crucible.dll").c_str(),
            //              namespace.class, dll name
            platformString("Crucible.Lib, Crucible").c_str(),
            platformString("Hello").c_str(),
            nullptr,
            nullptr,
            (void**)(&hello)
        );

        struct lib_args
        {
            const char_t *message;
            int number;
        };
        for (int i = 0; i < 3; ++i)
        {
            // <SnippetCallManaged>
            lib_args args
                    {
                            STR("from host!"),
                            i
                    };

            hello(&args, sizeof(args));
            // </SnippetCallManaged>
        }
        int i=0;
    };

    bool ScriptingEngine::loadHostFXR()
    {
        // Pre-allocate a large buffer for the path to hostfxr
        char_t buffer[MAX_PATH];
        size_t buffer_size = sizeof(buffer) / sizeof(char_t);
        int rc = get_hostfxr_path(buffer, &buffer_size, nullptr);
        if (rc != 0)
            return false;

        // Load hostfxr and get desired exports
        void *lib = load_library(buffer);
        init_for_cmd_line_fptr = (hostfxr_initialize_for_dotnet_command_line_fn)get_export(lib, "hostfxr_initialize_for_dotnet_command_line");
        init_for_config_fptr = (hostfxr_initialize_for_runtime_config_fn)get_export(lib, "hostfxr_initialize_for_runtime_config");
        get_delegate_fptr = (hostfxr_get_runtime_delegate_fn)get_export(lib, "hostfxr_get_runtime_delegate");
        run_app_fptr = (hostfxr_run_app_fn)get_export(lib, "hostfxr_run_app");
        close_fptr = (hostfxr_close_fn)get_export(lib, "hostfxr_close");

        return (init_for_config_fptr && get_delegate_fptr && close_fptr);
    }

    // Load and initialize .NET Core and get desired function pointer for scenario
    load_assembly_and_get_function_pointer_fn ScriptingEngine::get_dotnet_load_assembly(const char_t *config_path)
    {
        // Load .NET Core
        void *load_assembly_and_get_function_pointer = nullptr;
        hostfxr_handle cxt = nullptr;
        int rc = init_for_config_fptr(config_path, nullptr, &cxt);

        if (rc != 0 || cxt == nullptr)
        {
            close_fptr(cxt);
            throw std::runtime_error("Init failed");
        }

        // Get the load assembly function pointer
        rc = get_delegate_fptr(
                cxt,
                hdt_load_assembly_and_get_function_pointer,
                &load_assembly_and_get_function_pointer);
        if (rc != 0 || load_assembly_and_get_function_pointer == nullptr)
        {
            close_fptr(cxt);
            throw std::runtime_error("Get delegate failed:");
        }

        close_fptr(cxt);
        return (load_assembly_and_get_function_pointer_fn)load_assembly_and_get_function_pointer;
    }

    void ScriptingEngine::cleanup()
    {

    }
} // crucible