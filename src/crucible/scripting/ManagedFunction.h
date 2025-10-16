#ifndef CRUCIBLE_MANAGEDFUNCTION_H
#define CRUCIBLE_MANAGEDFUNCTION_H
#include <algorithm>

#include "crucible/CrucibleCore.h"

namespace crucible
{
    namespace scripting
    {
        CRUCIBLE_API class ManagedFunctionInternals
        {
        public:
            ~ManagedFunctionInternals();
            ManagedFunctionInternals(const ManagedFunctionInternals& other) = delete;
            ManagedFunctionInternals& operator=(const ManagedFunctionInternals& other) = delete;
            ManagedFunctionInternals(ManagedFunctionInternals&& from);
            ManagedFunctionInternals& operator=(ManagedFunctionInternals&& from);
            void* _functionPointer = nullptr;
            void* _functionGCHandle = nullptr;
            friend class ScriptingEngine;
        private:
            ManagedFunctionInternals()=default;
            void move(ManagedFunctionInternals& from);
        };

        template<typename ReturnType, typename... Args> CRUCIBLE_API class ManagedFunction
        {
        private:
            ManagedFunctionInternals _internals;
            ManagedFunction(ManagedFunctionInternals&& internals): _internals(std::move(internals))
            {
            }
        public:

            ~ManagedFunction()=default;
            ManagedFunction(const ManagedFunction& other) = delete;
            ManagedFunction& operator=(const ManagedFunction& other) = delete;
            ManagedFunction(ManagedFunction&& from): _internals(std::move(from._internals))
            {
            }
            ManagedFunction& operator=(ManagedFunction&& from)
            {
                _internals = std::move(from._internals);
                return *this;
            }

            ReturnType operator()(Args... args) const
            {
                return ((ReturnType (*)(Args...))_internals._functionPointer)(args...);
            }
            friend class ScriptingEngine;
        };
    } // scripting
} // crucible

#endif //CRUCIBLE_MANAGEDFUNCTION_H
