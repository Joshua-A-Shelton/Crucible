#include <gtest/gtest.h>
#include <glm/glm.hpp>
#include <glm/gtc/quaternion.hpp>
#include "crucible/scripting/BindingFlags.h"
#include "crucible/scripting/ScriptingEngine.h"

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
    GTEST_FAIL();
}

TEST(API,QuaternionFromAngleAxis)
{
    GTEST_FAIL();
}
TEST(API,QuaternionFromEulerAngles)
{
    GTEST_FAIL();
}
TEST(API,QuaternionMultiply)
{
    GTEST_FAIL();
}
TEST(API,QuaternionInverse)
{
    GTEST_FAIL();
}

TEST(API,Vector4MatrixMultiply)
{
    GTEST_FAIL();
}
TEST(API,Vector4DotProduct)
{
    GTEST_FAIL();
}
TEST(API, Vector3MatrixMultiply)
{
    GTEST_FAIL();
}
TEST(API,Vector3DotProduct)
{
    GTEST_FAIL();
}
TEST(API,Vector3CrossProduct)
{
    GTEST_FAIL();
}
