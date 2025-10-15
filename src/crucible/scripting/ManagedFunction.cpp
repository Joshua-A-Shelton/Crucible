#include "ManagedFunction.h"
#include "ScriptingEngine.h"
#include "slag/backends/vulkan/core/VulkanSwapChain.h"

namespace crucible
{
    namespace scripting
    {
        ManagedFunctionInternals::~ManagedFunctionInternals()
        {
            if (_functionPointer)
            {
                ScriptingEngine::freeInstance(_functionGCHandle);
            }
        }

        ManagedFunctionInternals::ManagedFunctionInternals(ManagedFunctionInternals&& from)
        {
            move(from);
        }

        ManagedFunctionInternals& ManagedFunctionInternals::operator=(ManagedFunctionInternals&& from)
        {
            move(from);
            return *this;
        }

        void ManagedFunctionInternals::move(ManagedFunctionInternals& from)
        {
            std::swap(_functionPointer, from._functionPointer);
            std::swap(_functionGCHandle, from._functionGCHandle);
        }
    } // scripting
} // crucible
