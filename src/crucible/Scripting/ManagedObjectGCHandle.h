#ifndef CRUCIBLE_MANAGEDOBJECTGCHANDLE_H
#define CRUCIBLE_MANAGEDOBJECTGCHANDLE_H

#include "../Crucible.h"

namespace crucible
{
    class CRUCIBLE_EXPORT ManagedObjectGCHandle
    {
    public:
        ~ManagedObjectGCHandle();
        ManagedObjectGCHandle(const ManagedObjectGCHandle&)=delete;
        ManagedObjectGCHandle& operator=(const ManagedObjectGCHandle&)=delete;
        ManagedObjectGCHandle(ManagedObjectGCHandle&& from);
        ManagedObjectGCHandle& operator=(ManagedObjectGCHandle&& from);
        void free();
    private:
        void* _gcHandle = nullptr;
        void move(ManagedObjectGCHandle&);
    };
} // crucible
#endif //CRUCIBLE_MANAGEDOBJECTGCHANDLE_H