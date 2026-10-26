# The launcher (ADR 0011): a thin C++ executable around the core library.
#   crucible_launcher      JIT: hosts .NET and loads the engine's C# assembly
#   crucible_launcher_aot  AOT: the core and the NativeAOT-compiled engine
#                          statically linked into one executable

add_executable(crucible_launcher
    Main.cpp
    JitHost.cpp)

crucible_set_compile_options(crucible_launcher)
target_link_libraries(crucible_launcher PRIVATE crucible::core crucible::nethost)

if(CRUCIBLE_BUILD_AOT)
    add_executable(crucible_launcher_aot
        Main.cpp
        AotHost.cpp)

    crucible_set_compile_options(crucible_launcher_aot)
    target_include_directories(crucible_launcher_aot PRIVATE "${PROJECT_SOURCE_DIR}/src/native/core/include")

    # Static libraries resolve left to right: the engine refers to the core
    # (its LibraryImport calls became direct references) and to the .NET
    # runtime, so the engine comes first and the runtime's libraries last.
    target_link_libraries(crucible_launcher_aot PRIVATE
        "${CRUCIBLE_ENGINE_AOT_LIBRARY}"
        crucible::core_static
        "LINKER:@${CRUCIBLE_ENGINE_AOT_LINK_INPUTS}")
    add_dependencies(crucible_launcher_aot crucible_engine_aot)
    set_property(TARGET crucible_launcher_aot APPEND PROPERTY LINK_DEPENDS
        "${CRUCIBLE_ENGINE_AOT_LIBRARY}" "${CRUCIBLE_ENGINE_AOT_LINK_INPUTS}")
endif()

if(CRUCIBLE_BUILD_TESTS)
    add_test(NAME launcher.jit.start
        COMMAND crucible_launcher --managed-dir "${CRUCIBLE_DOTNET_ARTIFACTS_DIR}/bin/Crucible.Engine/${CRUCIBLE_DOTNET_CONFIGURATION_DIR}")
    set_tests_properties(launcher.jit.start PROPERTIES LABELS integration)

    # Proves a hosting failure is reported: no engine assembly, so it must fail.
    add_test(NAME launcher.jit.missing-engine
        COMMAND crucible_launcher --managed-dir "${CMAKE_CURRENT_BINARY_DIR}/no-engine-here")
    set_tests_properties(launcher.jit.missing-engine PROPERTIES LABELS integration WILL_FAIL TRUE)

    if(CRUCIBLE_BUILD_AOT)
        add_test(NAME launcher.aot.start COMMAND crucible_launcher_aot)
        set_tests_properties(launcher.aot.start PROPERTIES LABELS "integration;aot")
    endif()
endif()
