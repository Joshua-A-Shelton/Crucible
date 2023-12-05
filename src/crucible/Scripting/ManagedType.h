#ifndef CRUCIBLE_MANAGEDTYPE_H
#define CRUCIBLE_MANAGEDTYPE_H

#include "../Crucible.h"
#include <string>

namespace crucible
{
    class CRUCIBLE_EXPORT ManagedType
    {
    private:
        void* runtimeTypeHandle = nullptr;
        void* _getFunction(const std::string& functionName);
    public:
        template<class T>
        T getFunction(const std::string& functionName)
        {
            return static_cast<T>(_getFunction(functionName));
        }
    };
} // crucible
#endif //CRUCIBLE_MANAGEDTYPE_H