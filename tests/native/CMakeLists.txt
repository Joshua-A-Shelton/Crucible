add_executable(crucible_native_tests
    Main.cpp
    CoreTests.cpp
    PlatformTests.cpp
    ResultTests.cpp)

crucible_set_compile_options(crucible_native_tests)
# Tests build without exceptions too (ADR 0030); doctest's REQUIRE then ends
# the run on failure instead of throwing.
target_compile_definitions(crucible_native_tests PRIVATE DOCTEST_CONFIG_NO_EXCEPTIONS_BUT_WITH_ALL_ASSERTS)
# SDL directly too: the platform tests push synthetic SDL events.
target_link_libraries(crucible_native_tests PRIVATE crucible::core_static SDL3::SDL3-static doctest::doctest)

# SDL's offscreen driver makes real windows that are never shown, so the tests
# need no display (Windows over SSH has none).
doctest_discover_tests(crucible_native_tests PROPERTIES LABELS native ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen")
