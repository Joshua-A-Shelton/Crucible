#ifndef CRUCIBLE_ENTITYCOMPONENTSYSTEM_H
#define CRUCIBLE_ENTITYCOMPONENTSYSTEM_H

#include <entt/entt.hpp>
namespace crucible
{

    class EntityComponentSystem
    {
    public:
        static inline entt::registry registry;
    };

} // crucible

#endif //CRUCIBLE_ENTITYCOMPONENTSYSTEM_H
