#include "ECSInterop.h"

#include "scenes/World.h"

namespace crucible
{
    namespace core
    {
        void ECSInterop::mapInteropTypes()
        {
            _types.MeshRenderer = World::RegisterOrRetrieveScriptingType("Crucible.Core.MeshRenderer");
            _types.Node = World::RegisterOrRetrieveType("Crucible Node",sizeof(NodeECSReference),alignof(NodeECSReference));
            _types.Skeleton = CLEARLYTHISNEEDSTOBESET;
            _types.Transform = World::RegisterOrRetrieveType("Crucible.Core.Transform",sizeof(core::Transform),alignof(core::Transform));
        }

        const InteropTypes& ECSInterop::types() const
        {
            return _types;
        }

    } // core
} // crucible
