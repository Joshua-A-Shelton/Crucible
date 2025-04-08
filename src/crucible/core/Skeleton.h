#ifndef CRUCIBLE_SKELETON_H
#define CRUCIBLE_SKELETON_H
#include <vector>

#include "Bone.h"

namespace crucible
{
    namespace core
    {
        class Skeleton
        {
        public:
            Skeleton(unsigned char* lz4SkeletalData, uint64_t skeletalLength);
            Skeleton(const std::vector<Bone>& bones);
            Skeleton(std::vector<Bone>&& bones);
            Skeleton(const Skeleton& skeleton);
            Skeleton& operator=(const Skeleton& skeleton);
            Skeleton& operator=(Skeleton&& skeleton);
            Skeleton(Skeleton&& skeleton);
            ~Skeleton()=default;
            Bone* getBone(uint16_t boneIndex);
            uint16_t boneCount() const;
            std::vector<unsigned char> toLZ4();
        private:
            bool isValid();
            std::vector<Bone> _bones;
        };
    } // core
} // crucible

#endif //CRUCIBLE_SKELETON_H
