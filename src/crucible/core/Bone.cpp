#include "Bone.h"
#include "Skeleton.h"
#include <boost/endian/conversion.hpp>

namespace crucible
{
    namespace core
    {
        Bone::Bone(int32_t skeletalIndex,const Transform& transform, int32_t parentIndex, const std::vector<uint16_t>& children)
        {
            assert(skeletalIndex >= 0);
            _transform = transform;
            _skeletalIndex = skeletalIndex;
            _parentIndex = parentIndex;
            _children = children;
        }

        Bone::Bone(const Bone& from)
        {
            copy(from);
        }

        Bone& Bone::operator=(const Bone& from)
        {
            copy(from);
            return *this;
        }

        Bone::Bone(Bone&& from)
        {
            move(from);
        }

        Bone& Bone::operator=(Bone&& from)
        {
            move(from);
            return *this;
        }

        bool Bone::operator==(const Bone& other) const
        {
            if (_transform != other._transform)
            {
                return false;
            }
            if (_skeletalIndex != other._skeletalIndex)
            {
                return false;
            }
            if (_parentIndex != other._parentIndex)
            {
                return false;
            }
            if (_children.size() != other._children.size())
            {
                return false;
            }
            for (auto i=0; i<_children.size(); i++)
            {
                if (_children[i] != other._children[i])
                {
                    return false;
                }
            }
            return true;
        }

        uint16_t Bone::skeletalIndex()const
        {
            return _skeletalIndex;
        }

        uint16_t Bone::childrenCount()const
        {
            return _children.size();
        }

        Bone* Bone::child(Skeleton& skeleton, uint16_t index)const
        {
            if (index < 0)
            {
                return nullptr;
            }
            assert(index >= 0 && index < _children.size());
            return skeleton.getBone(_children[index]);
        }

        Bone* Bone::parent(Skeleton& skeleton) const
        {
            if (_parentIndex == UINT16_MAX)
            {
                return nullptr;
            }
            return skeleton.getBone(_parentIndex);
        }

        Transform Bone::localTransform() const
        {
            return _transform;
        }

        Transform Bone::skeletalTransform(Skeleton& skeleton) const
        {
            Transform cumulativeTransform = _transform;
            Bone* boneParent = parent(skeleton);
            if (boneParent)
            {
                cumulativeTransform = cumulativeTransform + boneParent->skeletalTransform(skeleton);
            }
            return cumulativeTransform;
        }

        void Bone::copy(const Bone& from)
        {
            _skeletalIndex=from._skeletalIndex;
            _transform=from._transform;
            _parentIndex=from._parentIndex;
            _children=from._children;
        }

        void Bone::move(Bone& from)
        {
            _skeletalIndex=from._skeletalIndex;
            _transform=from._transform;
            _parentIndex=from._parentIndex;
            _children.swap(from._children);
        }

    } // core
} // crucible
