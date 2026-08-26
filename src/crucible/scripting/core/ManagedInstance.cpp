#include "ManagedInstance.h"

#include "crucible/scripting/exceptions/InvalidManagedResourceException.h"

namespace crucible
{
    namespace scripting
    {
        ManagedInstance::ManagedInstance(ManagedType type)
        {
            _gcHandle = _managedCreateInstance(type);
            if (_gcHandle == nullptr)
            {
                throw InvalidManagedResourceException("No default constructor for the given type exists");
            }
        }

        ManagedInstance::ManagedInstance(ManagedType type, int32_t parameterCount, ManagedType* parameterTypes,
            void** parameterValues)
        {
            _gcHandle = _managedCreateInstanceWithParams(type,parameterCount,parameterTypes,parameterValues);
            if (_gcHandle == nullptr)
            {
                throw InvalidManagedResourceException("No constructor for the given type exists with the given overloads");
            }
        }

        ManagedInstance::~ManagedInstance()
        {
            if (_gcHandle != nullptr)
            {
                _managedFreeInstance(_gcHandle);
            }
        }

        ManagedInstance::ManagedInstance(ManagedInstance&& from) noexcept
        {
            move(from);
        }

        ManagedInstance& ManagedInstance::operator=(ManagedInstance&& from) noexcept
        {
            move(from);
            return *this;
        }

        void ManagedInstance::move(ManagedInstance& from)
        {
            std::swap(_gcHandle, from._gcHandle);
        }
    } // scripting
} // crucible