#ifndef CRUCIBLE_MANAGEDTYPE_H
#define CRUCIBLE_MANAGEDTYPE_H
#include "../CrucibleCore.h"
#include <string>
namespace crucible
{
    namespace scripting
    {
        class ManagedInstance;
        class CRUCIBLE_API ManagedType
        {
        private:
            void* _runtimeTypeHandle = nullptr;
            void* _getFunction(const std::string& functionName);
        public:
            [[nodiscard]] ManagedInstance createNew()const;
            template<class T> T getFunction(const std::string& functionName)
            {
                return (T)(_getFunction(functionName));
            }

            bool operator ==(const ManagedType& against)const;
            bool operator !=(const ManagedType& against)const;

            void* runtimeTypeHandle()const;
        };

    } // scripting
} // crucible

#endif //CRUCIBLE_MANAGEDTYPE_H
