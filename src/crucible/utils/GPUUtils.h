#ifndef CRUCIBLE_GPUUTILS_H
#define CRUCIBLE_GPUUTILS_H
#include <vector>

#include <slag/Slag.h>

namespace crucible
{
    namespace utils
    {
        void copyBufferToArray(slag::Buffer* buffer, void* array);
    } // utils
} // crucible

#endif //CRUCIBLE_GPUUTILS_H
