#include <gtest/gtest.h>
#include <crucible/Crucible.h>
// For Direct3D 12 Agility SDK
#ifdef SLAG_DX12_BACKEND
#include <intsafe.h>
extern "C"
{
__declspec(dllexport) extern const UINT D3D12SDKVersion = 616;
__declspec(dllexport) extern const char* D3D12SDKPath = ".\\D3D12\\";
}
#endif

int main(int argc, char* argv[])
{
    ::testing::InitGoogleTest(&argc, argv);

    if (crucible::Crucible::initialize() != crucible::CrucibleInitializationResult::SUCCESS)
    {
        std::cout << "Failed to initialize Crucible" << std::endl;
        return -1;
    }

    auto run = RUN_ALL_TESTS();
    crucible::Crucible::cleanup();
    return run;
}