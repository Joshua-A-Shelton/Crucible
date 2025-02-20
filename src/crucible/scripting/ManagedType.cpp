#include "ManagedType.h"
#include "ScriptingEngine.h"
namespace crucible
{
    namespace scripting
    {
        ManagedInstance ManagedType::createNew()
        {
            auto gcHandle = scripting::ScriptingEngine::newInstance(*this);
            return ManagedInstance(gcHandle);
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

    }
}