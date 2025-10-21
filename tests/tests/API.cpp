#include <gtest/gtest.h>
#include <glm/glm.hpp>
#include <glm/gtc/quaternion.hpp>
#include "crucible/scripting/BindingFlags.h"
#include "crucible/scripting/ScriptingEngine.h"
#include "crucible/scripting/API.h"

using namespace crucible::scripting;
TEST(API, ScriptingFrameworkTests)
{
    auto apiType = ScriptingEngine::getManagedType("Crucible.Tests.API, Crucible-Runtime-Tests");
    auto runAllTests = ScriptingEngine::getManagedFunctionDelegate<int32_t>(apiType,"RunAllTests",BindingFlags::PUBLIC | BindingFlags::STATIC, nullptr, 0);
    auto result = runAllTests();
    GTEST_ASSERT_EQ(result,1);
}

TEST(API,Matrix4x4Multiply)
{
    glm::mat4 projection = glm::perspective(glm::radians(45.0f), 1.0f, 0.1f, 100.0f);
    glm::mat4 transform = glm::mat4(1.0f);
    transform = glm::rotate(transform, glm::radians(90.0f), glm::vec3(1.0f, 0.0f, 0.0f));
    transform = glm::translate(transform, glm::vec3(1.0f, 2.0f, 3.0f));
    transform = glm::scale(transform, glm::vec3(0.5f, 0.5f, 0.5f));

    glm::mat4 expected = transform * projection;
    glm::mat4 actual = glm::mat4(1.0f);
    slag::scripting::CRUCIBLE_NATIVE_Matrix4X4Multiply(transform,projection,actual);
    GTEST_ASSERT_EQ(actual,expected);
}

TEST(API,QuaternionFromAngleAxis)
{
    float angle = glm::radians(35.0f);
    glm::vec3 axis = glm::normalize(glm::vec3(1.0f, 0.0f, 1.0f));
    glm::quat expected = glm::quat(angle, axis);
    glm::quat actual;
    slag::scripting::CRUCIBLE_NATIVE_QuaternionFromAngleAxis(angle, axis, actual);
    GTEST_ASSERT_EQ(actual,expected);
}
TEST(API,QuaternionFromEulerAngles)
{
    glm::vec3 angles = glm::vec3(glm::radians(35.0f), glm::radians(21.0f), glm::radians(5.0f));
    glm::quat expected = glm::quat(angles);
    glm::quat actual;
    slag::scripting::CRUCIBLE_NATIVE_QuaternionFromEulerAngles(angles,actual);
    GTEST_ASSERT_EQ(actual,expected);
}
TEST(API,QuaternionMultiply)
{
    glm::quat q1(glm::radians(45.0f), glm::vec3(1.0f, 0.0f, 0.0f));
    glm::quat q2(glm::radians(35.0f), glm::vec3(0.0f, 1.0f, 0.0f));
    auto expected = q1*q2;
    glm::quat actual;
    slag::scripting::CRUCIBLE_NATIVE_QuaternionMultiply(q1,q2,actual);
    GTEST_ASSERT_EQ(expected,actual);
}
TEST(API,QuaternionInverse)
{
    glm::quat q1(glm::radians(45.0f), glm::vec3(1.0f, 0.0f, 0.0f));
    auto expected = glm::inverse(q1);
    glm::quat actual;
    slag::scripting::CRUCIBLE_NATIVE_QuaternionInverse(q1,actual);
    GTEST_ASSERT_EQ(expected,actual);
}

TEST(API,Vector4MatrixMultiply)
{
    glm::vec4 vector = glm::vec4(5.6f, 2.3f, 33.025f, 1.0f);
    glm::mat4 transform = glm::mat4(1.0f);
    transform = glm::translate(transform, glm::vec3(1.0f, 2.0f, 3.0f));
    transform = glm::scale(transform, glm::vec3(0.5f, 0.5f, 0.5f));
    transform = glm::scale(transform, glm::vec3(0.5f, 0.5f, 0.5f));
    auto expected = vector * transform;
    glm::vec4 actual;
    slag::scripting::CRUCIBLE_NATIVE_Vector4MatrixMultipy(vector, transform, actual);
    GTEST_ASSERT_EQ(actual,expected);
}
TEST(API,Vector4DotProduct)
{
    glm::vec4 vector1 = glm::vec4(5.6f, 2.3f, 33.025f, 1.0f);
    glm::vec4 vector2 = glm::vec4(12.0f, 0.0f, -13.5, 18.0f);
    auto expected = glm::dot(vector1, vector2);
    auto actual = slag::scripting::CRUCIBLE_NATIVE_Vector4DotProduct(vector1, vector2);
    GTEST_ASSERT_EQ(actual,expected);
}
TEST(API, Vector3MatrixMultiply)
{
    glm::vec3 vector1 = glm::vec3(5.6f, 2.3f, 33.025f);
    glm::vec4 vector2 = glm::vec4(vector1, 1.0f);
    glm::mat4 transform = glm::mat4(1.0f);
    transform = glm::translate(transform, glm::vec3(1.0f, 2.0f, 3.0f));
    transform = glm::scale(transform, glm::vec3(0.5f, 0.5f, 0.5f));
    transform = glm::scale(transform, glm::vec3(0.5f, 0.5f, 0.5f));
    auto expected4 = vector2 * transform;
    glm::vec3 expected(expected4.x,expected4.y,expected4.z);
    glm::vec3 actual;
    slag::scripting::CRUCIBLE_NATIVE_Vector3MatrixMultipy(vector1, transform, actual);
    GTEST_ASSERT_EQ(expected,actual);
}
TEST(API,Vector3DotProduct)
{
    glm::vec3 vector1 = glm::vec3(5.6f, 2.3f, 33.025f);
    glm::vec3 vector2 = glm::vec3(12.0f, 0.0f, -13.5);
    auto expected = glm::dot(vector1, vector2);
    auto actual = slag::scripting::CRUCIBLE_NATIVE_Vector3DotProduct(vector1, vector2);
    GTEST_ASSERT_EQ(actual,expected);
}
TEST(API,Vector3CrossProduct)
{
    glm::vec3 vector1 = glm::vec3(5.6f, 2.3f, 33.025f);
    glm::vec3 vector2 = glm::vec3(12.0f, 0.0f, -13.5);
    auto expected = glm::cross(vector1, vector2);
    glm::vec3 actual;
    slag::scripting::CRUCIBLE_NATIVE_Vector3CrossProduct(vector1, vector2, actual);
    GTEST_ASSERT_EQ(actual,expected);
}
