#include "gtest/gtest.h"
#include <crucible/core/Transform.h>

using namespace crucible::core;

TEST(TransformTest,Addition)
{
    float EPSILON = .000001;

    Transform transform1(glm::vec3(1,0,0),glm::quat(1,0,0,0),glm::vec3(1,1,1));
    Transform transform2(glm::vec3(0,1,0),glm::quat(1,0,0,0),glm::vec3(1,1,1));

    auto transformSum = transform1 + transform2;
    GTEST_ASSERT_EQ(transformSum.position(),glm::vec3(1,1,0));
    GTEST_ASSERT_EQ(transformSum.rotation(),glm::quat(1,0,0,0));
    GTEST_ASSERT_EQ(transformSum.scale(),glm::vec3(1,1,1));

    //rotate 90 degrees
    transform1.rotate(glm::angleAxis(1.5707963268f,glm::vec3(0.0f,0.0f,1.0f)));

    transformSum = transform1 + transform2;
    GTEST_ASSERT_TRUE(abs(transformSum.position().x - 2) <=EPSILON && abs(transformSum.position().y) <= EPSILON && abs(transformSum.position().z) <= EPSILON);
    GTEST_ASSERT_TRUE(transform1.rotation() == transformSum.rotation());

    transform1.scale(2);
    transformSum = transform1 + transform2;

    GTEST_ASSERT_TRUE(abs(transformSum.position().x - 3) <=EPSILON && abs(transformSum.position().y) <= EPSILON && abs(transformSum.position().z) <= EPSILON);
    GTEST_ASSERT_TRUE(transform1.rotation() == transformSum.rotation());
    GTEST_ASSERT_TRUE(transformSum.scale() == glm::vec3(2,2,2));

    Transform transform3
    (
        glm::vec3(-15,0,0),
        glm::quat(1.5707963268f,glm::vec3(0.0f,0.0f,1.0f)),
        glm::vec3(1,1,1)
    );

    transform1.setPosition(1203,.3,1);
    glm::quat r1(1.5707963268f,glm::vec3(0.0f,1.0f,0.0f));
    transform1.setRotation(r1);
    transform1.setScale(1);

    transform2.setPosition(1.1,1.2,-1.3);
    transform2.setRotation(glm::quat(1.5707963268f,glm::vec3(1.0f,0,0)));
    transform2.setScale(2);

    transformSum = (transform1+transform2)+transform3;
    auto transformSum2 = transform1 + (transform2 + transform3);

    auto sumMatrix = (transform1.matrix()*transform2.matrix())*transform3.matrix();
    auto sumMatrix2 = transform1.matrix()*(transform2.matrix()*transform3.matrix());

    auto differencePosition = transformSum.position() - transformSum2.position();
    auto differenceRotation = epsilonEqual(transformSum.rotation(),transformSum2.rotation(),EPSILON);
    auto differenceScale = transformSum.scale() - transformSum2.scale();

    GTEST_ASSERT_TRUE(abs(differencePosition.x) <= EPSILON && abs(differencePosition.y) <= EPSILON && abs(differencePosition.z) <= EPSILON);
    GTEST_ASSERT_TRUE(differenceRotation.x && differenceRotation.y && differenceRotation.z && differenceRotation.w);
    GTEST_ASSERT_TRUE(abs(differenceScale.x) <= EPSILON && abs(differenceScale.y) <= EPSILON && abs(differenceScale.z) <= EPSILON);

}