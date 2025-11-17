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
TEST(ScriptingEngine, GetManagedTypeNonExistentFail)
{
    GTEST_FLAG_SET(death_test_style, "threadsafe");
    EXPECT_DEATH([this]{auto nonExistentType = ScriptingEngine::getManagedType("Crucible.NonExistentType");}(),"not found in loaded assemblies");
}

TEST(ScriptingEngine, GetManagedTypeNonAssemblyQualifiedFail)
{
    GTEST_FLAG_SET(death_test_style, "threadsafe");
    EXPECT_DEATH([this]{auto nonExistentType = ScriptingEngine::getManagedType("Crucible.Tests.Utilities.DummyReferenceType");}(),"not found in loaded assemblies");
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
    auto dummiesType = ScriptingEngine::getManagedType("Crucible.Tests.Utilities.Dummies, Crucible-Runtime-Tests");
    auto intType = ScriptingEngine::getManagedType("System.Int32");
    ManagedType paramTypes[]={intType,intType};

    EXPECT_DEATH((ScriptingEngine::getManagedFunctionDelegate<ManagedInstance,int,int>(dummiesType,"GetDummyReference",BindingFlags::PUBLIC | BindingFlags::STATIC, paramTypes,2)),"function does not have blittable return type");
}

TEST(ScriptingEngine, GetManagedFunctionFailNonBlitParameters)
{
    GTEST_FLAG_SET(death_test_style, "threadsafe");
    auto dummiesType = ScriptingEngine::getManagedType("Crucible.Tests.Utilities.Dummies, Crucible-Runtime-Tests");
    auto dummiesReferenceType = ScriptingEngine::getManagedType("Crucible.Tests.Utilities.DummyReferenceType, Crucible-Runtime-Tests");

    EXPECT_DEATH((ScriptingEngine::getManagedFunctionDelegate<int,ManagedInstance>(dummiesType,"GetDummyReferenceThing1",BindingFlags::PUBLIC | BindingFlags::STATIC, &dummiesReferenceType,1)),"function has non blittable parameter types");
}

TEST(ScriptingEngine, GetManagedFunctionFailNonStatic)
{
    GTEST_FLAG_SET(death_test_style, "threadsafe");
    auto dummiesReferenceType = ScriptingEngine::getManagedType("Crucible.Tests.Utilities.DummyReferenceType, Crucible-Runtime-Tests");

    EXPECT_DEATH((ScriptingEngine::getManagedFunctionDelegate<int>(dummiesReferenceType,"Sum",BindingFlags::PUBLIC | BindingFlags::INSTANCE, nullptr,0)),"function is not static");
}


TEST(ScriptingEngine, GetManagedTypeLoadedAssembly)
{
    auto dummiesType = ScriptingEngine::getManagedType("Crucible.Tests.Utilities.Dummies, Crucible-Runtime-Tests");
}

TEST(ScriptingEngine, GetManagedFunctionLoadedAssembly)
{
    auto dummiesType = ScriptingEngine::getManagedType("Crucible.Tests.Utilities.Dummies, Crucible-Runtime-Tests");
    auto intType = ScriptingEngine::getManagedType("System.Int32");
    ManagedType parameterTypes[]={intType,intType};
    auto loadedDLLStaticFunction = ScriptingEngine::getManagedFunctionDelegate<int,int,int>(dummiesType,"LoadedDLLStaticFunction",BindingFlags::PUBLIC | BindingFlags::STATIC,parameterTypes,2);
    auto sum = loadedDLLStaticFunction(4,7);
    GTEST_ASSERT_EQ(sum,11);
}

TEST(ScriptingEngine, GetInstance)
{
    auto dummiesReferenceType = ScriptingEngine::getManagedType("Crucible.Tests.Utilities.DummyReferenceType, Crucible-Runtime-Tests");
    auto intType = ScriptingEngine::getManagedType("System.Int32");
    ManagedType parameterTypes[]={intType,intType};
    int param1 = 3;
    int param2 = 9;
    void* parameterValues[]
    {
        &param1,
        &param2,
    };
    auto instance = ScriptingEngine::createManagedInstance(dummiesReferenceType, 2,parameterTypes,parameterValues);
}