#include "crucible/core/scenes/Node.h"
#include "crucible/scripting/ScriptingEngine.h"
#include "gtest/gtest.h"

TEST(NodeBehavior, Scripting)
{
    auto testBehaviorType = crucible::scripting::ScriptingEngine::getManagedType("CrucibleRuntimeTests.TestBehavior,Crucible-Runtime-Tests");

    crucible::core::Node root(testBehaviorType);
    root.updateNode(.001);

    auto& inst = root.script();
    double outValue = -1;
    inst.invokeMethodReturn("GetLastDeltaTime",0,nullptr,nullptr,&outValue);
    GTEST_ASSERT_EQ(outValue, .001);

}