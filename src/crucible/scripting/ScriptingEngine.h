//
// Created by josh on 10/14/25.
//

#ifndef CRUCIBLE_SCRIPTINGENGINE_H
#define CRUCIBLE_SCRIPTINGENGINE_H
#include "ManagedFunction.h"
#include "ManagedFunctionPointers.h"
#include "ManagedType.h"

namespace crucible
{
    class Game;
    namespace scripting
    {
        class ScriptingEngine
        {
        public:
            static bool initialize();
            static void cleanup();
            static ManagedType getManagedType(const char* typeName);
            template<typename ReturnType, typename... Args> static ManagedFunction<ReturnType,Args...>  getManagedFunction(ManagedType& onType, const char* functionName, BindingFlags bindingFlags, ManagedType* parameterTypeArray,int32_t parameterTypeCount)
            {
                ManagedFunctionInternals internals{};
                _functionPointers.getManagedFunction(onType,functionName,bindingFlags,parameterTypeArray,parameterTypeCount,internals);
                return ManagedFunction<ReturnType,Args...>(std::move(internals));
            }
            friend class ManagedFunctionInternals;
            friend class ManagedInstance;
            friend class crucible::Game;
        private:
            static void freeInstance(void* instance);
            static void gameManagerInitialize();
            static void gameManagerCleanUp();
            static inline ManagedFunctionPointers _functionPointers;
        };






    } // scripting
} // crucible

#endif //CRUCIBLE_SCRIPTINGENGINE_H