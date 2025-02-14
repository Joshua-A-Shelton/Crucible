#include <gtest/gtest.h>
#include <crucible/Crucible.h>
int main(int argc, char **argv)
{
    ::testing::InitGoogleTest(&argc, argv);
    int result = 0;
    if(crucible::Crucible::initialize(crucible::Crucible::VULKAN,true))
    {
        result = RUN_ALL_TESTS();
    }
    crucible::Crucible::cleanup();
    return result;
}