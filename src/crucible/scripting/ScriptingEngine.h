//
// Created by josh on 10/14/25.
//

#ifndef CRUCIBLE_SCRIPTINGENGINE_H
#define CRUCIBLE_SCRIPTINGENGINE_H
#include "ManagedFunctionDelegate.h"
#include "CSharpFunctionPointers.h"
#include "ManagedInstance.h"
#include "ManagedType.h"

namespace crucible
{
    class Game;
    namespace scripting
    {
        ///Class that manages interop between C++ and C# code
        class CRUCIBLE_API ScriptingEngine
        {
        private:
            static CSharpFunctionPointers& funcPointers();
        public:
            ///Called once automatically at startup
            static bool initialize();
            ///Called once automatically on program exit
            static void cleanup();
            /**
             * Get a handle to C# type
             * @param typeName Fully qualified name of the type (or just the type name if in default context)
             * @return
             */
            static ManagedType getManagedType(const char* typeName);

            /**
             * Get a callable function pointer to directly call C# functions. Functions must be static, and use only fully blittable types at the interface
             * @tparam ReturnType Return type (in C++ types) of the function
             * @tparam Args Argument types (in C++ types) of the function
             * @param onType C# type to look for the function on
             * @param functionName Name of the function
             * @param bindingFlags Flags about the details of the function
             * @param parameterTypeArray Array of C# types the function parameters are, null if there are none
             * @param parameterTypeCount Number of parameters in parameterTypeArray
             * @return
             */
            template<typename ReturnType, typename... Args> static ManagedFunctionDelegate<ReturnType,Args...>  getManagedFunctionDelegate(ManagedType& onType, const char* functionName, BindingFlags bindingFlags, ManagedType* parameterTypeArray,int32_t parameterTypeCount)
            {
                ManagedFunctionDelegateInternals internals{};
                auto functions = funcPointers();
                functions.getManagedFunctionDelegate(onType,functionName,bindingFlags,parameterTypeArray,parameterTypeCount,internals);
                return ManagedFunctionDelegate<ReturnType,Args...>(std::move(internals));
            }

            /**
             * Create an instance of a C# object or struct
             * @param type C# type to create instance of
             * @param parameterCount Number of parameters of object constructor
             * @param parameterTypes C# types of object constructor
             * @param parameters Array of pointers that each point to either a blittable type or ManagedInstance
             * @return
             */
            static ManagedInstance createManagedInstance(ManagedType& type, int32_t parameterCount,ManagedType* parameterTypes, void** parameters);
            friend class ManagedFunctionDelegateInternals;
            friend class ManagedInstance;
            friend class crucible::Game;
        private:
            static void freeInstance(void* instance);
            static void gameManagerInitialize();
            static void gameManagerCleanUp();
            inline static CSharpFunctionPointers _functionPointers;
        };






    } // scripting
} // crucible

#endif //CRUCIBLE_SCRIPTINGENGINE_H