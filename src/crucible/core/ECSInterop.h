#ifndef CRUCIBLE_ECSINTEROP_H
#define CRUCIBLE_ECSINTEROP_H
#include "flecs.h"

namespace crucible
{
    namespace core
    {
        struct InteropTypes
        {
            ecs_entity_t MeshRenderer;
            ecs_entity_t Node;
            ecs_entity_t Skeleton;
            ecs_entity_t Transform;
        };
        class ECSInterop
        {
        public:
            static void mapInteropTypes();
            static const InteropTypes& types();
        private:
            static InteropTypes _types;

        };
    } // core
} // crucible

#endif //CRUCIBLE_ECSINTEROP_H
