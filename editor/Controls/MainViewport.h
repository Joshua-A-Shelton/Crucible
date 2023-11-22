#ifndef CRUCIBLE_MAINVIEWPORT_H
#define CRUCIBLE_MAINVIEWPORT_H
#include "Control.h"
namespace crucible
{
    namespace controls
    {
        class MainViewport: public Control
        {
        public:
            ~MainViewport()override=default;
            void show()override;
        };
    }
} // slag

#endif //CRUCIBLE_MAINVIEWPORT_H
