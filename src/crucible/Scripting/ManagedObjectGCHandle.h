#ifndef CRUCIBLE_MANAGEDOBJECTGCHANDLE_H
#define CRUCIBLE_MANAGEDOBJECTGCHANDLE_H

namespace crucible
{
    class ManagedObjectGCHandle
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