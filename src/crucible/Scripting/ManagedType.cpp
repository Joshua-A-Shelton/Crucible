#include "ManagedType.h"
#include "ScriptingEngine.h"
namespace crucible
{
    void *ManagedType::_getFunction(const std::string &functionName)
    {
        return ScriptingEngine::getManagedFunction(*this,functionName);
    }
} // crucible