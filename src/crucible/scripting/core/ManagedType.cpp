#include "ManagedType.h"

#include "crucible/scripting/exceptions/InvalidManagedResourceException.h"

namespace crucible
{
    namespace scripting
    {
        ManagedType ManagedType::get(const std::string& typeIdentifier)
        {
            ManagedType managedType{};
            void* typePointer = _managedGetType(typeIdentifier.c_str());
            if (typePointer == nullptr)
            {
                throw InvalidManagedResourceException("No type \"" + typeIdentifier +"\" could be found in the loaded managed runtime");
            }
            managedType._runtimeTypeHandle = typePointer;
            return managedType;
        }

        void* ManagedType::typeHandle() const
        {
            return _runtimeTypeHandle;
        }

        bool ManagedType::operator==(const ManagedType& against) const
        {
            return _runtimeTypeHandle == against._runtimeTypeHandle;
        }

        bool ManagedType::operator!=(const ManagedType& against) const
        {
            return _runtimeTypeHandle != against._runtimeTypeHandle;
        }
    } // scripting
} // crucible