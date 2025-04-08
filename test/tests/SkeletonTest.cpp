#include "gtest/gtest.h"
#include <crucible/core/animation/skeleton.h>

using namespace crucible::core;
TEST(SkeletonTest, Serialize)
{
    std::vector<Bone> bones;
    bones.push_back(Bone(0,Transform(),UINT16_MAX,std::vector<uint16_t>{1,2}));
    bones.push_back(Bone(1,Transform(glm::vec3(1,5,2.2),glm::quat(2,glm::vec3(1,0,0)),1),0,std::vector<uint16_t>{}));
    bones.push_back(Bone(2,Transform(),0,std::vector<uint16_t>{3,5}));
    bones.push_back(Bone(3,Transform(),2,std::vector<uint16_t>{4}));
    bones.push_back(Bone(4,Transform(glm::vec3(-6,.068,-2.5),glm::quat(1.256f,glm::vec3(0.0f,1.0f,0.0f)),2),3,std::vector<uint16_t>{}));
    bones.push_back(Bone(5,Transform(),2,std::vector<uint16_t>{}));

    Skeleton skeleton(bones);
    auto serialized = skeleton.toLZ4();
    Skeleton skeleton2(serialized.data(), serialized.size());
    auto serialized2 = skeleton2.toLZ4();
    GTEST_ASSERT_EQ(serialized.size(), serialized2.size());
    for (auto i = 0; i < serialized.size(); ++i)
    {
        GTEST_ASSERT_EQ(serialized[i], serialized2[i]);
    }
    GTEST_ASSERT_EQ(skeleton.boneCount(), skeleton2.boneCount());
    for (auto i = 0; i < skeleton.boneCount(); ++i)
    {
        GTEST_ASSERT_TRUE(*(skeleton.getBone(i)) == *(skeleton2.getBone(i)));
    }
}

