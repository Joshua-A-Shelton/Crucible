#include "gtest/gtest.h"
#include <crucible/core/animation/Skeleton.h>

using namespace crucible::core;

TEST(SkeletonTest, Construct)
{
    std::vector<Bone> bones;
    bones.push_back(Bone(0,Transform(),UINT16_MAX,std::vector<uint16_t>{1,2}));
    bones.push_back(Bone(1,Transform(glm::vec3(1,5,2.2),glm::quat(2,glm::vec3(1,0,0)),1),0,std::vector<uint16_t>{}));
    bones.push_back(Bone(2,Transform(),0,std::vector<uint16_t>{3,5}));
    bones.push_back(Bone(3,Transform(),2,std::vector<uint16_t>{4}));
    bones.push_back(Bone(4,Transform(glm::vec3(-6,.068,-2.5),glm::quat(1.256f,glm::vec3(0.0f,1.0f,0.0f)),2),3,std::vector<uint16_t>{}));
    bones.push_back(Bone(5,Transform(),2,std::vector<uint16_t>{}));
    Skeleton skeleton1(bones);
    Skeleton skeleton2(std::move(bones));
    GTEST_ASSERT_EQ(skeleton1.boneCount(), skeleton2.boneCount());
    for (auto i = 0; i < skeleton1.boneCount(); i++)
    {
        GTEST_ASSERT_EQ(*skeleton1.getBone(i), *skeleton2.getBone(i));
    }
    GTEST_ASSERT_EQ(skeleton1.rootBone()->skeletalIndex(), skeleton2.rootBone()->skeletalIndex());
    Skeleton skeleton3(skeleton2);
    GTEST_ASSERT_EQ(skeleton2.boneCount(), skeleton3.boneCount());
    for (auto i = 0; i < skeleton2.boneCount(); i++)
    {
        GTEST_ASSERT_EQ(*skeleton2.getBone(i), *skeleton3.getBone(i));
    }
    GTEST_ASSERT_EQ(skeleton2.rootBone()->skeletalIndex(), skeleton3.rootBone()->skeletalIndex());
    Skeleton skeleton4(skeleton1);
    GTEST_ASSERT_EQ(skeleton4.boneCount(), skeleton3.boneCount());
    for (auto i = 0; i < skeleton3.boneCount(); i++)
    {
        GTEST_ASSERT_EQ(*skeleton3.getBone(i), *skeleton4.getBone(i));
    }
    GTEST_ASSERT_EQ(skeleton4.rootBone()->skeletalIndex(), skeleton3.rootBone()->skeletalIndex());
    auto lz4 = skeleton1.toLZ4();
    Skeleton skeleton5(lz4.data(),lz4.size());
    GTEST_ASSERT_EQ(skeleton5.boneCount(), skeleton4.boneCount());
    for (auto i = 0; i < skeleton5.boneCount(); i++)
    {
        GTEST_ASSERT_EQ(*skeleton5.getBone(i), *skeleton4.getBone(i));
    }
    GTEST_ASSERT_EQ(skeleton4.rootBone()->skeletalIndex(), skeleton5.rootBone()->skeletalIndex());
}

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

    GTEST_ASSERT_EQ(skeleton.rootBone()->skeletalIndex(), skeleton2.rootBone()->skeletalIndex());
}

TEST(SkeletonTest, InterpolateKeyframe)
{
    std::vector<Bone> bones;
    bones.push_back(Bone(0,Transform(),UINT16_MAX,std::vector<uint16_t>{1,2}));
    bones.push_back(Bone(1,Transform(glm::vec3(-1,1,0),glm::quat(1,0,0,0),1),0,std::vector<uint16_t>{}));
    bones.push_back(Bone(2,Transform(glm::vec3(1,1,0),glm::quat(1,0,0,0),1),0,std::vector<uint16_t>{}));
    Skeleton skeleton(bones);
    auto defaultKeyframe = skeleton.currentPoseToKeyframe();

    std::vector<Transform> transforms(3);
    auto& rootTransform = transforms[0];
    auto& left = transforms[1];
    auto& right = transforms[2];

    rootTransform = defaultKeyframe.localBoneTransform(0);
    left = defaultKeyframe.localBoneTransform(1);
    right = defaultKeyframe.localBoneTransform(2);

    rootTransform.rotate(glm::radians(45.0f), glm::vec3(1,0,0));

    Keyframe forward(transforms.data(), transforms.size());

    rootTransform.rotate(glm::radians(-90.0f), glm::vec3(1,0,0));

    left.translate(0,1,0);
    left.rotate(glm::radians(45.0f), normalize(glm::vec3(1,1,0)));


    right.scale(3);

    Keyframe back(transforms.data(), transforms.size());

    skeleton.interpolate(defaultKeyframe,forward,0);
    auto current = skeleton.currentPoseToKeyframe();
    GTEST_ASSERT_EQ(defaultKeyframe.boneCount(), current.boneCount());
    for (auto i = 0; i < current.boneCount(); ++i)
    {
        GTEST_ASSERT_TRUE(defaultKeyframe.localBoneTransform(i).approximateEquals(current.localBoneTransform(i)));
    }

    skeleton.interpolate(defaultKeyframe,forward,1);
    current = skeleton.currentPoseToKeyframe();

    GTEST_ASSERT_EQ(forward.boneCount(), current.boneCount());
    for (auto i = 0; i < current.boneCount(); ++i)
    {
        GTEST_ASSERT_TRUE(forward.localBoneTransform(i).approximateEquals(current.localBoneTransform(i)));
    }

    skeleton.interpolate(defaultKeyframe,forward,.5);
    current = skeleton.currentPoseToKeyframe();

    auto rootPos = current.localBoneTransform(0);
    auto bone1Pos = current.localBoneTransform(1);
    auto bone2Pos = current.localBoneTransform(2);

    auto euler = glm::eulerAngles(rootPos.rotation());
    auto degrees = glm::degrees(euler.x);
    GTEST_ASSERT_TRUE(abs(22.5f - degrees) < 0.001f);

    skeleton.snapToKeyframe(defaultKeyframe);
    skeleton.interpolate(defaultKeyframe,back,.5);

    current = skeleton.currentPoseToKeyframe();

    rootPos = current.localBoneTransform(0);
    bone1Pos = current.localBoneTransform(1);
    bone2Pos = current.localBoneTransform(2);

    euler = glm::eulerAngles(rootPos.rotation());
    degrees = glm::degrees(euler.x);
    GTEST_ASSERT_TRUE(abs(22.5f + degrees) < 0.001f);


    GTEST_ASSERT_TRUE(abs(1.5f - bone1Pos.position().y) < 0.001f);

    GTEST_ASSERT_TRUE(abs(2 - bone2Pos.scale().x) < 0.001f);
    GTEST_ASSERT_TRUE(abs(2 - bone2Pos.scale().y) < 0.001f);
    GTEST_ASSERT_TRUE(abs(2 - bone2Pos.scale().z) < 0.001f);
}

