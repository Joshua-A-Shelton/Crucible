#ifndef CRUCIBLE_MANAGEDOBJECTHANDLE_H
#define CRUCIBLE_MANAGEDOBJECTHANDLE_H

namespace crucible
{
    class ManagedObjectHandle
    {
    public:
        ~ManagedObjectHandle();
        ManagedObjectHandle(const ManagedObjectHandle&)=delete;
        ManagedObjectHandle& operator=(const ManagedObjectHandle&)=delete;
        ManagedObjectHandle(ManagedObjectHandle&& from);
        ManagedObjectHandle& operator=(ManagedObjectHandle&& from);
    private:
        void* _gcHandle = nullptr;
        void move(ManagedObjectHandle&);
    };
} // crucible
#endif //CRUCIBLE_MANAGEDOBJECTHANDLE_H