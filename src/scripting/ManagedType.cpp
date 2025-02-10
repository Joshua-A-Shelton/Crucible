#include "ManagedType.h"
#include "ScriptingEngine.h"
namespace crucible
{
    namespace scripting
    {
        void* ManagedType::_getFunction(const std::string& functionName)
        {
            return ScriptingEngine::getManagedFunction(*this,functionName);
        }
    }
}