#include "World.h"

#include <stdexcept>

#include "crucible/Node.h"
#include "crucible/Transform.h"
#include "crucible/scripting/ManagedInstance.h"

namespace crucible
{
    namespace ecs
    {
        inline flecs::world CRUCIBLE_WORLD{};
        inline EngineDefinedTypes CRUCIBLE_ENGINE_TYPES{};

        void registerEngineDefinedTypes()
        {
            CRUCIBLE_ENGINE_TYPES.Node = registerOrRetrieveDataType("Crucible.Core.Node",sizeof(NodeECSReference),alignof(NodeECSReference));
            CRUCIBLE_ENGINE_TYPES.Transform = registerOrRetrieveDataType("Crucible.Core.Transform",sizeof(Transform),alignof(Transform));
        }

        const EngineDefinedTypes& engineTypes()
        {
            return CRUCIBLE_ENGINE_TYPES;
        }

        ecs_entity_t registerOrRetrieveDataType(const char* typeName, size_t typeSize, size_t typeAlignment)
        {
            ecs_entity_t myId = 0;
            {
                ecs_component_desc_t desc = {0};
                ecs_entity_desc_t edesc = {0};
                edesc.id = myId;
                edesc.use_low_id = true;
                edesc.name = typeName;
                edesc.symbol = typeName;
                desc.entity = ecs_entity_init(CRUCIBLE_WORLD.world_, &edesc);
                desc.type.size = (static_cast<ecs_size_t>(typeSize));
                desc.type.alignment = static_cast<int64_t>(typeAlignment);
                myId = ecs_component_init(CRUCIBLE_WORLD.world_, &desc);
            }
            if(myId == 0)
            {
                throw std::runtime_error(std::string("Unable to create or find type: ")+typeName);
            }
            return myId;
        }

        ecs_entity_t registerOrRetrieveReferenceType(const char* typeName)
        {
            ecs_entity_t myId = 0;
            {
                ecs_component_desc_t desc = {0};
                ecs_entity_desc_t edesc = {0};
                edesc.id = myId;
                edesc.use_low_id = true;
                edesc.name = typeName;
                edesc.symbol = typeName;
                desc.entity = ecs_entity_init(CRUCIBLE_WORLD.world_, &edesc);
                desc.type.size = (static_cast<ecs_size_t>(sizeof(scripting::ManagedInstance)));
                desc.type.alignment = static_cast<int64_t>(alignof(scripting::ManagedInstance));


                ecs_type_hooks_t cl{};
                cl.ctor = flecs::_::ctor<scripting::ManagedInstance>(cl.flags);
                cl.dtor = flecs::_::dtor<scripting::ManagedInstance>(cl.flags);

                cl.copy = flecs::_::copy<scripting::ManagedInstance>(cl.flags);
                cl.copy_ctor = flecs::_::copy_ctor<scripting::ManagedInstance>(cl.flags);
                cl.move = flecs::_::move<scripting::ManagedInstance>(cl.flags);
                cl.move_ctor = flecs::_::move_ctor<scripting::ManagedInstance>(cl.flags);

                cl.ctor_move_dtor = flecs::_::ctor_move_dtor<scripting::ManagedInstance>(cl.flags);
                cl.move_dtor = flecs::_::move_dtor<scripting::ManagedInstance>(cl.flags);

                desc.type.hooks = cl;


                myId = ecs_component_init(CRUCIBLE_WORLD.world_, &desc);
            }
            if(myId == 0)
            {
                throw std::runtime_error(std::string("Unable to create or find type: ")+typeName);
            }
            return myId;
        }

        flecs::world& world()
        {
            return CRUCIBLE_WORLD;
        }

    } // ecs
} // crucible
