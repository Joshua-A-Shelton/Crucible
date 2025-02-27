#include "ManagedType.h"
#include "ScriptingEngine.h"
#include "ManagedInstance.h"
namespace crucible
{
    namespace scripting
    {
        ManagedInstance ManagedType::createNew()const
        {
            return scripting::ScriptingEngine::newInstance(*this);
        }

        void* ManagedType::_getFunction(const std::string& functionName)
        {
            return ScriptingEngine::getManagedFunction(*this,functionName);
        }

        bool ManagedType::operator==(const ManagedType& against) const
        {
            return _runtimeTypeHandle == against._runtimeTypeHandle;
        }

        bool ManagedType::operator!=(const ManagedType& against) const
        {
            return _runtimeTypeHandle != against._runtimeTypeHandle;
        }

        void* ManagedType::runtimeTypeHandle() const
        {
            return _runtimeTypeHandle;
        }
    }
}