#ifndef CRUCIBLE_RESOURCES_H
#define CRUCIBLE_RESOURCES_H

#include "Control.h"

namespace crucible
{
    namespace controls
    {

        class Resources: public Control
        {
        public:
            ~Resources() override=default;
            void show()override;
        };

    } // crucible
} // controls

#endif //CRUCIBLE_RESOURCES_H
