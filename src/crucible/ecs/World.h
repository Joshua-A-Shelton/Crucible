#ifndef CRUCIBLE_WORLD_H
#define CRUCIBLE_WORLD_H
#include <flecs.h>
namespace crucible
{
    class Node;
    namespace ecs
    {
        struct EngineDefinedTypes
        {
            ecs_entity_t Node = 0;
            ecs_entity_t Transform = 0;
        };
        void registerEngineDefinedTypes();
        const EngineDefinedTypes& engineTypes();
        ecs_entity_t registerOrRetrieveDataType(const char* typeName, size_t typeSize, size_t typeAlignment);
        ecs_entity_t registerOrRetrieveReferenceType(const char* typeName);
        flecs::world& world();
    } // ecs
} // crucible

#endif //CRUCIBLE_WORLD_H
