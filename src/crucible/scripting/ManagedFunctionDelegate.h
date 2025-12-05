#ifndef CRUCIBLE_MANAGEDFUNCTION_H
#define CRUCIBLE_MANAGEDFUNCTION_H
#include <algorithm>

#include "crucible/CrucibleCore.h"

namespace crucible
{
    namespace scripting
    {
        class CRUCIBLE_API ManagedFunctionDelegateInternals
        {
        public:
            ~ManagedFunctionDelegateInternals();
            ManagedFunctionDelegateInternals(const ManagedFunctionDelegateInternals& other) = delete;
            ManagedFunctionDelegateInternals& operator=(const ManagedFunctionDelegateInternals& other) = delete;
            ManagedFunctionDelegateInternals(ManagedFunctionDelegateInternals&& from);
            ManagedFunctionDelegateInternals& operator=(ManagedFunctionDelegateInternals&& from);
            void* _functionPointer = nullptr;
            void* _functionGCHandle = nullptr;
            friend class ScriptingEngine;
        private:
            ManagedFunctionDelegateInternals()=default;
            void move(ManagedFunctionDelegateInternals& from);
        };

        template<typename ReturnType, typename... Args> class CRUCIBLE_API ManagedFunctionDelegate
        {
        private:
            ManagedFunctionDelegateInternals _internals;
            ManagedFunctionDelegate(ManagedFunctionDelegateInternals&& internals): _internals(std::move(internals))
            {
            }
        public:

            ~ManagedFunctionDelegate()=default;
            ManagedFunctionDelegate(const ManagedFunctionDelegate& other) = delete;
            ManagedFunctionDelegate& operator=(const ManagedFunctionDelegate& other) = delete;
            ManagedFunctionDelegate(ManagedFunctionDelegate&& from): _internals(std::move(from._internals))
            {
            }
            ManagedFunctionDelegate& operator=(ManagedFunctionDelegate&& from)
            {
                _internals = std::move(from._internals);
                return *this;
            }

            ReturnType operator()(Args... args) const
            {
                return ((ReturnType (*)(Args...))_internals._functionPointer)(args...);
            }
            void* rawFunctionPointer() const
            {
                return _internals._functionPointer;
            }
            friend class ScriptingEngine;
        };
    } // scripting
} // crucible

#endif //CRUCIBLE_MANAGEDFUNCTION_H
