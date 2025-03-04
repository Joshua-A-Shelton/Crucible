#ifndef CRUCIBLE_WORLD_H
#define CRUCIBLE_WORLD_H
#include <flecs.h>
namespace crucible
{
    namespace core
    {

        class World
        {
        public:
            inline static flecs::world ECS;
            static ecs_entity_t RegisterOrRetrieveType(const char* typeName, size_t typeSize, size_t typeAlignment);
            static ecs_entity_t RegisterOrRetrieveScriptingType(const char* typeName);
        };

    } // core
} // crucible

#endif //CRUCIBLE_WORLD_H
