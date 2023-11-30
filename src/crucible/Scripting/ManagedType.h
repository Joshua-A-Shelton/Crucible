#ifndef CRUCIBLE_MANAGEDTYPE_H
#define CRUCIBLE_MANAGEDTYPE_H

#include "../Crucible.h"

namespace crucible
{
    class CRUCIBLE_EXPORT ManagedType
    {
    private:
        void* runtimeTypeHandle = nullptr;
    };
} // crucible
#endif //CRUCIBLE_MANAGEDTYPE_H