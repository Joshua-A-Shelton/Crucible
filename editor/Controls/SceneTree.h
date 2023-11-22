#ifndef CRUCIBLE_SCENETREE_H
#define CRUCIBLE_SCENETREE_H
#include "Control.h"
namespace crucible
{
    namespace controls
    {
        class SceneTree: Control
        {
        public:
            ~SceneTree()override=default;
            void show()override;
        };
    }

} // crucible

#endif //CRUCIBLE_SCENETREE_H
