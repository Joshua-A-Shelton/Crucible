#ifndef CRUCIBLE_SCRIPTINGMODULE_H
#define CRUCIBLE_SCRIPTINGMODULE_H
#include <crucible/Crucible.h>
#include "DLLLoader.h"
#include "ManagedType.h"
#include "ManagedInstance.h"
namespace crucible
{
    namespace scripting
    {
        struct ScriptingModule
        {
        public:
            CrucibleModuleID moduleId = CrucibleModuleID::SCRIPTING;
            ManagedLoadAssembly* managedLoadAssembly = &DLLLoader::_managedLoadAssembly;
            ManagedUnloadAssembly* managedUnloadAssembly = &DLLLoader::_managedUnloadAssembly;
            ManagedUnloadAllContexts* managedUnloadAllContexts = &DLLLoader::_managedUnloadAllContexts;
            ManagedGetType* managedGetType = &ManagedType::_managedGetType;
            ManagedCreateInstance* managedCreateInstance = &ManagedInstance::_managedCreateInstance;
            ManagedCreateInstanceWithParams* managedCreateInstanceWithParams = &ManagedInstance::_managedCreateInstanceWithParams;
            ManagedFreeInstance* managedFreeInstance = &ManagedInstance::_managedFreeInstance;
        };
    } // scripting
} // crucible

#endif //CRUCIBLE_SCRIPTINGMODULE_H