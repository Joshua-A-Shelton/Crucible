#include "ManagedInstance.h"

#include "ScriptingEngine.h"

namespace crucible
{
    namespace scripting
    {
        ManagedInstance::~ManagedInstance()
        {
            if (_gcHandle)
            {
                ScriptingEngine::freeInstance(_gcHandle);
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

        void ManagedInstance::invokeMethod(const char* method, int32_t parameterCount, ManagedType* parameterTypes,void** parameterValues)
        {
            ScriptingEngine::invokeInstanceMethod(*this,method,parameterCount,parameterTypes,parameterValues);
        }

        ManagedInstance ManagedInstance::invokeMethodReturn(const char* method, int32_t parameterCount,ManagedType* parameterTypes, void** parameterValues)
        {
            return ScriptingEngine::invokeInstanceMethodWithReturnObject(*this,method,parameterCount,parameterTypes,parameterValues);
        }

        void ManagedInstance::invokeMethodReturn(const char* method, int32_t parameterCount,ManagedType* parameterTypes, void** parameterValues, void* outValue)
        {
            ScriptingEngine::invokeInstanceMethodWithReturnValue(*this,method,parameterCount,parameterTypes,parameterValues,outValue);
        }

        bool ManagedInstance::isNull() const
        {
            return _gcHandle == nullptr;
        }

        ManagedInstance::ManagedInstance(void* gcHandle)
        {
            _gcHandle = gcHandle;
        }

        void ManagedInstance::move(ManagedInstance& from)
        {
            std::swap(_gcHandle, from._gcHandle);
        }
    } // scripting
} // crucible
