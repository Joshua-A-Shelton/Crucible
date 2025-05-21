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
            _types.Skeleton = World::RegisterOrRetrieveScriptingType("Crucible.Core.Animation.Skeleton");
            _types.Transform = World::RegisterOrRetrieveType("Crucible.Core.Transform",sizeof(core::Transform),alignof(core::Transform));
        }

        const InteropTypes& ECSInterop::types()
        {
            return _types;
        }

    } // core
} // crucible
