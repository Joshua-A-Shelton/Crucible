#include "ScriptingEngine.h"

#include <cassert>
#include <filesystem>
#include <hostfxr.h>
#include <coreclr_delegates.h>
#include <iostream>
#include <nethost.h>


#ifdef WIN32
#include <Windows.h>

#define STR(s) L ## s
#define CH(c) L ## c
#define DIR_SEPARATOR "\\"

#define string_compare wcscmp

#else
#include <dlfcn.h>
#include <linux/limits.h>

#define STR(s) s
#define CH(c) c
#define DIR_SEPARATOR '/'
#define MAX_PATH PATH_MAX

#define string_compare strcmp

#endif

namespace crucible
{
    namespace scripting
    {
        hostfxr_initialize_for_runtime_config_fn init_for_config_fptr = nullptr;
        hostfxr_get_runtime_delegate_fn get_delegate_fptr = nullptr;
        hostfxr_close_fn close_fptr = nullptr;
        hostfxr_handle host_handle = nullptr;
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

        load_assembly_and_get_function_pointer_fn getDotNetLoadAssembly(const char_t *config_path)
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


        CSharpFunctionPointers& ScriptingEngine::funcPointers()
        {
            return _functionPointers;
        }

        bool ScriptingEngine::initialize()
        {
            // This sample assumes the managed assembly to load and its runtime configuration file are next to the host
            auto executableDirectory = std::filesystem::current_path();

            if(!loadHostFXR())
            {
                std::cout << "Unable to load hostfxr library" << std::endl;
                return false;
            }
            load_assembly_and_get_function_pointer_fn load_assembly_and_get_function_pointer = getDotNetLoadAssembly(platformString(executableDirectory.string() + DIR_SEPARATOR+ "config"+DIR_SEPARATOR+"CSharpConfig.json").c_str());

            component_entry_point_fn initFunc = nullptr;
            load_assembly_and_get_function_pointer(
                    platformString(executableDirectory.string() + DIR_SEPARATOR + "Crucible-Runtime.dll").c_str(),
                    //              namespace.class, dll name
                    platformString("Crucible.Initialization.Initializer, Crucible-Runtime").c_str(),
                    platformString("RuntimeEntry").c_str(),
                    nullptr,
                    nullptr,
                    (void**)(&initFunc)
            );

            if (initFunc == nullptr)
            {
                std::cout << "Unable to load Crucible-Runtime.dll" << std::endl;
                return false;
            }

            auto init = initFunc(&_functionPointers,sizeof(CSharpFunctionPointers));
            if (init!=0)
            {
                std::cout << "Unable to load function pointers from Crucible-Runtime.dll" << std::endl;
                return false;
            }

            return true;
        }

        void ScriptingEngine::cleanup()
        {
            _functionPointers.unloadAllContexts();
            if (host_handle)
            {
                close_fptr(host_handle);
            }
        }

        ManagedType ScriptingEngine::getManagedType(const char* typeName)
        {
            ManagedType type{};
            _functionPointers.getManagedType(typeName, type);
            return type;
        }

        ManagedInstance ScriptingEngine::createManagedInstance(ManagedType& type, int32_t parameterCount,ManagedType* parameterTypes, void** parameters)
        {
            void* instance = nullptr;
            _functionPointers.newInstance(type,parameterCount,parameterTypes,parameters,&instance);
            return ManagedInstance(instance);
        }

        void ScriptingEngine::freeInstance(void* instance)
        {
            _functionPointers.freeInstance(instance);
        }

        void ScriptingEngine::gameManagerInitialize()
        {
            _functionPointers.gameManagerInitialize();
        }

        void ScriptingEngine::gameManagerCleanUp()
        {
            _functionPointers.gameManagerCleanUp();
        }
    } // scripting
} // crucible