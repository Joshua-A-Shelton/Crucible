#ifndef CRUCIBLE_WORLD_H
#define CRUCIBLE_WORLD_H
#include <flecs.h>

#include "Node.h"
#include "crucible/core/rendering/MeshPass.h"
#include "slag/CommandBuffer.h"

namespace crucible
{
    namespace core
    {
        class World
        {
        public:
            inline static flecs::world ECS{};
            inline static Node* RootNode = nullptr;
            static ecs_entity_t RegisterOrRetrieveType(const char* typeName, size_t typeSize, size_t typeAlignment);
            static ecs_entity_t RegisterOrRetrieveScriptingType(const char* typeName);
            inline static MeshPass* MeshDrawPass=nullptr;
        };

    } // core
} // crucible

#endif //CRUCIBLE_WORLD_H
