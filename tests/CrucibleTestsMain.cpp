#include <gtest/gtest.h>
#include <crucible/Crucible.h>
int main(int argc, char** argv)
{
    ::testing::InitGoogleTest(&argc, argv);
    crucible::initialize();
    auto run = RUN_ALL_TESTS();
    crucible::cleanup();
    return run;
}