#include "API_Scripting.h"
#include "crucible/Crucible.h"

#include <cassert>
#include <filesystem>

#include "core/Callbacks.h"

#ifndef CRUCIBLE_AOT_SCRIPTING
#include <hostfxr.h>
#include <coreclr_delegates.h>
#include <nethost.h>
#endif


#ifdef WIN32
#include <Windows.h>

#define STR(s) L ## s
#define CH(c) L ## c
#define DIR_SEPARATOR "\\"

#define string_compare wcscmp
#define char_t wchar_t

#else
#include <dlfcn.h>

#define STR(s) s
#define CH(c) c
#define DIR_SEPARATOR '/'
#define MAX_PATH PATH_MAX

#define string_compare strcmp
#define char_t char

#endif

namespace crucible
{
    namespace scripting
    {

        typedef int (*RuntimeEntry)(void*, int);

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
                    throw std::runtime_error("invalid encoding");

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

        std::string platformString(const std::string& multi)
        {
            return multi;
        }
#endif

        void ApplyScriptingCallbacks(const CallbackLocations& callbackLocations)
        {
            ScriptingInitializeCallback = callbackLocations.initialize;
            ScriptingUpdateCallback = callbackLocations.update;
            ScriptingCleanupCallback = callbackLocations.cleanup;
        }


#ifndef CRUCIBLE_AOT_SCRIPTING
        hostfxr_initialize_for_runtime_config_fn init_for_config_fptr = nullptr;
        hostfxr_get_runtime_delegate_fn get_delegate_fptr = nullptr;
        hostfxr_close_fn close_fptr = nullptr;
        hostfxr_handle host_handle = nullptr;


        bool loadHostFXR()
        {
            // Pre-allocate a large buffer for the path to hostfxr
            char_t buffer[MAX_PATH];
            size_t buffer_size = sizeof(buffer) / sizeof(char_t);
            int rc = get_hostfxr_path(buffer, &buffer_size, nullptr);
            if (rc != 0)
                return false;

            // Load hostfxr and get desired exports
            void *lib = load_library(buffer);
            init_for_config_fptr = (hostfxr_initialize_for_runtime_config_fn)get_export(lib, "hostfxr_initialize_for_runtime_config");
            get_delegate_fptr = (hostfxr_get_runtime_delegate_fn)get_export(lib, "hostfxr_get_runtime_delegate");
            close_fptr = (hostfxr_close_fn)get_export(lib, "hostfxr_close");

            return (init_for_config_fptr && get_delegate_fptr && close_fptr);
        }

        // Load and initialize .NET Core and get desired function pointer for scenario
        load_assembly_and_get_function_pointer_fn get_dotnet_load_assembly(const char_t *config_path)
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

            //close_fptr(cxt);
            host_handle = cxt;
            return (load_assembly_and_get_function_pointer_fn)load_assembly_and_get_function_pointer;
        }

        CrucibleInitializationResult initializeDynamicScriptingSubmodule(const std::filesystem::path& scriptingDLL)
        {
            auto executableDirectory = std::filesystem::current_path();

            loadHostFXR();
            load_assembly_and_get_function_pointer_fn load_assembly_and_get_function_pointer = nullptr;
            try
            {
                load_assembly_and_get_function_pointer = get_dotnet_load_assembly(platformString(executableDirectory.string() + DIR_SEPARATOR+ "config"+DIR_SEPARATOR+"CSharpConfig.json").c_str());
            }
            catch (...)
            {
                return CrucibleInitializationResult::UNABLE_TO_LOAD_DOTNET_CORE;
            }

            component_entry_point_fn initFunc = nullptr;
            auto typeName = std::string("Crucible.Interop, ") + scriptingDLL.stem().string();
            load_assembly_and_get_function_pointer(
                    platformString(scriptingDLL.string().c_str()).c_str(),
                    //              namespace.class, dll name
                    platformString(typeName).c_str(),
                    platformString("DynamicRuntimeEntry").c_str(),
                    nullptr,
                    nullptr,
                    (void**)(&initFunc)
            );

            if (initFunc == nullptr)
            {
                return CrucibleInitializationResult::UNABLE_TO_FIND_SCRIPTING_ENTRY_POINT;
            }

            CallbackLocations callbackLocations{};
            //call C# initialization function
            if (initFunc(&callbackLocations,sizeof(CallbackLocations))!=0)
            {
                return CrucibleInitializationResult::ERROR_IN_SCRIPTING_INITIALIZATION;
            }
            ApplyScriptingCallbacks(callbackLocations);
            return CrucibleInitializationResult::SUCCESS;
        }

#endif


        CrucibleInitializationResult initializeCompiledScriptingSubmodule(const std::filesystem::path& scriptingDLL)
        {
#ifdef _WIN32
            HMODULE scriptingLibrary = LoadLibraryA(scriptingDLL.string().c_str());
            if (scriptingLibrary)
            {
                RuntimeEntry initFunc = (RuntimeEntry)GetProcAddress(scriptingLibrary, "StaticRuntimeEntry");
                if (initFunc == nullptr)
                {
                    return CrucibleInitializationResult::UNABLE_TO_FIND_SCRIPTING_ENTRY_POINT;
                }

                CallbackLocations callbackLocations{};
                //call C# initialization function
                if (initFunc(&callbackLocations,sizeof(CallbackLocations))!=0)
                {
                    return CrucibleInitializationResult::ERROR_IN_SCRIPTING_INITIALIZATION;
                }
                ApplyScriptingCallbacks(callbackLocations);
                return CrucibleInitializationResult::SUCCESS;
            }
            else
            {
                return CrucibleInitializationResult::SCRIPTING_DLL_LOADING_FAILURE;
            }
#else
            void* handle = dlopen(scriptingDLL.string().c_str(), RTLD_NOW);
            if (handle == nullptr)
            {
                return CrucibleInitializationResult::UNABLE_TO_LOAD_DOTNET_CORE;
            }
            RuntimeEntry initFunc = (RuntimeEntry)dlsym(handle, "StaticRuntimeEntry");
            const char* dlsym_error = dlerror();
            if (dlsym_error != nullptr || initFunc == nullptr)
            {
                return CrucibleInitializationResult::UNABLE_TO_FIND_SCRIPTING_ENTRY_POINT;
            }
            CallbackLocations callbackLocations{};
            //call C# initialization function
            if (initFunc(&callbackLocations,sizeof(CallbackLocations))!=0)
            {
                return CrucibleInitializationResult::ERROR_IN_SCRIPTING_INITIALIZATION;
            }
            ApplyScriptingCallbacks(callbackLocations);
            return CrucibleInitializationResult::SUCCESS;
#endif


        }

        CrucibleInitializationResult initializeScriptingSubmodule(const std::filesystem::path& scriptingDLL)
        {
            CrucibleInitializationResult result = CrucibleInitializationResult::ERROR_IN_SCRIPTING_INITIALIZATION;
#ifdef CRUCIBLE_AOT_SCRIPTING
            result = initializeCompiledScriptingSubmodule(scriptingDLL);
#else
            result = initializeDynamicScriptingSubmodule(scriptingDLL);
#endif
            if (ScriptingInitializeCallback!=nullptr)
            {
                ScriptingInitializeCallback();
            }
            return result;
        }

        void cleanupScriptingSubmodule()
        {
            if (ScriptingCleanupCallback!=nullptr)
            {
                ScriptingCleanupCallback();
            }
#ifndef CRUCIBLE_AOT_SCRIPTING
            if (host_handle)
            {
                close_fptr(host_handle);
            }
#endif
        }
    }
}