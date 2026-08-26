#ifndef CRUCIBLE_MANAGEDINSTANCE_H
#define CRUCIBLE_MANAGEDINSTANCE_H
#include "ManagedType.h"
typedef void* (*ManagedCreateInstance)(crucible::scripting::ManagedType forType);
typedef void* (*ManagedCreateInstanceWithParams)(crucible::scripting::ManagedType forType, int32_t parameterCount, crucible::scripting::ManagedType* parameterTypes, void** parameterValues);
typedef void (*ManagedFreeInstance)(void* gcHandle);

namespace crucible
{
    namespace scripting
    {
        class ManagedInstance
        {
        public:
            ManagedInstance(ManagedType type);
            ManagedInstance(ManagedType type, int32_t parameterCount, ManagedType* parameterTypes, void** parameterValues);
            ~ManagedInstance();
            ManagedInstance(const ManagedInstance&)=delete;
            ManagedInstance& operator=(const ManagedInstance&)=delete;
            ManagedInstance(ManagedInstance&& from) noexcept;
            ManagedInstance& operator=(ManagedInstance&& from) noexcept;
            friend class ScriptingModule;
        private:
            inline static ManagedCreateInstance _managedCreateInstance = nullptr;
            inline static ManagedCreateInstanceWithParams _managedCreateInstanceWithParams = nullptr;
            inline static ManagedFreeInstance _managedFreeInstance = nullptr;
            void move(ManagedInstance& from);
            void* _gcHandle = nullptr;
        };
    } // scripting
} // crucible

#endif //CRUCIBLE_MANAGEDINSTANCE_H