#include "Keyframe.h"

namespace crucible
{
    namespace core
    {
        Keyframe::Keyframe(const Transform* boneTransforms, size_t boneCount)
        {
            _boneLocalTransforms.resize(boneCount);
            memcpy(_boneLocalTransforms.data(),boneTransforms,sizeof(Transform)*boneCount);
        }

        Keyframe::Keyframe(std::vector<Transform>&& boneTransforms)
        {
            _boneLocalTransforms.swap(boneTransforms);
        }

        Keyframe::Keyframe(const Keyframe& from)
        {
            copy(from);
        }

        Keyframe& Keyframe::operator=(const Keyframe& from)
        {
            copy(from);
            return *this;
        }

        Keyframe::Keyframe(Keyframe&& from)
        {
            move(from);
        }

        Keyframe& Keyframe::operator=(Keyframe&& from)
        {
            move(from);
            return *this;
        }

        size_t Keyframe::boneCount() const
        {
            return _boneLocalTransforms.size();
        }

        Transform Keyframe::localBoneTransform(size_t boneIndex) const
        {
            return _boneLocalTransforms[boneIndex];
        }

        void Keyframe::copy(const Keyframe& from)
        {
            _boneLocalTransforms = from._boneLocalTransforms;
        }

        void Keyframe::move(Keyframe& from)
        {
            _boneLocalTransforms.swap(from._boneLocalTransforms);
        }
    } // core
} // crucible
