#ifndef CRUCIBLE_ENGINE_H
#define CRUCIBLE_ENGINE_H
#include "Window.h"

namespace crucible
{
    class Engine
    {
    public:
        static void initialize();
        static void cleanup();
    };
} // crucible
#endif //CRUCIBLE_ENGINE_H