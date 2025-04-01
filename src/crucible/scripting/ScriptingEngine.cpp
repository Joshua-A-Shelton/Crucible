#include "ScriptingEngine.h"
#include <cassert>
#include <stdexcept>
#include <nethost.h>
#include <filesystem>
#include "CoreFunctions.h"
#include "crucible/core/scenes/Node.h"
#include "crucible/core/Transform.h"
#ifdef WIN32
#include <Windows.h>

#define STR(s) L ## s
#define CH(c) L ## c
#define DIR_SEPARATOR "\\"

#define string_compare wcscmp

#else
#include <dlfcn.h>


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


        void ScriptingEngine::initialize()
        {
            // This sample assumes the managed assembly to load and its runtime configuration file are next to the host
            auto executableDirectory = std::filesystem::current_path();

            loadHostFXR();
            load_assembly_and_get_function_pointer_fn load_assembly_and_get_function_pointer = get_dotnet_load_assembly(platformString(executableDirectory.string() + DIR_SEPARATOR+ "config"+DIR_SEPARATOR+"CSharpConfig.json").c_str());

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

            initFunc(&Interop::managedFunctionPointers,sizeof(ManagedFunctionPointers));

            //register unmanaged functions
            CoreFunctionsInitializer initialize;
            //register managed functions
            registerManagedFunctions();

        }

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
            init_for_config_fptr = (hostfxr_initialize_for_runtime_config_fn)get_export(lib, "hostfxr_initialize_for_runtime_config");
            get_delegate_fptr = (hostfxr_get_runtime_delegate_fn)get_export(lib, "hostfxr_get_runtime_delegate");
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

            //close_fptr(cxt);
            host_handle = cxt;
            return (load_assembly_and_get_function_pointer_fn)load_assembly_and_get_function_pointer;
        }

        void ScriptingEngine::registerUnmanagedFunction(const std::string& assemblyQualifiedClassName,const std::string &managedDelegateName, void **functionPointer)
        {
            auto className = platformString(assemblyQualifiedClassName);
            auto delegate = platformString(managedDelegateName);
            FunctionMapping mapping{className.c_str(),delegate.c_str(),functionPointer};
            Interop::managedFunctionPointers.registerUnmanagedFunction(mapping);
        }

        ManagedType ScriptingEngine::getManagedType(const std::string& typeName)
        {
            ManagedType m;
            Interop::managedFunctionPointers.getType(typeName.c_str(),m);
            return m;
        };

        void* ScriptingEngine::getManagedFunction(ManagedType& type, const std::string& fullyQualifiedFunctionName)
        {
            void* fptr = nullptr;
            Interop::managedFunctionPointers.getFunctionPointer(type,fullyQualifiedFunctionName.c_str(),&fptr);
            return fptr;
        }

        void ScriptingEngine::InvokeInstanceMethod(ManagedInstance& instance, const std::string& methodName, int32_t parameterCount, void** parameterTypes, void** parameters)
        {
            auto type = instance.getType();
            Interop::managedFunctionPointers.invokeInstanceMethod(&type, instance.gcHandle(),methodName.c_str(),parameterCount,parameterTypes,parameters);
        }

        ManagedInstance ScriptingEngine::InvokeInstanceMethodWithReturnObject(ManagedInstance& instance,const std::string& methodName, int32_t parameterCount, void** parameterTypes, void** parameters)
        {
            auto type = instance.getType();
            void* objectInstance = nullptr;
            ManagedType mt;
            Interop::managedFunctionPointers.invokeInstanceMethodReturnReference(&type,instance.gcHandle(),methodName.c_str(),parameterCount,parameterTypes,parameters,&objectInstance,mt);
            return ManagedInstance(mt,objectInstance);
        }

        void ScriptingEngine::InvokeInstanceMethodWithReturnValue(ManagedInstance& instance,const std::string& methodName, int32_t parameterCount, void** parameterTypes, void** parameters,void* valuePtr)
        {
            auto type = instance.getType();
            Interop::managedFunctionPointers.invokeInstanceMethodReturnValue(&type,instance.gcHandle(),methodName.c_str(),parameterCount,parameterTypes,parameters,valuePtr);
        }

        void ScriptingEngine::loadManagedDll(const char* contextName,const char* path, bool collectible)
        {
            Interop::managedFunctionPointers.loadLibrary(contextName,path,collectible);
        }

        void ScriptingEngine::unloadManagedDllContext(const char* contextName)
        {
            Interop::managedFunctionPointers.unloadLibrary(contextName);
        }

        ManagedInstance ScriptingEngine::newInstance(const ManagedType& type)
        {
            void* handle = nullptr;
            Interop::managedFunctionPointers.newInstance((void*) &type, &handle);
            return ManagedInstance(type,handle);
        }

        void ScriptingEngine::freeGCHandle(void* handle)
        {
            Interop::managedFunctionPointers.freeUnmanagedGCHandle(handle);
        }

        void ScriptingEngine::getMeshRenderData(ManagedInstance& managedMeshRendererInstance, core::Mesh** mesh, core::Material** material, unsigned char* priority)
        {
            Interop::managedFunctionPointers.getMeshRenderData(managedMeshRendererInstance.gcHandle(),mesh,material,priority);
        }

        core::Camera* ScriptingEngine::getCamera(ManagedInstance& managedInstance)
        {
            core::Camera* camera;
            Interop::managedFunctionPointers.getCamera(managedInstance.gcHandle(),&camera);
            return camera;
        }

        void ScriptingEngine::registerManagedFunctions()
        {
            ManagedType nodeBehaviorType = getManagedType("Crucible.Core.NodeBehavior");
            core::Node::update = nodeBehaviorType.getFunction<void(*)(void*, double)>("UpdateNode");
            assert(core::Node::update != nullptr && "Node::update is null");
        }


        void ScriptingEngine::cleanup()
        {
            auto gameManagerType = scripting::ScriptingEngine::getManagedType("Crucible.Core.GameManager");
            auto cleanup = gameManagerType.getFunction<void(*)()>("CleanUp");
            cleanup();

            Interop::managedFunctionPointers.unloadAllContexts();
            if (host_handle)
            {
                close_fptr(host_handle);
            }
        }


    } // scripting
} // crucible