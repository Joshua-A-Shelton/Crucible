#include "ManagedFunctionDelegate.h"
#include "ScriptingEngine.h"

namespace crucible
{
    namespace scripting
    {
        ManagedFunctionDelegateInternals::~ManagedFunctionDelegateInternals()
        {
            if (_functionPointer)
            {
                ScriptingEngine::freeInstance(_functionGCHandle);
            }
        }

        ManagedFunctionDelegateInternals::ManagedFunctionDelegateInternals(ManagedFunctionDelegateInternals&& from)
        {
            move(from);
        }

        ManagedFunctionDelegateInternals& ManagedFunctionDelegateInternals::operator=(ManagedFunctionDelegateInternals&& from)
        {
            move(from);
            return *this;
        }

        void ManagedFunctionDelegateInternals::move(ManagedFunctionDelegateInternals& from)
        {
            std::swap(_functionPointer, from._functionPointer);
            std::swap(_functionGCHandle, from._functionGCHandle);
        }
    } // scripting
} // crucible
