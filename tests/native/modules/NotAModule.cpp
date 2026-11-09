// A shared library without a module's entry points, for ModuleTests.

#if defined(_WIN32)
extern "C" __declspec(dllexport) int notAModule()
#else
extern "C" __attribute__((visibility("default"))) int notAModule()
#endif
{
    return 1;
}
