#include "crucible/scripting/ScriptingEngine.h"

#include <gtest/gtest.h>

#include "crucible/scripting/BindingFlags.h"
using namespace crucible::scripting;
TEST(ScriptingEngine, GetManagedType)
{
    auto gameManagerType = ScriptingEngine::getManagedType("Crucible.Core.GameManager");
}
TEST(ScriptingEngine, GetManagedTypeEmptyFail)
{
    GTEST_FLAG_SET(death_test_style, "threadsafe");
    EXPECT_DEATH([this]{auto nonExistentType = ScriptingEngine::getManagedType("typeName is null or empty");}(),"");
}
TEST(ScriptingEngine, GetManagedTypeFail)
{
    GTEST_FLAG_SET(death_test_style, "threadsafe");
    EXPECT_DEATH([this]{auto nonExistentType = ScriptingEngine::getManagedType("Crucible.NonExistentType");}(),"not found in loaded assemblies");
}

TEST(ScriptingEngine, GetManagedFunctionDelegate)
{
    auto gameManagerType = ScriptingEngine::getManagedType("Crucible.Core.Math.Common");
    auto floatType = ScriptingEngine::getManagedType("System.Single");
    ManagedType parameterTypes[]={floatType,floatType};
    auto approximately = ScriptingEngine::getManagedFunctionDelegate<bool,float,float>(gameManagerType,"Approximately",BindingFlags::PUBLIC | BindingFlags::STATIC,parameterTypes,2);
    GTEST_ASSERT_TRUE(approximately(1.0f,1.0f));
    GTEST_ASSERT_FALSE(approximately(1.0f,20.0f));
}
TEST(ScriptingEngine, GetManagedFunctionDelegateBlitReturnType)
{
    auto vector3Type = ScriptingEngine::getManagedType("Crucible.Core.Math.Vector3");
    struct TestingVector3{float x,y,z;};
    auto up = ScriptingEngine::getManagedFunctionDelegate<TestingVector3>(vector3Type,"Up",BindingFlags::PUBLIC | BindingFlags::STATIC,nullptr,0);
    auto transformed = up();
    GTEST_ASSERT_EQ(transformed.x,0.0f);
    GTEST_ASSERT_EQ(transformed.y,1.0f);
    GTEST_ASSERT_EQ(transformed.z,0.0f);
}
TEST(ScriptingEngine, GetManagedFunctionFailNonBlitReturnTypes)
{
    GTEST_FLAG_SET(death_test_style, "threadsafe");
    GTEST_FAIL();
}

TEST(ScriptingEngine, GetManagedFunctionDelegateNonBlitParameters)
{
    GTEST_FLAG_SET(death_test_style, "threadsafe");
    GTEST_FAIL();
}

TEST(ScriptingEngine, GetManagedTypeLoadedAssembly)
{
    GTEST_FAIL();
}

TEST(ScriptingEngine, GetManagedFunctionLoadedAssembly)
{
    GTEST_FAIL();
}

TEST(ScriptingEngine, GetInstance)
{
    GTEST_FAIL();
}

TEST(ScriptingEngine, InvokeMethodNoReturn)
{
    GTEST_FAIL();
}

TEST(ScriptingEngine, InvokeMethodReferenceReturn)
{
    GTEST_FAIL();
}

TEST(ScriptingEngine, InvokeMethodInstanceReturn)
{
    GTEST_FAIL();
}