#ifndef CRUCIBLE_MANAGEDTYPE_H
#define CRUCIBLE_MANAGEDTYPE_H

namespace crucible
{
    class ManagedType
    {
    private:
        void* runtimeTypeHandle = nullptr;
    };
} // crucible
#endif //CRUCIBLE_MANAGEDTYPE_H