#ifndef CRUCIBLE_MANAGEDTYPE_H
#define CRUCIBLE_MANAGEDTYPE_H
#include <cstdint>

#include "crucible/CrucibleCore.h"

namespace crucible
{
    namespace scripting
    {
        class CRUCIBLE_API ManagedType
        {
        private:
            void* _handle = nullptr;
        };
    } // scripting
} // crucible

#endif //CRUCIBLE_MANAGEDTYPE_H
