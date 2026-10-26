# The launcher (ADR 0011): a thin C++ executable around the core library that
# hosts .NET and calls the engine's managed entry point.

add_executable(crucible_launcher
    Main.cpp
    DotnetHost.cpp)

crucible_set_compile_options(crucible_launcher)
target_link_libraries(crucible_launcher PRIVATE crucible::core crucible::nethost)

if(CRUCIBLE_BUILD_TESTS)
    add_test(NAME launcher.jit.start
        COMMAND crucible_launcher --managed-dir "${CRUCIBLE_DOTNET_ARTIFACTS_DIR}/bin/Crucible.Engine/${CRUCIBLE_DOTNET_CONFIGURATION_DIR}")
    set_tests_properties(launcher.jit.start PROPERTIES LABELS integration)

    # Proves a hosting failure is reported: no engine assembly, so it must fail.
    add_test(NAME launcher.jit.missing-engine
        COMMAND crucible_launcher --managed-dir "${CMAKE_CURRENT_BINARY_DIR}/no-engine-here")
    set_tests_properties(launcher.jit.missing-engine PROPERTIES LABELS integration WILL_FAIL TRUE)
endif()
