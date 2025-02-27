#include "crucible/core/scenes/Node.h"
#include "crucible/scripting/ScriptingEngine.h"
#include "gtest/gtest.h"

TEST(ScriptingTest, Invoke)
{
    auto testBehaviorType = crucible::scripting::ScriptingEngine::getManagedType("CrucibleRuntimeTests.TestBehavior,Crucible-Runtime-Tests");

    crucible::core::Node root(testBehaviorType);

    auto& inst = root.script();
    double outValue = 0;
    inst.invokeMethodReturn("GetLastDeltaTime",0,nullptr,nullptr,&outValue);
    GTEST_ASSERT_EQ(outValue, 0);

    outValue = 100;

    std::vector<void*> paramValues{&outValue};
    auto doubleType = crucible::scripting::ScriptingEngine::getManagedType("System.Double");
    std::vector<crucible::scripting::ManagedType> paramTypes{doubleType};
    inst.invokeMethod("SetLastDeltaTime",1,paramTypes.data(),paramValues.data());

    inst.invokeMethodReturn("GetLastDeltaTime",0,nullptr,nullptr,&outValue);
    GTEST_ASSERT_EQ(outValue, 100);

    auto dummyObject = inst.invokeMethodReturn("CreateDummyObject",0,nullptr,nullptr);
    GTEST_ASSERT_FALSE(dummyObject.isNull());

    int32_t dummyValue = 0;
    dummyObject.invokeMethodReturn("ReturnSomeValue",0,nullptr,nullptr,&dummyValue);

    GTEST_ASSERT_EQ(dummyValue,500);
}