add_executable(crucible_native_tests
    Main.cpp
    BinaryFileTests.cpp
    CoreTests.cpp
    DrawListTests.cpp
    EcsTests.cpp
    InputTimingTests.cpp
    JobTests.cpp
    MathTests.cpp
    MemoryTests.cpp
    PlatformTests.cpp
    RenderAssetsTests.cpp
    RenderScheduleTests.cpp
    RenderTests.cpp
    ResultTests.cpp
    ShaderTests.cpp
    SimdTests.cpp
    SystemsTests.cpp
    TransformTests.cpp)

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
    TextureImportTests.cpp)
crucible_set_compile_options(crucible_importers_tests)
# ufbx's sample files (ADR 0044) are the FBX tests' models.
target_compile_definitions(crucible_importers_tests PRIVATE DOCTEST_CONFIG_NO_EXCEPTIONS_BUT_WITH_ALL_ASSERTS
    CRUCIBLE_UFBX_DATA_DIR="${ufbx_SOURCE_DIR}/data"
    CRUCIBLE_SHADER_LIBRARY_DIR="${CRUCIBLE_SHADER_LIBRARY_DIR}")
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
