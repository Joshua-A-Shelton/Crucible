#ifndef CRUCIBLE_CRUCIBLE_H
#define CRUCIBLE_CRUCIBLE_H
#include "core/scenes/Node.h"
#include "core/Mesh.h"
#include "core/Transform.h"
#include "core/VertexAttributeStream.h"
#include "CrucibleCore.h"
#include "Game.h"
namespace crucible
{

    class CRUCIBLE_API Crucible
    {
    public:
        enum RenderingBackend
        {
            DEFAULT,
            VULKAN,
            DX12
        };
        static bool initialize(RenderingBackend backend, bool debugEnabled);
        static void cleanup();
    };

} // crucible

#endif //CRUCIBLE_CRUCIBLE_H
