#include <utility>
#include "ManagedObjectHandle.h"
#include "Interop.h"

namespace crucible
{
    ManagedObjectHandle::~ManagedObjectHandle()
    {
        managedFunctionPointers.freeUnmanagedGCHandle(_gcHandle);
    }

    void ManagedObjectHandle::move(ManagedObjectHandle& from)
    {
        std::swap(_gcHandle,from._gcHandle);
    }

    ManagedObjectHandle::ManagedObjectHandle(ManagedObjectHandle&& from)
    {
        move(from);
    }

    ManagedObjectHandle& ManagedObjectHandle::operator=(ManagedObjectHandle&& from)
    {
        move(from);
        return *this;
    }

} // crucible