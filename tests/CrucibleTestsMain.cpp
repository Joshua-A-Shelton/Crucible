#include <gtest/gtest.h>
#include <crucible/Crucible.h>

#include "crucible/scripting/ScriptingEngine.h"

int main(int argc, char** argv)
{
    ::testing::InitGoogleTest(&argc, argv);
    crucible::initialize();
    crucible::scripting::ScriptingEngine::loadCSharpDLL("Testing","Crucible-Runtime-Tests.dll");
    auto run = RUN_ALL_TESTS();
    crucible::scripting::ScriptingEngine::unloadAllContexts();
    crucible::cleanup();
    return run;
}
