#ifndef CRUCIBLE_MANAGEDFUNCTION_H
#define CRUCIBLE_MANAGEDFUNCTION_H

namespace crucible
{
    class ManagedFunction
    {
    private:
        void* _gcHandle = nullptr;
    };
} // crucible
#endif //CRUCIBLE_MANAGEDFUNCTION_H