// A native module for ModuleTests (ADR 0074): registers a system as it
// starts and removes it as it stops.

#include <crucible/Module.h>
#include <crucible/Systems.h>

namespace {

void nothing(const crucible::loop::SystemContext&, void*) {}

} // namespace

CRUCIBLE_MODULE_EXPORT bool crucibleModuleStart()
{
    crucible::linkEngineComponents();
    crucible::loop::registerSystem({.name = "test.ModuleSystem", .phase = "Update", .function = nothing});
    return true;
}

CRUCIBLE_MODULE_EXPORT void crucibleModuleStop()
{
    crucible::loop::unregisterSystem("test.ModuleSystem");
}
