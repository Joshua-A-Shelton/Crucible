#include <utility>
#include "ManagedObjectGCHandle.h"
#include "Interop.h"

namespace crucible
{
    ManagedObjectGCHandle::~ManagedObjectGCHandle()
    {
        free();
    }

    void ManagedObjectGCHandle::move(ManagedObjectGCHandle& from)
    {
        std::swap(_gcHandle,from._gcHandle);
    }

    ManagedObjectGCHandle::ManagedObjectGCHandle(ManagedObjectGCHandle&& from)
    {
        move(from);
    }

    ManagedObjectGCHandle& ManagedObjectGCHandle::operator=(ManagedObjectGCHandle&& from)
    {
        move(from);
        return *this;
    }

    void ManagedObjectGCHandle::free()
    {
        if(_gcHandle)
        {
            Interop::managedFunctionPointers.freeUnmanagedGCHandle(_gcHandle);
            _gcHandle = nullptr;
        }
    }

} // crucible