#include <utility>
#include "ManagedObjectHandle.h"
#include "ScriptingEngine.h"

namespace crucible
{
    ManagedObjectHandle::~ManagedObjectHandle()
    {
        ScriptingEngine::_freeUnmanagedGCHandleFunction_fn_ptr(_gcHandle);
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