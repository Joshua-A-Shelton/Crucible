#include "ManagedInstance.h"
#include "ScriptingEngine.h"
namespace crucible
{
    namespace scripting
    {
        ManagedInstance::ManagedInstance()
        {
            _gcHandle = nullptr;
        }
        ManagedInstance::ManagedInstance(const ManagedType& type, void* gcHandle)
        {
            _type = type;
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

        void ManagedInstance::invokeMethod(const std::string& method, int32_t parameterCount, ManagedType* parameterTypes, void** parameterValues)
        {
            scripting::ScriptingEngine::InvokeInstanceMethod(*this,method,parameterCount,(void**)parameterTypes,parameterValues);
        }

        ManagedInstance ManagedInstance::invokeMethodReturn(const std::string& method, int32_t parameterCount,ManagedType* parameterTypes, void** parameterValues)
        {
            return scripting::ScriptingEngine::InvokeInstanceMethodWithReturnObject(*this,method,parameterCount,(void**)parameterTypes,parameterValues);
        }

        void ManagedInstance::invokeMethodReturn(const std::string& method, int32_t parameterCount,ManagedType* parameterTypes, void** parameterValues, void* outValue)
        {
            scripting::ScriptingEngine::InvokeInstanceMethodWithReturnValue(*this,method,parameterCount,(void**)parameterTypes,parameterValues,outValue);
        }

        bool ManagedInstance::isNull() const
        {
            return _gcHandle == nullptr;
        }

        ManagedType ManagedInstance::getType() const
        {
            return _type;
        }

        void* ManagedInstance::gcHandle() const
        {
            return _gcHandle;
        }

        void ManagedInstance::move(ManagedInstance& from)
        {
            _type = from._type;
            std::swap(_gcHandle,from._gcHandle);
        }
    } // scripting
} // crucible