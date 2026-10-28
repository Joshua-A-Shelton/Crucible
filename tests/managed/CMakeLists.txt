# Built by the crucible_managed target (the whole solution); registered here.
crucible_add_dotnet_test(Crucible.Engine.Tests)

# Hot reload is JIT-only (ADR 0001), so this suite has no AOT variant.
crucible_add_dotnet_test(Crucible.Engine.HotReloadTests)

# The asset pipeline is editor code, and the editor runs on a JIT (ADR 0016).
crucible_add_dotnet_test(Crucible.Assets.Tests)

# The source generator's tests run the C# compiler, which needs a JIT.
crucible_add_dotnet_test(Crucible.Generators.Tests)

# The same suite again as NativeAOT code (ADR 0001: every C# suite runs under
# both). Published after the engine's AOT build, since both build the engine.
if(CRUCIBLE_BUILD_AOT)
    crucible_add_dotnet_aot_test(Crucible.Engine.Tests
        "${PROJECT_SOURCE_DIR}/tests/managed/Crucible.Engine.Tests/Crucible.Engine.Tests.csproj"
        crucible_engine_aot)
endif()
