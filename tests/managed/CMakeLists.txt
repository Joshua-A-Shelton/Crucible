# Built by the crucible_managed target (the whole solution); registered here.
crucible_add_dotnet_test(Crucible.Engine.Tests)

# The same suite again as NativeAOT code (ADR 0001: every C# suite runs under
# both). Published after the engine's AOT build, since both build the engine.
if(CRUCIBLE_BUILD_AOT)
    crucible_add_dotnet_aot_test(Crucible.Engine.Tests
        "${PROJECT_SOURCE_DIR}/tests/managed/Crucible.Engine.Tests/Crucible.Engine.Tests.csproj"
        crucible_engine_aot)
endif()
