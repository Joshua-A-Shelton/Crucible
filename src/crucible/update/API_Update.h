#ifndef CRUCIBLE_API_UPDATE_H
#define CRUCIBLE_API_UPDATE_H

#ifdef CRUCIBLE_UPDATE_MODULE
#include <crucible/core/CrucibleAPI.h>

namespace crucible
{
    namespace update
    {
       CRUCIBLE_API void run();
    } // update
} // crucible

extern "C"
{
    CRUCIBLE_API void CRUCIBLE_API_finish();
}
#endif

#endif //CRUCIBLE_API_UPDATE_H