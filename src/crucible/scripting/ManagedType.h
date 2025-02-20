#ifndef CRUCIBLE_MANAGEDTYPE_H
#define CRUCIBLE_MANAGEDTYPE_H
#include "../CrucibleCore.h"
#include <string>
#include "ManagedInstance.h"
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
            ManagedInstance createNew();
            template<class T> T getFunction(const std::string& functionName)
            {
                return (T)(_getFunction(functionName));
            }

            bool operator ==(const ManagedType& against)const;
            bool operator !=(const ManagedType& against)const;
        };

    } // scripting
} // crucible

#endif //CRUCIBLE_MANAGEDTYPE_H
