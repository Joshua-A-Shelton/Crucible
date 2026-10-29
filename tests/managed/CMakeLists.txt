# Built by the crucible_managed target (the whole solution); registered here.
crucible_add_dotnet_test(Crucible.Engine.Tests)

# Hot reload is JIT-only (ADR 0001), so this suite has no AOT variant.
crucible_add_dotnet_test(Crucible.Engine.HotReloadTests)

# The asset pipeline is editor code, and the editor runs on a JIT (ADR 0016).
crucible_add_dotnet_test(Crucible.Assets.Tests)
# ufbx's sample files (ADR 0044), for the FBX importer's tests.
set_property(TEST Crucible.Assets.Tests APPEND PROPERTY ENVIRONMENT "CRUCIBLE_UFBX_DATA=${ufbx_SOURCE_DIR}/data")

# The source generator's tests run the C# compiler, which needs a JIT.
crucible_add_dotnet_test(Crucible.Generators.Tests)

# The same suite again as NativeAOT code (ADR 0001: every C# suite runs under
# both). Published after the engine's AOT build, since both build the engine.
if(CRUCIBLE_BUILD_AOT)
    crucible_add_dotnet_aot_test(Crucible.Engine.Tests
        "${PROJECT_SOURCE_DIR}/tests/managed/Crucible.Engine.Tests/Crucible.Engine.Tests.csproj"
        crucible_engine_aot)
endif()

# The M4 demonstration (ADR 0046): the sample program run on the project
# checked in beside it, failing on any failed check. Editor-side, so JIT only.
set(sceneRoundTrip "${CRUCIBLE_DOTNET_ARTIFACTS_DIR}/bin/Crucible.Samples.SceneRoundTrip/${CRUCIBLE_DOTNET_CONFIGURATION_DIR}/Crucible.Samples.SceneRoundTrip${CMAKE_EXECUTABLE_SUFFIX}")
add_test(NAME samples.scene-roundtrip
    COMMAND "${sceneRoundTrip}"
        --project "${PROJECT_SOURCE_DIR}/samples/SceneRoundTrip/Project/Demo.crproject"
        --work "${CMAKE_CURRENT_BINARY_DIR}/scene-roundtrip")
if(WIN32)
    set(_libraryPath PATH)
else()
    set(_libraryPath LD_LIBRARY_PATH)
endif()
set_tests_properties(samples.scene-roundtrip PROPERTIES
    LABELS integration
    PASS_REGULAR_EXPRESSION "All [0-9]+ checks passed"
    ENVIRONMENT_MODIFICATION "${_libraryPath}=path_list_prepend:$<TARGET_FILE_DIR:crucible_core>")
