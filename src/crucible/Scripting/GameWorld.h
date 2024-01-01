#ifndef CRUCIBLEEDITOR_GAMEWORLD_H
#define CRUCIBLEEDITOR_GAMEWORLD_H

#include "crucible/Graphics/CameraViewData.h"
#include "glm/glm.hpp"

namespace crucible
{

    class GameWorld
    {
    public:
        static void getManagedFunctions();
        static glm::mat4 getMainCamera();
    };

} // crucible

#endif //CRUCIBLEEDITOR_GAMEWORLD_H
