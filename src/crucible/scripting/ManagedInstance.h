#ifndef CRUCIBLE_MANAGEDINSTANCE_H
#define CRUCIBLE_MANAGEDINSTANCE_H
#include <cstdint>
#include <crucible/CrucibleCore.h>
namespace crucible
{
    namespace scripting
    {
        class ManagedType;

        class CRUCIBLE_API ManagedInstance
        {
        public:
            ManagedInstance();
            ManagedInstance(void* gcHandle);
            ~ManagedInstance();
            ManagedInstance(const ManagedInstance&) = delete;
            ManagedInstance& operator=(const ManagedInstance& from)=delete;
            ManagedInstance(ManagedInstance&& from);
            ManagedInstance& operator=(ManagedInstance&& from);
            void invokeMethod(const char* method, int32_t parameterCount, ManagedType* parameterTypes, void** parameterValues);
            ManagedInstance invokeMethodReturn(const char* method, int32_t parameterCount, ManagedType* parameterTypes, void** parameterValues);
            void invokeMethodReturn(const char* method, int32_t parameterCount, ManagedType* parameterTypes, void** parameterValues, void* outValue);
            bool isNull() const;
            void* gcHandle();
        private:
            void move(ManagedInstance& from);
            void* _gcHandle = nullptr;
        };
    } // scripting
} // crucible

#endif //CRUCIBLE_MANAGEDINSTANCE_H
