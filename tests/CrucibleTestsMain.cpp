#include <gtest/gtest.h>
#include <crucible/Crucible.h>

#include "crucible/update/API_Update.h"
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

    crucible::CrucibleInitParams initParams{};
#ifdef CRUCIBLE_SCRIPTING_MODULE
    initParams.scriptingDLLPath = "Crucible-Runtime-Tests.dll";
#endif
    if (crucible::Crucible::initialize(initParams) != crucible::CrucibleInitializationResult::SUCCESS)
    {
        std::cout << "Failed to initialize Crucible" << std::endl;
        return -1;
    }

    auto run = RUN_ALL_TESTS();
    crucible::Crucible::cleanup();
    return run;
}