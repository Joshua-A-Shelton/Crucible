#ifndef CRUCIBLE_SCENE_H
#define CRUCIBLE_SCENE_H
#include "Node.h"
namespace crucible
{
    namespace core
    {

        class Scene
        {
        private:
            std::vector<Node*> _nodes;
        };

    } // core
} // crucible

#endif //CRUCIBLE_SCENE_H
