#include <gtest/gtest.h>
#include <crucible/Crucible.h>
#include <crucible/scripting/ScriptingEngine.h>
int main(int argc, char **argv)
{
    ::testing::InitGoogleTest(&argc, argv);
    int result = 0;
    if(crucible::Crucible::initialize(crucible::Crucible::VULKAN,true))
    {
        crucible::scripting::ScriptingEngine::loadManagedDll("Testing","plugins/Crucible-Runtime-Tests.dll",true);
        result = RUN_ALL_TESTS();
    }
    crucible::Crucible::cleanup();
    return result;
}