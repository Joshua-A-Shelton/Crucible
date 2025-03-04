#include <stdexcept>
#include "World.h"
#include <crucible/scripting/ManagedInstance.h>
#include <flecs/addons/cpp/component.hpp>

namespace crucible
{
    namespace core
    {
        ecs_entity_t World::RegisterOrRetrieveType(const char* typeName, size_t typeSize, size_t typeAlignment)
        {
            ecs_entity_t myId = 0;
            {
                ecs_component_desc_t desc = {0};
                ecs_entity_desc_t edesc = {0};
                edesc.id = myId;
                edesc.use_low_id = true;
                edesc.name = typeName;
                edesc.symbol = typeName;
                desc.entity = ecs_entity_init(World::ECS.world_, &edesc);
                desc.type.size = (static_cast<ecs_size_t>(typeSize));
                desc.type.alignment = static_cast<int64_t>(typeAlignment);
                myId = ecs_component_init(World::ECS.world_, &desc);
            }
            if(myId == 0)
            {
                throw std::runtime_error(std::string("Unable to create or find type: ")+typeName);
            }
            return myId;
        }

        ecs_entity_t World::RegisterOrRetrieveScriptingType(const char* typeName)
        {
            ecs_entity_t myId = 0;
            {
                ecs_component_desc_t desc = {0};
                ecs_entity_desc_t edesc = {0};
                edesc.id = myId;
                edesc.use_low_id = true;
                edesc.name = typeName;
                edesc.symbol = typeName;
                desc.entity = ecs_entity_init(World::ECS.world_, &edesc);
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


                myId = ecs_component_init(World::ECS.world_, &desc);
            }
            if(myId == 0)
            {
                throw std::runtime_error(std::string("Unable to create or find type: ")+typeName);
            }
            return myId;
        }
    } // core
} // crucible