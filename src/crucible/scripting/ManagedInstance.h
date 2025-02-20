#ifndef CRUCIBLE_MANAGEDINSTANCE_H
#define CRUCIBLE_MANAGEDINSTANCE_H

namespace crucible
{
    namespace scripting
    {

        class ManagedInstance
        {
        public:
            ~ManagedInstance();
            ManagedInstance(const ManagedInstance&)=delete;
            ManagedInstance& operator=(const ManagedInstance&)=delete;
            ManagedInstance(ManagedInstance&& from);
            ManagedInstance& operator=(ManagedInstance&& from);
            friend class ManagedType;
        private:
            ManagedInstance(void* gcHandle);
            void move(ManagedInstance& from);
            void* _gcHandle= nullptr;
        };

    } // scripting
} // crucible

#endif //CRUCIBLE_MANAGEDINSTANCE_H
