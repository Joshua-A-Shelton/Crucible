#include "Engine.h"
#include <slag/SlagLib.h>
#include <SDL.h>
#include <mono/jit/jit.h>
#include <mono/metadata/assembly.h>
#include <boost/filesystem.hpp>
#include <boost/dll.hpp>

using namespace slag;
namespace crucible
{
    MonoDomain* rootDomain = nullptr;
    MonoDomain* appDomain = nullptr;
    void Engine::initialize()
    {
        SDL_Init(SDL_INIT_EVERYTHING);
        SlagInitDetails details;
        details.backend = VULKAN;
        SlagLib::initialize(details);

        auto path = boost::dll::program_location().parent_path().string();
        std::replace( path.begin(), path.end(), '/', '\\');

        //mono
        mono_set_dirs(path.c_str(), path.c_str());
        // Initialise mono runtime
        rootDomain = mono_jit_init("Crucible");


        // Create your app domain, this is what stores your loaded assemblies in memory.
        //  Second parameter is optional configuration file path
        char name[]="CrucibleRuntime";
        appDomain = mono_domain_create_appdomain(&name[0], nullptr);
        assert(appDomain && "Unable to start mono runtime");

        // Tell mono to set this domain as the default one.
        // Second parameter forces this domain to be used
        mono_domain_set(appDomain, true);
    }

    void Engine::cleanup()
    {
        mono_jit_cleanup(rootDomain);
        rootDomain = nullptr;
        appDomain = nullptr;
        SlagLib::cleanup();
    }
} // crucible