#include <gtest/gtest.h>
#include <crucible/Crucible.h>
#ifdef CRUCIBLE_MODULE_SCRIPTING
using namespace crucible::scripting;

class Scripting: public ::testing::Test
{
protected:
    static void SetUpTestSuite()
    {
        if (crucible::scripting::DLLLoader::loadAssembly("testing","Crucible-Runtime-Tests.dll") != AssemblyLoadResult::SUCCESS)
        {
            GTEST_FAIL() << "Failed to load test assembly";
        }
    }
};

TEST_F(Scripting, ManagedType)
{
    //load default type in runtime
    auto intType = ManagedType::get("System.Int32");
    GTEST_ASSERT_TRUE(intType.typeHandle() != nullptr);
    //load type in CrucibleRuntime.dll (This is the glue dll, and should always be loaded. Types from this dll don't require an assembly name)
    auto textureType = ManagedType::get("Crucible.Rendering.Texture");
    GTEST_ASSERT_TRUE(textureType.typeHandle() != nullptr);
    //load the type including the assembly name
    auto alsoTextureType = ManagedType::get("Crucible.Rendering.Texture, Crucible-Runtime");
    GTEST_ASSERT_EQ(textureType,alsoTextureType);
    //load type in runtime loaded dll (int this case CrucibleTimeTests.dll, name requires assembly name)
    auto dummyType = ManagedType::get("Crucible.Extras.Dummy, Crucible-Runtime-Tests");
    GTEST_ASSERT_TRUE(dummyType.typeHandle() != nullptr);

    GTEST_ASSERT_NE(intType,textureType);
    GTEST_ASSERT_NE(intType,dummyType);
    GTEST_ASSERT_NE(textureType,dummyType);
}
#endif
