#ifndef CRUCIBLE_BONE_H
#define CRUCIBLE_BONE_H
#include <vector>

#include <crucible/core/Transform.h>

namespace crucible
{
    namespace core
    {
        class Skeleton;
        class Bone
        {
        public:
            Bone(int32_t skeletalIndex,const Transform& transform, int32_t parentIndex, const std::vector<uint16_t>& children);
            Bone()=default;
            ~Bone()=default;
            Bone(const Bone& from);
            Bone& operator=(const Bone& from);
            Bone(Bone&& from);
            Bone& operator=(Bone&& from);

            bool operator==(const Bone& other) const;

            uint16_t skeletalIndex()const;
            uint16_t childrenCount()const;
            Bone* child(Skeleton& skeleton, uint16_t index)const;
            Bone* parent(Skeleton& skeleton)const;
            Transform localTransform()const;
            Transform skeletalTransform(Skeleton& skeleton)const;
            friend class Skeleton;

        private:
            void copy(const Bone& from);
            void move(Bone& from);
            uint16_t _skeletalIndex=-1;
            Transform _transform;
            uint16_t _parentIndex=-1;
            std::vector<uint16_t> _children;
        };
    } // core
} // crucible

#endif //CRUCIBLE_BONE_H
