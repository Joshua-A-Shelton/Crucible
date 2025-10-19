#include <gtest/gtest.h>

#include "crucible/scripting/BindingFlags.h"
#include "crucible/scripting/ScriptingEngine.h"

using namespace crucible::scripting;
TEST(API, ManagedAPISuite)
{
    auto apiType = ScriptingEngine::getManagedType("Crucible.Tests.API, Crucible-Runtime-Tests");
    auto runAllTests = ScriptingEngine::getManagedFunctionDelegate<int32_t>(apiType,"RunAllTests",BindingFlags::PUBLIC | BindingFlags::STATIC, nullptr, 0);
    auto result = runAllTests();
    GTEST_ASSERT_EQ(result,1);
}