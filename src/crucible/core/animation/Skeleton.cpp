#include "Skeleton.h"

#include <bit>
#include <execution>
#include <lz4.h>
#include <queue>
#include <ranges>
#include <stdexcept>
#include <unordered_set>
#include <boost/endian/conversion.hpp>

#include <crucible/core/serialization/Serializer.h>

namespace crucible
{
    namespace core
    {
        Skeleton::Skeleton(unsigned char* lz4SkeletalData, uint64_t skeletalLength)
        {
            unsigned char* dataPtr = lz4SkeletalData;
            int32_t compressedSize = *std::bit_cast<int32_t*>(dataPtr);
            dataPtr+=sizeof(int32_t);
            int32_t decompressedSize = *std::bit_cast<int32_t*>(dataPtr);
            dataPtr+=sizeof(int32_t);
            uint16_t boneCount = *std::bit_cast<uint16_t*>(dataPtr);
            dataPtr+=sizeof(uint16_t);
            if constexpr (std::endian::native == std::endian::big)
            {
                boost::endian::big_to_native_inplace(compressedSize);
                boost::endian::big_to_native_inplace(decompressedSize);
                boost::endian::big_to_native_inplace(boneCount);
            }
            _bones.resize(boneCount);
            std::vector<char> decompressedData(decompressedSize);

            auto result = LZ4_decompress_safe((const char*)dataPtr,decompressedData.data(),compressedSize,decompressedSize);
            if (result<0)
            {
                throw std::runtime_error("Skeleton Decompression failure");
            }
            char* decompressedDataPtr = decompressedData.data();
            for (auto& bone : _bones)
            {
                uint16_t index = *((uint16_t*)decompressedDataPtr);
                decompressedDataPtr+=sizeof(uint16_t);
                Transform boneTransform((unsigned char*)decompressedDataPtr);
                decompressedDataPtr+=sizeof(Transform);
                uint16_t parentIndex = *((uint16_t*)decompressedDataPtr);
                decompressedDataPtr+=sizeof(uint16_t);
                uint16_t childrenCount = *((uint16_t*)decompressedDataPtr);
                decompressedDataPtr+=sizeof(uint16_t);
                if constexpr (std::endian::native == std::endian::big)
                {
                    boost::endian::big_to_native_inplace(index);
                    boost::endian::big_to_native_inplace(parentIndex);
                    boost::endian::big_to_native_inplace(childrenCount);
                }
                std::vector<uint16_t> children(childrenCount);
                for (auto i = 0; i < childrenCount; ++i)
                {
                    auto child = *((uint16_t*)decompressedDataPtr);
                    if constexpr (std::endian::native == std::endian::big)
                    {
                        boost::endian::big_to_native_inplace(child);
                    }
                    children[i] = child;
                    decompressedDataPtr+=sizeof(uint16_t);
                }
                bone = Bone(index,boneTransform,parentIndex,children);
                if (bone._parentIndex == UINT16_MAX)
                {
                    if (_rootBone==nullptr)
                    {
                        _rootBone = &bone;
                    }
                    else
                    {
                        throw std::runtime_error("Skeleton has more than one root bone defined");
                    }
                }
            }
            assert(dataPtr + compressedSize == lz4SkeletalData+skeletalLength && "Skeleton Data not expected length");
            assert(isValid() && "Skeleton Data is not arranged in tree structure or has too many bones");
        }

        Skeleton::Skeleton(const std::vector<Bone>& bones)
        {
            _bones = bones;
            _currentTransforms.resize(bones.size());
            for (auto i=0; i < _bones.size(); ++i)
            {
                if (_bones[i].parent(*this)==nullptr)
                {
                    _rootBone = &_bones[i];
                    break;
                }
            }
            assert(isValid() && "Skeleton Data is not arranged in tree structure or has too many bones");
            updateCurrentTransforms();
        }

        Skeleton::Skeleton(std::vector<Bone>&& bones)
        {
            _bones = std::move(bones);
            _currentTransforms.resize(_bones.size());
            for (auto i=0; i < _bones.size(); ++i)
            {
                if (_bones[i].parent(*this)==nullptr)
                {
                    _rootBone = &_bones[i];
                    break;
                }
            }
            assert(isValid() && "Skeleton Data is not arranged in tree structure or has too many bones");
            updateCurrentTransforms();
        }

        Skeleton::Skeleton(const Skeleton& skeleton)
        {
           copy(skeleton);
        }

        Skeleton& Skeleton::operator=(const Skeleton& skeleton)
        {
            copy(skeleton);
            return *this;
        }

        Skeleton& Skeleton::operator=(Skeleton&& skeleton)
        {
            move(skeleton);
            return *this;
        }

        Skeleton::Skeleton(Skeleton&& skeleton)
        {
            move(skeleton);
        }

        Bone* Skeleton::getBone(uint16_t boneIndex)
        {
            assert(boneIndex < _bones.size());
            return &_bones[boneIndex];
        }

        uint16_t Skeleton::boneCount() const
        {
            return _bones.size();
        }

        std::vector<unsigned char> Skeleton::toLZ4()
        {
            std::vector<unsigned char> boneBinaryUncompressed;
            for (auto& bone : _bones)
            {
                auto index = bone.skeletalIndex();
                uint16_t parentIndex = UINT16_MAX;
                auto parent = bone.parent(*this);
                if (parent)
                {
                    parentIndex = parent->skeletalIndex();
                }
                if constexpr (std::endian::native == std::endian::big)
                {
                    boost::endian::big_to_native_inplace(index);
                    boost::endian::big_to_native_inplace(parentIndex);
                }
                bufferInsert(boneBinaryUncompressed,index);
                auto childCount = bone.childrenCount();
                auto childCountEndianCorrected = childCount;
                if constexpr (std::endian::native == std::endian::big)
                {
                    boost::endian::big_to_native_inplace(childCountEndianCorrected);
                }
                auto transform = bone.localTransform().littleEndian();
                bufferInsert(boneBinaryUncompressed,transform);
                bufferInsert(boneBinaryUncompressed,parentIndex);
                bufferInsert(boneBinaryUncompressed,childCountEndianCorrected);
                for (auto i = 0; i < childCount; ++i)
                {
                    auto childBone = bone.child(*this,i);
                    auto childIndex = childBone->skeletalIndex();
                    if constexpr (std::endian::native == std::endian::big)
                    {
                        boost::endian::big_to_native_inplace(childIndex);
                    }
                    bufferInsert(boneBinaryUncompressed,childIndex);
                }
            }

            int32_t compressedSize = LZ4_compressBound(static_cast<int32_t>(boneBinaryUncompressed.size()));
            int32_t decompressedSize = static_cast<int32_t>(boneBinaryUncompressed.size());
            std::vector<char> compressedData(compressedSize);
            compressedSize =LZ4_compress_fast(reinterpret_cast<const char*>(boneBinaryUncompressed.data()),compressedData.data(),static_cast<int32_t>(boneBinaryUncompressed.size()),compressedSize,1);

            std::vector<unsigned char> data(compressedSize+(sizeof(int32_t)*2)+sizeof(uint16_t));
            auto writePtr = data.data();

            uint16_t count = _bones.size();
            if constexpr (std::endian::native == std::endian::big)
            {
                boost::endian::big_to_native_inplace(compressedSize);
                boost::endian::big_to_native_inplace(decompressedSize);
                boost::endian::big_to_native_inplace(count);
            }

            memcpy(writePtr,&compressedSize,sizeof(int32_t));
            writePtr += sizeof(int32_t);
            memcpy(writePtr,&decompressedSize,sizeof(int32_t));
            writePtr += sizeof(int32_t);
            memcpy(writePtr,&count,sizeof(uint16_t));
            writePtr += sizeof(uint16_t);
            memcpy(writePtr,compressedData.data(),compressedSize);
            return data;
        }

        std::vector<glm::mat4>& Skeleton::shaderTransforms()
        {
            updateCurrentTransforms();
            return _currentTransforms;
        }

        Bone* Skeleton::rootBone() const
        {
            return _rootBone;
        }

        void Skeleton::snapToKeyframe(const Keyframe& keyframe)
        {
            assert(keyframe.boneCount() == _bones.size() && "Keyframe must have same number of transforms as bones in skeleton");
            std::for_each(std::execution::par,_bones.begin(),_bones.end(),[&](auto& bone)
            {
                auto i = bone.skeletalIndex();
                auto transform = keyframe.localBoneTransform(i);
                bone._transform = transform;
            });

        }

        Keyframe Skeleton::currentPoseToKeyframe() const
        {
            std::vector<Transform> boneTransforms(_bones.size());
            std::for_each(std::execution::par,_bones.begin(),_bones.end(),[&](auto& bone)
            {
                auto i = bone.skeletalIndex();
                boneTransforms[i] = bone.localTransform();
            });

            return  Keyframe(std::move(boneTransforms));
        }

        void Skeleton::interpolate(const Keyframe& keyframe1, const Keyframe& keyframe2, float amount)
        {
            assert(keyframe1.boneCount() == _bones.size() && keyframe2.boneCount() == _bones.size() && "Keyframes must have same number of transforms as bones in skeleton");
            assert(amount >= 0 && amount <=1 && "amount must be between 0 and 1");
            std::for_each(std::execution::par,_bones.begin(),_bones.end(),[&](auto& bone)
            {
                auto i = bone.skeletalIndex();
                auto t1 = keyframe1.localBoneTransform(i);
                auto t2 = keyframe2.localBoneTransform(i);
                auto position = glm::mix(t1.position(),t2.position(),amount);
                auto scale = glm::mix(t1.scale(),t2.scale(),amount);
                auto rotation = glm::slerp(t1.rotation(),t2.rotation(),amount);
                bone._transform.setPosition(position);
                bone._transform.setScale(scale);
                bone._transform.setRotation(rotation);
            });
        }

        void Skeleton::copy(const Skeleton& skeleton)
        {
            _bones = skeleton._bones;
            _rootBone = &_bones[skeleton._rootBone->skeletalIndex()];
            _currentTransforms = skeleton._currentTransforms;
        }

        void Skeleton::move(Skeleton& skeleton)
        {
            _bones.swap(skeleton._bones);
            _rootBone = skeleton._rootBone;
            _currentTransforms.swap(skeleton._currentTransforms);
        }

        bool Skeleton::isValid()
        {
            if (_bones.size() >= UINT16_MAX)
            {
                return false;
            }
            std::unordered_set<int32_t> boneIndexSet;
            uint16_t rootBoneIndex = UINT16_MAX;
            for (auto& bone: _bones)
            {
                if (boneIndexSet.find(bone.skeletalIndex())!=boneIndexSet.end())
                {
                    return false;
                }
                std::unordered_set<int32_t> parentSet;
                auto* parent = bone.parent(*this);
                if (parent)
                {
                    bool foundChild = false;
                    for (auto i = 0; i < parent->childrenCount(); ++i)
                    {
                        if (parent->child(*this,i)->skeletalIndex() == bone.skeletalIndex())
                        {
                            foundChild = true;
                            break;
                        }
                    }
                    if (!foundChild)
                    {
                        return false;
                    }
                }
                //we found a root bone
                else
                {
                    //check to see if it's the first time we've found it
                    if (rootBoneIndex == UINT16_MAX)
                    {
                        rootBoneIndex = bone.skeletalIndex();
                    }
                    //see if it's not the same root bone, we can only have one root
                    else if (rootBoneIndex!=bone.skeletalIndex())
                    {
                        return false;
                    }
                }
                if (bone.childrenCount() > 0)
                {
                    for (auto i = 0; i < bone.childrenCount(); ++i)
                    {
                        auto childBone = bone.child(*this,i);
                        auto childsParent = childBone->parent(*this);
                        if (childsParent->skeletalIndex() != bone.skeletalIndex())
                        {
                            return false;
                        }
                    }
                }
                while (parent)
                {
                    if (parentSet.find(parent->skeletalIndex())!=parentSet.end())
                    {
                        return false;
                    }
                    parent = parent->parent(*this);
                }
            }
            if (_rootBone == nullptr)
            {
                return false;
            }
            return true;
        }

        void Skeleton::updateCurrentTransforms()
        {
            updateBoneTransform(_rootBone, Transform());
        }

        void Skeleton::updateBoneTransform(Bone* bone, const Transform& parentTransform)
        {
            auto cumulativeTransform = bone->localTransform() + parentTransform;
            _currentTransforms[bone->skeletalIndex()] = cumulativeTransform.matrix();
            for (auto i=0; i<bone->childrenCount(); ++i)
            {
                auto child = bone->child(*this,i);
                updateBoneTransform(child, cumulativeTransform);
            }
        }
    } // core
} // crucible
