#include "ManagedInstance.h"
#include "ScriptingEngine.h"
namespace crucible
{
    namespace scripting
    {
        ManagedInstance::ManagedInstance(void* gcHandle)
        {
            _gcHandle = gcHandle;
        }

        ManagedInstance::~ManagedInstance()
        {
            if(_gcHandle)
            {
                ScriptingEngine::freeGCHandle(_gcHandle);
            }
        }

        ManagedInstance::ManagedInstance(ManagedInstance&& from)
        {
            move(from);
        }

        ManagedInstance& ManagedInstance::operator=(ManagedInstance&& from)
        {
            move(from);
            return *this;
        }

        void ManagedInstance::move(ManagedInstance& from)
        {
            std::swap(_gcHandle,from._gcHandle);
        }
    } // scripting
} // crucible