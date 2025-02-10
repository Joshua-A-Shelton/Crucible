#ifndef CRUCIBLE_MANAGEDTYPE_H
#define CRUCIBLE_MANAGEDTYPE_H
#include "../CrucibleCore.h"
#include <string>
namespace crucible
{
    namespace scripting
    {

        class CRUCIBLE_API ManagedType
        {
        private:
            void* _runtimeTypeHandle = nullptr;
            void* _getFunction(const std::string& functionName);
        public:
            template<class T> T getFunction(const std::string& functionName)
            {
                return (T)(_getFunction(functionName));
            }
        };

    } // scripting
} // crucible

#endif //CRUCIBLE_MANAGEDTYPE_H
