#ifndef CRUCIBLE_CONTROL_H
#define CRUCIBLE_CONTROL_H

namespace crucible
{
    namespace controls
    {
        class Control
        {
        public:
            virtual ~Control()=default;
            virtual void show()=0;
        };
    }
} // crucible

#endif //CRUCIBLE_CONTROL_H
