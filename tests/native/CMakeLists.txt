add_executable(crucible_native_tests
    Main.cpp
    CoreTests.cpp
    EcsTests.cpp
    InputTimingTests.cpp
    JobTests.cpp
    MathTests.cpp
    MemoryTests.cpp
    PlatformTests.cpp
    RenderTests.cpp
    ResultTests.cpp
    SimdTests.cpp
    SystemsTests.cpp)

crucible_set_compile_options(crucible_native_tests)
crucible_use_d3d12_agility(crucible_native_tests)
# Tests build without exceptions too (ADR 0030); doctest's REQUIRE then ends
# the run on failure instead of throwing.
target_compile_definitions(crucible_native_tests PRIVATE DOCTEST_CONFIG_NO_EXCEPTIONS_BUT_WITH_ALL_ASSERTS)
# SDL directly too: the platform tests push synthetic SDL events.
target_link_libraries(crucible_native_tests PRIVATE crucible::core_static SDL3::SDL3-static doctest::doctest)

# SDL's offscreen driver makes real windows that are never shown, so the tests
# need no display (Windows over SSH has none).
# ADD_LABELS turns test suites into labels: "gpu" tests need a Slag device.
# Test names must not contain commas: doctest's label lookup passes the name
# to --test-case unescaped, where a comma separates filters, so the test
# silently loses its labels.
doctest_discover_tests(crucible_native_tests ADD_LABELS 1
    PROPERTIES LABELS native ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen")

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
