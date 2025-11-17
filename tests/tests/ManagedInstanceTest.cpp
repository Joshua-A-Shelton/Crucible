#include "crucible/scripting/ScriptingEngine.h"
#include <gtest/gtest.h>
using namespace crucible::scripting;
TEST(ManagedInstance, IsNullEqualsCSharpNull)
{
    auto dummiesReferenceType = ScriptingEngine::getManagedType("Crucible.Tests.Utilities.DummyReferenceType, Crucible-Runtime-Tests");
    auto intType = ScriptingEngine::getManagedType("System.Int32");
    ManagedType managedTypes[] = {intType,intType};
    int param1 = 1;
    int param2 = 2;
    void* parameters[]{&param1, &param2};
    auto dummyInstance = ScriptingEngine::createManagedInstance(dummiesReferenceType,2,managedTypes,parameters);

    int makeNull = 0;
    void* parameters2[]{&makeNull};
    auto value = dummyInstance.invokeMethodReturn("SpanNew",1,&intType,parameters2);
    GTEST_ASSERT_TRUE(value.isNull());
    makeNull = 1;
    auto value2 = dummyInstance.invokeMethodReturn("SpanNew",1,&intType,parameters2);
    GTEST_ASSERT_TRUE(!value2.isNull());
}

TEST(ManagedInstance, InvokeMethod)
{
    auto dummiesReferenceType = ScriptingEngine::getManagedType("Crucible.Tests.Utilities.DummyReferenceType, Crucible-Runtime-Tests");
    auto intType = ScriptingEngine::getManagedType("System.Int32");
    ManagedType managedTypes[] = {intType,intType};
    int param1 = 1;
    int param2 = 2;
    void* parameters[]{&param1, &param2};
    auto dummyInstance = ScriptingEngine::createManagedInstance(dummiesReferenceType,2,managedTypes,parameters);
    testing::internal::CaptureStdout();
    dummyInstance.invokeMethodReturn("WriteToConsole",0,nullptr,nullptr);
    std::string captured = testing::internal::GetCapturedStdout();
    GTEST_ASSERT_EQ(captured,"Dummy write to console");
}

TEST(ManagedInstance, InvokeMethodInstanceReturn)
{
    auto dummiesReferenceType = ScriptingEngine::getManagedType("Crucible.Tests.Utilities.DummyReferenceType, Crucible-Runtime-Tests");
    auto intType = ScriptingEngine::getManagedType("System.Int32");
    ManagedType managedTypes[] = {intType,intType};
    int param1 = 1;
    int param2 = 2;
    void* parameters[]{&param1, &param2};
    auto dummyInstance = ScriptingEngine::createManagedInstance(dummiesReferenceType,2,managedTypes,parameters);

    int makeNull = 0;
    void* parameters2[]{&makeNull};
    auto value = dummyInstance.invokeMethodReturn("SpanNew",1,&intType,parameters2);
    GTEST_ASSERT_TRUE(value.isNull());
}

TEST(ManagedInstance, InvokeMethodValueReturn)
{
    auto dummiesReferenceType = ScriptingEngine::getManagedType("Crucible.Tests.Utilities.DummyReferenceType, Crucible-Runtime-Tests");
    auto intType = ScriptingEngine::getManagedType("System.Int32");
    ManagedType managedTypes[] = {intType,intType};
    int param1 = 1;
    int param2 = 2;
    void* parameters[]{&param1, &param2};
    auto dummyInstance = ScriptingEngine::createManagedInstance(dummiesReferenceType,2,managedTypes,parameters);

    float sumAsFloat = 0;
    dummyInstance.invokeMethodReturn("SumAsFloat",0,nullptr,nullptr,&sumAsFloat);
    GTEST_ASSERT_EQ(sumAsFloat,3.0f);

    struct TestingVector3{float x,y,z;};
    TestingVector3 v3{0,0,0};
    dummyInstance.invokeMethodReturn("AsVector",0,nullptr,nullptr,&v3);
    GTEST_ASSERT_EQ(v3.x,1.0f);
    GTEST_ASSERT_EQ(v3.y,2.0f);
    GTEST_ASSERT_EQ(v3.z,0.0f);
}