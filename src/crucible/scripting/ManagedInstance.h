#ifndef CRUCIBLE_MANAGEDINSTANCE_H
#define CRUCIBLE_MANAGEDINSTANCE_H
#include <crucible/CrucibleCore.h>
namespace crucible
{
    namespace scripting
    {
        class CRUCIBLE_API ManagedInstance
        {
        public:
            ~ManagedInstance();
            ManagedInstance(const ManagedInstance&) = delete;
            ManagedInstance& operator=(const ManagedInstance& from)=delete;
            ManagedInstance(ManagedInstance&& from);
            ManagedInstance& operator=(ManagedInstance&& from);
            friend class ScriptingEngine;
        private:
            ManagedInstance(void* gcHandle);
            void move(ManagedInstance& from);
            void* _gcHandle = nullptr;
        };
    } // scripting
} // crucible

#endif //CRUCIBLE_MANAGEDINSTANCE_H
