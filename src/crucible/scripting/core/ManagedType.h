#ifndef CRUCIBLE_MANAGEDTYPE_H
#define CRUCIBLE_MANAGEDTYPE_H
#include <string>

typedef void* (*ManagedGetType)(const char*);
namespace crucible
{
    namespace scripting
    {
        class ManagedType
        {
        public:
            static ManagedType get(const std::string& typeIdentifier);
            friend class ScriptingModule;
            void* typeHandle() const;
            bool operator ==(const ManagedType& against)const;
            bool operator !=(const ManagedType& against)const;
        private:
            void* _runtimeTypeHandle = nullptr;
            inline static ManagedGetType _managedGetType = nullptr;

        };
    } // scripting
} // crucible

#endif //CRUCIBLE_MANAGEDTYPE_H