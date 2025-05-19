#ifndef CRUCIBLE_SKELETON_H
#define CRUCIBLE_SKELETON_H
#include <vector>

#include "Bone.h"
#include "Keyframe.h"

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
            std::vector<glm::mat4>& shaderTransforms();
            Bone* rootBone()const;
            void snapToKeyframe(const Keyframe& keyframe);
            Keyframe currentPoseToKeyframe() const;
            void interpolate(const Keyframe& keyframe1, const Keyframe& keyframe2, float amount);
        private:
            void copy(const Skeleton& skeleton);
            void move(Skeleton& skeleton);
            bool isValid();
            void updateCurrentTransforms();
            void updateBoneTransform(Bone* bone, const Transform& parentTransform);
            std::vector<Bone> _bones;
            Bone* _rootBone = nullptr;
            std::vector<glm::mat4> _currentTransforms;
        };
    } // core
} // crucible

#endif //CRUCIBLE_SKELETON_H
