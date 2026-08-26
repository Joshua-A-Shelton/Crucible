#include <gtest/gtest.h>
#include <crucible/Crucible.h>
#include <crucible/scripting/core/Callbacks.h>
#ifdef CRUCIBLE_SCRIPTING_MODULE
using namespace crucible::scripting;

TEST(Scripting, Callbacks)
{
    GTEST_ASSERT_NE(ScriptingInitializeCallback, nullptr);
    GTEST_ASSERT_NE(ScriptingUpdateCallback, nullptr);
    GTEST_ASSERT_NE(ScriptingCleanupCallback, nullptr);

    ScriptingUpdateCallback(.01);
}
#endif
