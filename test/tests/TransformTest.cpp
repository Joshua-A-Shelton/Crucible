#include "gtest/gtest.h"
#include <crucible/core/Transform.h>

using namespace crucible::core;

TEST(TransformTest,Addition)
{
    float EPSILON = .000001;
    float NINTEY_DEGRESS_IN_RADIANS = glm::radians(90.0);

    Transform transform1(glm::vec3(1,0,0),glm::quat(1,0,0,0),glm::vec3(1,1,1));
    Transform transform2(glm::vec3(0,1,0),glm::quat(1,0,0,0),glm::vec3(1,1,1));

    auto transformSum = transform1 * transform2;

    auto epsilonPosition = glm::epsilonEqual(transformSum.position(),glm::vec3(1,1,0),EPSILON);
    auto epsilonRotation = glm::epsilonEqual(transformSum.rotation(),glm::quat(1,0,0,0),EPSILON);
    auto epsilonScale = glm::epsilonEqual(transformSum.scale(),glm::vec3(1,1,1),EPSILON);

    GTEST_ASSERT_TRUE(epsilonPosition.x && epsilonPosition.y && epsilonPosition.z);
    GTEST_ASSERT_TRUE(epsilonRotation.x && epsilonRotation.y && epsilonRotation.z && 1.0f - abs(transformSum.rotation().w) <= EPSILON);
    GTEST_ASSERT_TRUE(epsilonScale.x && epsilonScale.y && epsilonScale.z);

    //rotate 90 degrees
    transform1.rotate(glm::angleAxis(NINTEY_DEGRESS_IN_RADIANS,glm::vec3(0.0f,0.0f,1.0f)));

    transformSum = transform1 * transform2;

    epsilonPosition = glm::epsilonEqual(transformSum.position(),glm::vec3(2,0,0),EPSILON);
    epsilonRotation = glm::epsilonEqual(transformSum.rotation(),transform1.rotation(),EPSILON);
    epsilonScale = glm::epsilonEqual(transformSum.scale(),glm::vec3(1,1,1),EPSILON);

    GTEST_ASSERT_TRUE(epsilonPosition.x && epsilonPosition.y && epsilonPosition.z);
    GTEST_ASSERT_TRUE(epsilonRotation.x && epsilonRotation.y && epsilonRotation.z && epsilonRotation.w);
    GTEST_ASSERT_TRUE(epsilonScale.x && epsilonScale.y && epsilonScale.z);

    transform1.scale(2);
    transformSum = transform1 * transform2;

    epsilonPosition = glm::epsilonEqual(transformSum.position(),glm::vec3(3,0,0),EPSILON);
    epsilonRotation = glm::epsilonEqual(transformSum.rotation(),transform1.rotation(),EPSILON);
    epsilonScale = glm::epsilonEqual(transformSum.scale(),glm::vec3(2,2,2),EPSILON);

    GTEST_ASSERT_TRUE(epsilonPosition.x && epsilonPosition.y && epsilonPosition.z);
    GTEST_ASSERT_TRUE(epsilonRotation.x && epsilonRotation.y && epsilonRotation.z && epsilonRotation.w);
    GTEST_ASSERT_TRUE(epsilonScale.x && epsilonScale.y && epsilonScale.z);

    Transform transform3
    (
        glm::vec3(1,0,0),
        glm::angleAxis(NINTEY_DEGRESS_IN_RADIANS,glm::vec3(0.0f,0.0f,1)),
        glm::vec3(1,1,1)
    );

    auto transform4 = transform3;
    transform1 = transform3;
    transform2 = transform3;

    transformSum = transform1 * transform2 * transform3 * transform4;
    epsilonPosition = glm::epsilonEqual(transformSum.position(),glm::vec3(0,0,0),EPSILON);
    epsilonRotation = glm::epsilonEqual(transformSum.rotation(),glm::quat(1,0,0,0),EPSILON);
    epsilonScale = glm::epsilonEqual(transformSum.scale(),glm::vec3(1,1,1),EPSILON);

    GTEST_ASSERT_TRUE(epsilonPosition.x && epsilonPosition.y && epsilonPosition.z);
    GTEST_ASSERT_TRUE(epsilonRotation.x && epsilonRotation.y && epsilonRotation.z && 1.0f - abs(transformSum.rotation().w) <= EPSILON);
    GTEST_ASSERT_TRUE(epsilonScale.x && epsilonScale.y && epsilonScale.z);

    transform1.scale(.5);

    transformSum = transform1 * transform2 * transform3 * transform4;
    epsilonPosition = glm::epsilonEqual(transformSum.position(),glm::vec3(.5,0,0),EPSILON);
    epsilonRotation = glm::epsilonEqual(transformSum.rotation(),glm::quat(1,0,0,0),EPSILON);
    epsilonScale = glm::epsilonEqual(transformSum.scale(),glm::vec3(.5,.5,.5),EPSILON);

    GTEST_ASSERT_TRUE(epsilonPosition.x && epsilonPosition.y && epsilonPosition.z);
    GTEST_ASSERT_TRUE(epsilonRotation.x && epsilonRotation.y && epsilonRotation.z && 1.0f - abs(transformSum.rotation().w) <= EPSILON);
    GTEST_ASSERT_TRUE(epsilonScale.x && epsilonScale.y && epsilonScale.z);

    transform2.scale(2);
    transform3.scale(.5);
    transform4.scale(2);

    transformSum = transform1 * transform2 * transform3 * transform4;
    epsilonPosition = glm::epsilonEqual(transformSum.position(),glm::vec3(0,0,0),EPSILON);
    epsilonRotation = glm::epsilonEqual(transformSum.rotation(),glm::quat(1,0,0,0),EPSILON);
    epsilonScale = glm::epsilonEqual(transformSum.scale(),glm::vec3(1,1,1),EPSILON);

    GTEST_ASSERT_TRUE(epsilonPosition.x && epsilonPosition.y && epsilonPosition.z);
    GTEST_ASSERT_TRUE(epsilonRotation.x && epsilonRotation.y && epsilonRotation.z && 1.0f - abs(transformSum.rotation().w) <= EPSILON);
    GTEST_ASSERT_TRUE(epsilonScale.x && epsilonScale.y && epsilonScale.z);

}