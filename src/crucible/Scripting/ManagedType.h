#ifndef CRUCIBLE_MANAGEDTYPE_H
#define CRUCIBLE_MANAGEDTYPE_H

#include "../Crucible.h"
#include "ScriptingEngine.h"

namespace crucible
{
    class CRUCIBLE_EXPORT ManagedType
    {
    private:
        void* runtimeTypeHandle = nullptr;
    public:
        template<class T>
        T getFunction(const std::string& functionName)
        {
            return static_cast<T>(ScriptingEngine::getManagedFunction(this,functionName));
        }
    };
} // crucible
#endif //CRUCIBLE_MANAGEDTYPE_H