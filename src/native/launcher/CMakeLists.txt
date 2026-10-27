# The launcher (ADR 0011): a thin C++ executable around the core library.
#   crucible_launcher      JIT: hosts .NET and loads the engine's C# assembly
#   crucible_launcher_aot  AOT: the core and the NativeAOT-compiled engine
#                          statically linked into one executable

add_executable(crucible_launcher
    CpuCheck.cpp
    Main.cpp
    JitHost.cpp)

crucible_set_compile_options(crucible_launcher)

# The CPU check (ADR 0024's amendment) is compiled for the base x64 level, so
# it can run on the processors it turns away; the flag after the global one
# wins.
if(CRUCIBLE_X64)
    if(MSVC)
        set_source_files_properties(CpuCheck.cpp PROPERTIES COMPILE_OPTIONS "/arch:SSE2")
    else()
        set_source_files_properties(CpuCheck.cpp PROPERTIES COMPILE_OPTIONS "-march=x86-64")
    endif()
    string(REPLACE "v" "" crucibleX86Level "${CRUCIBLE_X86_LEVEL}")
    set_source_files_properties(CpuCheck.cpp PROPERTIES COMPILE_DEFINITIONS "CRUCIBLE_X86_LEVEL=${crucibleX86Level}")
endif()
target_link_libraries(crucible_launcher PRIVATE crucible::core crucible::nethost)
crucible_copy_nethost(crucible_launcher)
crucible_use_d3d12_agility(crucible_launcher)
if(WIN32)
    # Windows keeps DLLs next to the executable; the engine looks there for
    # the core (CoreLibraryResolver).
    add_custom_command(TARGET crucible_launcher POST_BUILD
        COMMAND "${CMAKE_COMMAND}" -E copy_if_different
            "$<TARGET_FILE:crucible_core>" "$<TARGET_FILE_DIR:crucible_launcher>"
        VERBATIM)
endif()

if(CRUCIBLE_BUILD_AOT)
    add_executable(crucible_launcher_aot
        CpuCheck.cpp
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
    crucible_use_d3d12_agility(crucible_launcher_aot)
    add_dependencies(crucible_launcher_aot crucible_engine_aot)
    set_property(TARGET crucible_launcher_aot APPEND PROPERTY LINK_DEPENDS
        "${CRUCIBLE_ENGINE_AOT_LIBRARY}" "${CRUCIBLE_ENGINE_AOT_LINK_INPUTS}")
endif()

if(CRUCIBLE_BUILD_TESTS)
    # The launchers create a device, so these need a GPU. The offscreen video
    # driver can't present, so the loop runs headless for a fixed number of
    # frames; a real window is checked by hand.
    set(engineDirectory "${CRUCIBLE_DOTNET_ARTIFACTS_DIR}/bin/Crucible.Engine/${CRUCIBLE_DOTNET_CONFIGURATION_DIR}")
    add_test(NAME launcher.jit.start
        COMMAND crucible_launcher --managed-dir "${engineDirectory}" --headless --frames 60)
    set_tests_properties(launcher.jit.start PROPERTIES LABELS integration ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "ran 60 frames")

    # Windowed under the offscreen driver: creating the surface fails, which
    # throws in C# (ADR 0030) and must come back as a failed run, not a crash.
    add_test(NAME launcher.jit.windowed-without-display
        COMMAND crucible_launcher --managed-dir "${engineDirectory}" --frames 1)
    set_tests_properties(launcher.jit.windowed-without-display PROPERTIES LABELS integration WILL_FAIL TRUE
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen")

    # The latency harness, headless: writes its CSVs and prints a summary.
    add_test(NAME launcher.jit.latency
        COMMAND crucible_launcher --managed-dir "${engineDirectory}" --headless --frames 1000
            --latency "${CMAKE_CURRENT_BINARY_DIR}/latency-test.csv")
    set_tests_properties(launcher.jit.latency PROPERTIES LABELS integration ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "Crucible.Latency: 1000 frames")

    if(CRUCIBLE_X64)
        # The refusal a CPU without the x64 level gets (ADR 0024's amendment).
        add_test(NAME launcher.unsupported-cpu COMMAND crucible_launcher --headless --frames 1)
        set_tests_properties(launcher.unsupported-cpu PROPERTIES LABELS integration
            ENVIRONMENT "CRUCIBLE_TEST_UNSUPPORTED_CPU=1"
            PASS_REGULAR_EXPRESSION "needs a processor with")
    endif()

    add_test(NAME launcher.bad-option COMMAND crucible_launcher --frames many)
    set_tests_properties(launcher.bad-option PROPERTIES LABELS integration WILL_FAIL TRUE)

    # Proves a hosting failure is reported: no engine assembly, so it must fail.
    add_test(NAME launcher.jit.missing-engine
        COMMAND crucible_launcher --managed-dir "${CMAKE_CURRENT_BINARY_DIR}/no-engine-here")
    set_tests_properties(launcher.jit.missing-engine PROPERTIES LABELS integration WILL_FAIL TRUE
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen")

    # A managed folder whose name isn't ASCII: on Windows only wmain receives
    # such a path intact. The setup test copies the engine there first.
    set(unicodeDirectory "${CMAKE_CURRENT_BINARY_DIR}/managed-Zoë-日本")
    add_test(NAME launcher.jit.unicode-path.setup
        COMMAND "${CMAKE_COMMAND}" -E copy_directory
            "${CRUCIBLE_DOTNET_ARTIFACTS_DIR}/bin/Crucible.Engine/${CRUCIBLE_DOTNET_CONFIGURATION_DIR}"
            "${unicodeDirectory}")
    set_tests_properties(launcher.jit.unicode-path.setup PROPERTIES FIXTURES_SETUP unicode-managed-dir)
    add_test(NAME launcher.jit.unicode-path
        COMMAND crucible_launcher --managed-dir "${unicodeDirectory}" --headless --frames 1)
    set_tests_properties(launcher.jit.unicode-path PROPERTIES
        LABELS integration
        FIXTURES_REQUIRED unicode-managed-dir
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen")

    if(CRUCIBLE_BUILD_AOT)
        add_test(NAME launcher.aot.start COMMAND crucible_launcher_aot --headless --frames 60)
        set_tests_properties(launcher.aot.start PROPERTIES LABELS "integration;aot" ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
            PASS_REGULAR_EXPRESSION "ran 60 frames")
    endif()
endif()
