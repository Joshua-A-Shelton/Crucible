#ifndef CRUCIBLE_MANAGEDINSTANCE_H
#define CRUCIBLE_MANAGEDINSTANCE_H
#include <string>

#include "ManagedType.h"

namespace crucible
{
    namespace scripting
    {

        class ManagedInstance
        {
        public:
            ManagedInstance();
            ManagedInstance(const ManagedType& type, void* gcHandle);
            ~ManagedInstance();
            ManagedInstance(const ManagedInstance&)=delete;
            ManagedInstance& operator=(const ManagedInstance&)=delete;
            ManagedInstance(ManagedInstance&& from);
            ManagedInstance& operator=(ManagedInstance&& from);

            void invokeMethod(const std::string& method, int32_t parameterCount, ManagedType* parameterTypes, void** parameterValues);
            ManagedInstance invokeMethodReturn(const std::string& method, int32_t parameterCount, ManagedType* parameterTypes, void** parameterValues);
            void invokeMethodReturn(const std::string& method, int32_t parameterCount, ManagedType* parameterTypes, void** parameterValues, void* outValue);


            bool isNull() const;
            [[nodiscard]] ManagedType getType() const;
            void* gcHandle() const;

            friend class ScriptingEngine;
        private:
            void move(ManagedInstance& from);
            void* _gcHandle= nullptr;
            ManagedType _type;
        };

    } // scripting
} // crucible

#endif //CRUCIBLE_MANAGEDINSTANCE_H
