#ifndef CRUCIBLE_MENUBAR_H
#define CRUCIBLE_MENUBAR_H
#include "Control.h"
namespace crucible
{
    namespace controls
    {
        class MenuBar: public Control
        {
        public:
            ~MenuBar()override=default;
            void show()override;
        };
    }
} // crucible

#endif //CRUCIBLE_MENUBAR_H
