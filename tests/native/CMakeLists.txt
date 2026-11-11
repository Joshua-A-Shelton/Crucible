add_executable(crucible_native_tests
    Main.cpp
    AudioContractTests.cpp
    AudioEffectTests.cpp
    AudioPauseTests.cpp
    AudioSchedulingTests.cpp
    BinaryFileTests.cpp
    CoreTests.cpp
    DebugLineTests.cpp
    SkinningTests.cpp
    SkinnedCullingTests.cpp
    DrawListTests.cpp
    EditorStepsTests.cpp
    EcsTests.cpp
    InputSourceTests.cpp
    InputTimingTests.cpp
    ContactEventTests.cpp
    ConvexColliderTests.cpp
    MeshColliderTests.cpp
    PhysicsBodyTests.cpp
    RaycastTests.cpp
    ShapeCastTests.cpp
    OverlapTests.cpp
    QueryFilterTests.cpp
    PhysicsInterpolationTests.cpp
    PhysicsSyncTests.cpp
    MixerTests.cpp
    MusicTests.cpp
    SpatialAudioTests.cpp
    JobTests.cpp
    LightTests.cpp
    LogTests.cpp
    MaterialTests.cpp
    MathTests.cpp
    MemoryTests.cpp
    PhysicsContractTests.cpp
    PlatformTests.cpp
    RenderAssetReloadTests.cpp
    RenderAssetsTests.cpp
    RenderCommandsTests.cpp
    RenderScheduleTests.cpp
    RenderTests.cpp
    ResultTests.cpp
    ShaderTests.cpp
    ShadowTests.cpp
    SimdTests.cpp
    SystemsTests.cpp
    TextTests.cpp
    OutlineTests.cpp
    PostProcessTests.cpp
    TransformTests.cpp
    TransparencyTests.cpp)

crucible_set_compile_options(crucible_native_tests)
crucible_use_d3d12_agility(crucible_native_tests)
# Tests build without exceptions too (ADR 0030); doctest's REQUIRE then ends
# the run on failure instead of throwing.
target_compile_definitions(crucible_native_tests PRIVATE DOCTEST_CONFIG_NO_EXCEPTIONS_BUT_WITH_ALL_ASSERTS)
# The renderer's internal headers, for the GPU tests' hooks, and the mirrors they
# include (shared.g.h).
target_include_directories(crucible_native_tests PRIVATE "${PROJECT_SOURCE_DIR}/src/native/core"
    "${PROJECT_BINARY_DIR}/src/native/core/shaders")
# SDL directly too: the platform tests push synthetic SDL events.
target_link_libraries(crucible_native_tests PRIVATE crucible::core_static SDL3::SDL3-static doctest::doctest)
# The shader toolchain's test shader (ADR 0047), with its C++ mirror here and
# its C# mirror for Crucible.Engine.Tests.
crucible_add_shader(crucible_native_tests SOURCE shaders/LayoutProbe.slang
    CS_OUTPUT_DIR "${CRUCIBLE_GENERATED_DIR}/managed/shaders")
add_dependencies(crucible_managed crucible_native_tests_shader_LayoutProbe)
# The camera's test shader (ADR 0050).
crucible_add_shader(crucible_native_tests SOURCE shaders/ViewProbe.slang)
# The batched command API's test shader (ADR 0058).
crucible_add_shader(crucible_native_tests SOURCE shaders/StepProbe.slang)
# Noto Sans (SIL Open Font License, fonts/OFL.txt), for the text tests.
target_compile_definitions(crucible_native_tests PRIVATE CRUCIBLE_TEST_FONT_DIR="${CMAKE_CURRENT_SOURCE_DIR}/fonts")
# A game's material shader (ADR 0052), compiled as the Shader importer does.
crucible_add_shader(crucible_native_tests SOURCE shaders/TestMaterials.slang MATERIALS)

# SDL's offscreen driver makes real windows that are never shown, so the tests
# need no display (Windows over SSH has none).
# ADD_LABELS turns test suites into labels: "gpu" tests need a Slag device.
# Test names must not contain commas: doctest's label lookup passes the name
# to --test-case unescaped, where a comma separates filters, so the test
# silently loses its labels.
doctest_discover_tests(crucible_native_tests ADD_LABELS 1
    PROPERTIES LABELS native ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen")

# The importers (ADR 0042), editor-only: their own executable, so the core's
# tests never link them.
add_executable(crucible_importers_tests Main.cpp FbxImportTests.cpp MeshImportTests.cpp ShaderCompilerTests.cpp
    SkinImportTests.cpp ClipImportTests.cpp PoseTests.cpp LayerTests.cpp StateMachineTests.cpp BlendSpaceTests.cpp
    AnimationEventTests.cpp RootMotionTests.cpp IkTests.cpp TextureImportTests.cpp SoundImportTests.cpp AdpcmTests.cpp
    VorbisStreamingTests.cpp CollisionCookingTests.cpp)
crucible_set_compile_options(crucible_importers_tests)
# ufbx's sample files (ADR 0044) are the FBX tests' models.
target_compile_definitions(crucible_importers_tests PRIVATE DOCTEST_CONFIG_NO_EXCEPTIONS_BUT_WITH_ALL_ASSERTS
    CRUCIBLE_UFBX_DATA_DIR="${ufbx_SOURCE_DIR}/data"
    CRUCIBLE_WALKER_DIR="${PROJECT_SOURCE_DIR}/samples/Walker/Project/Assets/Models"
    CRUCIBLE_SHADER_LIBRARY_DIR="${CRUCIBLE_SHADER_LIBRARY_DIR}"
    CRUCIBLE_SOUNDS_DIR="${CMAKE_CURRENT_SOURCE_DIR}/data/sounds")
target_include_directories(crucible_importers_tests SYSTEM PRIVATE "${stb_SOURCE_DIR}") # stb_image_write, to make test images
target_link_libraries(crucible_importers_tests PRIVATE crucible_importers_static doctest::doctest)
doctest_discover_tests(crucible_importers_tests ADD_LABELS 1 PROPERTIES LABELS "native;importers")

# ADR 0032: the core includes only standard headers that don't allocate.
add_test(NAME core.includes
    COMMAND "${CMAKE_COMMAND}" -DCORE_DIR=${PROJECT_SOURCE_DIR}/src/native/core
        -P "${PROJECT_SOURCE_DIR}/cmake/CheckCoreIncludes.cmake")
set_tests_properties(core.includes PROPERTIES LABELS native)

# A ';' in a test name makes CTest run nothing and report a pass; a ',' drops
# the test's suite label. Both are refused here.
add_test(NAME tests.names
    COMMAND "${CMAKE_COMMAND}" -DTEST_DIR=${CMAKE_CURRENT_SOURCE_DIR} -P "${PROJECT_SOURCE_DIR}/cmake/CheckTestNames.cmake")
set_tests_properties(tests.names PROPERTIES LABELS native)
# Native modules (ADR 0074) load into the shared core, as the launcher and
# editor use it, so their tests are an executable of their own.
add_library(crucible_test_module SHARED modules/TestModule.cpp)
crucible_set_compile_options(crucible_test_module)
set_target_properties(crucible_test_module PROPERTIES CXX_VISIBILITY_PRESET hidden)
target_link_libraries(crucible_test_module PRIVATE crucible::core)
add_library(crucible_not_a_module SHARED modules/NotAModule.cpp)
add_library(crucible_test_subsystems SHARED modules/TestSubsystems.cpp)
crucible_set_compile_options(crucible_test_subsystems)
set_target_properties(crucible_test_subsystems PROPERTIES CXX_VISIBILITY_PRESET hidden)
target_link_libraries(crucible_test_subsystems PRIVATE crucible::core)
add_executable(crucible_module_tests Main.cpp ModuleTests.cpp SubsystemReplacementTests.cpp)
crucible_set_compile_options(crucible_module_tests)
target_compile_definitions(crucible_module_tests PRIVATE DOCTEST_CONFIG_NO_EXCEPTIONS_BUT_WITH_ALL_ASSERTS
    CRUCIBLE_TEST_MODULE="$<TARGET_FILE:crucible_test_module>"
    CRUCIBLE_NOT_A_MODULE="$<TARGET_FILE:crucible_not_a_module>"
    CRUCIBLE_TEST_SUBSYSTEMS="$<TARGET_FILE:crucible_test_subsystems>")
target_link_libraries(crucible_module_tests PRIVATE crucible::core doctest::doctest)
add_dependencies(crucible_module_tests crucible_test_module crucible_not_a_module crucible_test_subsystems)
if(WIN32)
    add_custom_command(TARGET crucible_module_tests POST_BUILD
        COMMAND "${CMAKE_COMMAND}" -E copy_if_different
            "$<TARGET_FILE:crucible_core>" "$<TARGET_FILE_DIR:crucible_module_tests>"
        VERBATIM)
endif()
doctest_discover_tests(crucible_module_tests ADD_LABELS 1 PROPERTIES LABELS native)

# Every public declaration is exported for native modules (ADR 0074).
add_test(NAME exports.public
    COMMAND "${CMAKE_COMMAND}" -DHEADER_DIR=${PROJECT_SOURCE_DIR}/src/native/core/include/crucible
        -P "${PROJECT_SOURCE_DIR}/cmake/CheckExports.cmake")
set_tests_properties(exports.public PROPERTIES LABELS native)
