#include "gtest/gtest.h"
#include <crucible/core/Transform.h>
#include <crucible/core/scenes/Node.h>
#include <crucible/scripting/CoreFunctions.h>

using namespace crucible::core;

float EPSILON = .00001;
float NINTEY_DEGREES_IN_RADIANS = glm::radians(90.0);
const char* TRANSFORM_CLASS = "Crucible.Core.Transform";

TEST(TransformTest,Addition)
{

    Transform transform1(glm::vec3(1,0,0),glm::quat(1,0,0,0),glm::vec3(1,1,1));
    Transform transform2(glm::vec3(0,1,0),glm::quat(1,0,0,0),glm::vec3(1,1,1));

    auto transformSum = transform2+transform1;

    auto epsilonPosition = glm::epsilonEqual(transformSum.position(),glm::vec3(1,1,0),EPSILON);
    auto epsilonRotation = glm::epsilonEqual(transformSum.rotation(),glm::quat(1,0,0,0),EPSILON);
    auto epsilonScale = glm::epsilonEqual(transformSum.scale(),glm::vec3(1,1,1),EPSILON);

    GTEST_ASSERT_TRUE(epsilonPosition.x && epsilonPosition.y && epsilonPosition.z);
    GTEST_ASSERT_TRUE(epsilonRotation.x && epsilonRotation.y && epsilonRotation.z && 1.0f - abs(transformSum.rotation().w) <= EPSILON);
    GTEST_ASSERT_TRUE(epsilonScale.x && epsilonScale.y && epsilonScale.z);

    //rotate 90 degrees
    transform1.rotate(glm::angleAxis(NINTEY_DEGREES_IN_RADIANS, glm::vec3(0.0f, 0.0f, 1.0f)));

    transformSum = transform2+transform1;

    epsilonPosition = glm::epsilonEqual(transformSum.position(),glm::vec3(2,0,0),EPSILON);
    epsilonRotation = glm::epsilonEqual(transformSum.rotation(),transform1.rotation(),EPSILON);
    epsilonScale = glm::epsilonEqual(transformSum.scale(),glm::vec3(1,1,1),EPSILON);

    GTEST_ASSERT_TRUE(epsilonPosition.x && epsilonPosition.y && epsilonPosition.z);
    GTEST_ASSERT_TRUE(epsilonRotation.x && epsilonRotation.y && epsilonRotation.z && epsilonRotation.w);
    GTEST_ASSERT_TRUE(epsilonScale.x && epsilonScale.y && epsilonScale.z);

    transform1.scale(2);
    transformSum = transform2+transform1;

    epsilonPosition = glm::epsilonEqual(transformSum.position(),glm::vec3(3,0,0),EPSILON);
    epsilonRotation = glm::epsilonEqual(transformSum.rotation(),transform1.rotation(),EPSILON);
    epsilonScale = glm::epsilonEqual(transformSum.scale(),glm::vec3(2,2,2),EPSILON);

    GTEST_ASSERT_TRUE(epsilonPosition.x && epsilonPosition.y && epsilonPosition.z);
    GTEST_ASSERT_TRUE(epsilonRotation.x && epsilonRotation.y && epsilonRotation.z && epsilonRotation.w);
    GTEST_ASSERT_TRUE(epsilonScale.x && epsilonScale.y && epsilonScale.z);

    Transform transform3
    (
        glm::vec3(1,0,0),
        glm::angleAxis(NINTEY_DEGREES_IN_RADIANS, glm::vec3(0.0f, 0.0f, 1)),
        glm::vec3(1,1,1)
    );

    auto transform4 = transform3;
    transform1 = transform3;
    transform2 = transform3;

    transformSum = transform4 + transform3 + transform2 + transform1;
    epsilonPosition = glm::epsilonEqual(transformSum.position(),glm::vec3(0,0,0),EPSILON);
    epsilonRotation = glm::epsilonEqual(transformSum.rotation(),glm::quat(1,0,0,0),EPSILON);
    epsilonScale = glm::epsilonEqual(transformSum.scale(),glm::vec3(1,1,1),EPSILON);

    GTEST_ASSERT_TRUE(epsilonPosition.x && epsilonPosition.y && epsilonPosition.z);
    GTEST_ASSERT_TRUE(epsilonRotation.x && epsilonRotation.y && epsilonRotation.z && 1.0f - abs(transformSum.rotation().w) <= EPSILON);
    GTEST_ASSERT_TRUE(epsilonScale.x && epsilonScale.y && epsilonScale.z);

    transform1.scale(.5);

    transformSum = transform4 + transform3 + transform2 + transform1;
    epsilonPosition = glm::epsilonEqual(transformSum.position(),glm::vec3(.5,0,0),EPSILON);
    epsilonRotation = glm::epsilonEqual(transformSum.rotation(),glm::quat(1,0,0,0),EPSILON);
    epsilonScale = glm::epsilonEqual(transformSum.scale(),glm::vec3(.5,.5,.5),EPSILON);

    GTEST_ASSERT_TRUE(epsilonPosition.x && epsilonPosition.y && epsilonPosition.z);
    GTEST_ASSERT_TRUE(epsilonRotation.x && epsilonRotation.y && epsilonRotation.z && 1.0f - abs(transformSum.rotation().w) <= EPSILON);
    GTEST_ASSERT_TRUE(epsilonScale.x && epsilonScale.y && epsilonScale.z);

    transform2.scale(2);
    transform3.scale(.5);
    transform4.scale(2);

    transformSum = transform4 + transform3 + transform2 + transform1;
    epsilonPosition = glm::epsilonEqual(transformSum.position(),glm::vec3(0,0,0),EPSILON);
    epsilonRotation = glm::epsilonEqual(transformSum.rotation(),glm::quat(1,0,0,0),EPSILON);
    epsilonScale = glm::epsilonEqual(transformSum.scale(),glm::vec3(1,1,1),EPSILON);

    GTEST_ASSERT_TRUE(epsilonPosition.x && epsilonPosition.y && epsilonPosition.z);
    GTEST_ASSERT_TRUE(epsilonRotation.x && epsilonRotation.y && epsilonRotation.z && 1.0f - abs(transformSum.rotation().w) <= EPSILON);
    GTEST_ASSERT_TRUE(epsilonScale.x && epsilonScale.y && epsilonScale.z);

}

TEST(TransformTest, Subtraction)
{
    Transform t1;
    t1.setPosition(5,2.2,15);
    t1.setRotation(glm::angleAxis(NINTEY_DEGREES_IN_RADIANS,glm::vec3(1,0,0)));
    t1.setScale(-.7,1.5,.8);

    Transform t2;
    t2.setPosition(3,-1.3,4);
    t2.setRotation(glm::angleAxis(NINTEY_DEGREES_IN_RADIANS,glm::vec3(0,1,0)));
    t2.setScale(3);

    Transform sum = t2+t1;
    Transform difference = sum-t2;

    auto epsilonPosition = glm::epsilonEqual(difference.position(),t1.position(),EPSILON);
    auto epsilonRotation = glm::epsilonEqual(difference.rotation(),t1.rotation(),EPSILON);
    auto epsilonScale = glm::epsilonEqual(difference.scale(),t1.scale(),EPSILON);

    GTEST_ASSERT_TRUE(epsilonPosition.x && epsilonPosition.y && epsilonPosition.z);
    GTEST_ASSERT_TRUE(epsilonRotation.w && epsilonRotation.x && epsilonRotation.y && epsilonRotation.z);
    GTEST_ASSERT_TRUE(epsilonScale.x && epsilonScale.y && epsilonScale.z);


    t1.setPosition(15, .3f, -2565);
    t1.setRotation(glm::angleAxis(1.57079632679f,normalize(glm::vec3(1,2,0))));
    t1.setScale(1,1,1);

    t2.setPosition(-400,1.5f,0);
    t2.setRotation(glm::quat(1,0,0,0));
    t2.setScale(2, 3, 2);

    sum = t2+t1;
    difference = sum-t2;

    epsilonPosition = glm::epsilonEqual(difference.position(),t1.position(),EPSILON);
    epsilonRotation = glm::epsilonEqual(difference.rotation(),t1.rotation(),EPSILON);
    epsilonScale = glm::epsilonEqual(difference.scale(),t1.scale(),EPSILON);

    GTEST_ASSERT_TRUE(epsilonPosition.x && epsilonPosition.y && epsilonPosition.z);
    GTEST_ASSERT_TRUE(epsilonRotation.w && epsilonRotation.x && epsilonRotation.y && epsilonRotation.z);
    GTEST_ASSERT_TRUE(epsilonScale.x && epsilonScale.y && epsilonScale.z);

    sum = t1+t2;
    difference = sum-t1;

    epsilonPosition = glm::epsilonEqual(difference.position(),t2.position(),EPSILON);
    epsilonRotation = glm::epsilonEqual(difference.rotation(),t2.rotation(),EPSILON);
    epsilonScale = glm::epsilonEqual(difference.scale(),t2.scale(),EPSILON);

    GTEST_ASSERT_TRUE(epsilonPosition.x && epsilonPosition.y && epsilonPosition.z);
    GTEST_ASSERT_TRUE(epsilonRotation.w && epsilonRotation.x && epsilonRotation.y && epsilonRotation.z);
    GTEST_ASSERT_TRUE(epsilonScale.x && epsilonScale.y && epsilonScale.z);

}

TEST(TransformTest, Heirarchy)
{
    Node root(nullptr);
    Node* child = root.addChild();
    Node* grandChild = child->addChild();
    Node* greatGrandChild = grandChild->addChild();

    Transform transform;
    transform.setPosition(1,0,2);

    crucible::scripting::cs_nodePointerAddDataComponent(&root,TRANSFORM_CLASS,sizeof(Transform), alignof(Transform),&transform);


    crucible::scripting::cs_nodePointerAddDataComponent(grandChild,TRANSFORM_CLASS,sizeof(Transform), alignof(Transform),&transform);
    crucible::scripting::cs_nodePointerAddDataComponent(greatGrandChild,TRANSFORM_CLASS,sizeof(Transform), alignof(Transform),&transform);

    auto ggChildTransform = (Transform*)crucible::scripting::cs_nodePointerGetDataComponent(greatGrandChild,TRANSFORM_CLASS,sizeof(Transform), alignof(Transform));
    auto finalTransform = ggChildTransform->toGlobal(grandChild);

    auto epsilonPosition = glm::epsilonEqual(finalTransform.position(),glm::vec3(3,0,6),EPSILON);

    GTEST_ASSERT_TRUE(epsilonPosition.x && epsilonPosition.y && epsilonPosition.z);
}

TEST(TransformTest, Inverse)
{
    Transform identity;

    Transform transform;
    transform.setPosition(15,23,-1.568);
    transform.setRotation(glm::angleAxis(1.57079632679f,normalize(glm::vec3(1,2,3))));
    transform.setScale(1,1,1);

    Transform result = transform + transform.inverse();

    auto epsilonPosition = glm::epsilonEqual(result.position(),identity.position(),EPSILON);
    auto epsilonRotation = glm::epsilonEqual(result.rotation(),identity.rotation(),EPSILON);
    auto epsilonScale = glm::epsilonEqual(result.scale(),identity.scale(),EPSILON);

    GTEST_ASSERT_TRUE(epsilonPosition.x && epsilonPosition.y && epsilonPosition.z);
    GTEST_ASSERT_TRUE(epsilonRotation.w && epsilonRotation.x && epsilonRotation.y && epsilonRotation.z);
    GTEST_ASSERT_TRUE(epsilonScale.x && epsilonScale.y && epsilonScale.z);

    transform.setScale(1.5,2,-.3);

    result = transform + transform.inverse();

    epsilonPosition = glm::epsilonEqual(result.position(),identity.position(),EPSILON);
    epsilonRotation = glm::epsilonEqual(result.rotation(),identity.rotation(),EPSILON);
    epsilonScale = glm::epsilonEqual(result.scale(),identity.scale(),EPSILON);

    GTEST_ASSERT_TRUE(epsilonPosition.x && epsilonPosition.y && epsilonPosition.z);
    GTEST_ASSERT_TRUE(epsilonRotation.w && epsilonRotation.x && epsilonRotation.y && epsilonRotation.z);
    GTEST_ASSERT_TRUE(epsilonScale.x && epsilonScale.y && epsilonScale.z);

}

TEST(TransformTest, ScriptingConcat)
{
    auto transformTestType = crucible::scripting::ScriptingEngine::getManagedType("CrucibleRuntimeTests.TransformTest,Crucible-Runtime-Tests");
    auto concat = transformTestType.getFunction<bool (*)()>("Concat");
    GTEST_ASSERT_TRUE(concat());
}

TEST(TransformTest, ScriptingDecat)
{
    auto transformTestType = crucible::scripting::ScriptingEngine::getManagedType("CrucibleRuntimeTests.TransformTest,Crucible-Runtime-Tests");
    auto concat = transformTestType.getFunction<bool (*)()>("Decat");
    GTEST_ASSERT_TRUE(concat());
}

TEST(TransformTest, ScriptingInverse)
{
    auto transformTestType = crucible::scripting::ScriptingEngine::getManagedType("CrucibleRuntimeTests.TransformTest,Crucible-Runtime-Tests");
    auto invert = transformTestType.getFunction<bool (*)()>("Inverse");
    GTEST_ASSERT_TRUE(invert());
}

TEST(TransformTest, ScriptingCumulative)
{
    Node node(nullptr);

    auto transformTestType = crucible::scripting::ScriptingEngine::getManagedType("CrucibleRuntimeTests.TransformTest,Crucible-Runtime-Tests");
    auto cumulative = transformTestType.getFunction<bool (*)(const boost::uuids::uuid&)>("Cumulative");
    GTEST_ASSERT_TRUE(cumulative(node.uuid()));
}