#ifndef CRUCIBLE_KEYFRAME_H
#define CRUCIBLE_KEYFRAME_H
#include <vector>

#include "crucible/core/Transform.h"

namespace crucible
{
    namespace core
    {
        class Keyframe
        {
        public:
            Keyframe(const Transform* boneTransforms, size_t boneCount);
            Keyframe(std::vector<Transform>&& boneTransforms);
            Keyframe(const Keyframe& from);
            Keyframe& operator=(const Keyframe& from);
            Keyframe(Keyframe&& from);
            Keyframe& operator=(Keyframe&& from);
            size_t boneCount()const;
            Transform localBoneTransform(size_t boneIndex)const;
        private:
            void copy(const Keyframe& from);
            void move(Keyframe& from);
            std::vector<Transform> _boneLocalTransforms;
        };
    } // core
} // crucible

#endif //CRUCIBLE_KEYFRAME_H
