#ifndef CRUCIBLE_MANAGEDFUNCTION_H
#define CRUCIBLE_MANAGEDFUNCTION_H

#include "../Crucible.h"

namespace crucible
{
    class CRUCIBLE_EXPORT ManagedFunction
    {
    private:
        void* _gcHandle = nullptr;
    };
} // crucible
#endif //CRUCIBLE_MANAGEDFUNCTION_H