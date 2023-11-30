#ifndef CRUCIBLE_ENGINE_H
#define CRUCIBLE_ENGINE_H
#include "Crucible.h"

namespace crucible
{
    class CRUCIBLE_EXPORT Engine
    {
    public:
        static void initialize();
        static void cleanup();
    };
} // crucible
#endif //CRUCIBLE_ENGINE_H