#ifndef CRUCIBLE_INSPECTOR_H
#define CRUCIBLE_INSPECTOR_H
#include "Control.h"
namespace crucible
{

    namespace controls
    {
        class Inspector: public Control
        {
        public:
            ~Inspector()override=default;
            void show()override;
        };
    }
} // crucible

#endif //CRUCIBLE_INSPECTOR_H
