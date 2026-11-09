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

# The editor launcher (ADR 0016, 0066): the JIT launcher loading the editor's
# assembly as its game. The editor is JIT only, like games in development.
add_executable(crucible_editor
    CpuCheck.cpp
    Main.cpp
    JitHost.cpp)
crucible_set_compile_options(crucible_editor)
target_compile_definitions(crucible_editor PRIVATE CRUCIBLE_EDITOR_LAUNCHER=1)
target_link_libraries(crucible_editor PRIVATE crucible::core crucible::nethost)
crucible_copy_nethost(crucible_editor)
crucible_use_d3d12_agility(crucible_editor)
if(WIN32)
    add_custom_command(TARGET crucible_editor POST_BUILD
        COMMAND "${CMAKE_COMMAND}" -E copy_if_different
            "$<TARGET_FILE:crucible_core>" "$<TARGET_FILE_DIR:crucible_editor>"
        VERBATIM)
endif()
# The asset pipeline's native library beside the editor, where the editor
# looks for it (Crucible.Editor's Dependencies.cs), as a shipped editor keeps it.
add_custom_command(TARGET crucible_editor POST_BUILD
    COMMAND "${CMAKE_COMMAND}" -E copy_if_different
        "$<TARGET_FILE:crucible_importers>" "$<TARGET_FILE_DIR:crucible_editor>"
    VERBATIM)
add_dependencies(crucible_editor crucible_importers)

# What the editor needs to build a project's native module (ADR 0074),
# beside the launchers: the tools this build used, and the shared core's
# headers, definitions, and import library. The module is compiled like the
# core (C++20, no exceptions or RTTI) with the same compiler.
if(MSVC)
    set(crucibleModuleOptions "\"/permissive-\", \"/EHs-c-\", \"/GR-\", \"/Zc:__cplusplus\"")
    set(crucibleModuleDefinitions "\"_HAS_EXCEPTIONS=0\"")
    # cl.exe at .../VC/Tools/MSVC/<version>/bin/Hostx64/x64: the environment
    # it needs comes from .../VC/Auxiliary/Build/vcvars64.bat.
    string(REGEX REPLACE "/VC/Tools/.*$" "/VC/Auxiliary/Build/vcvars64.bat" crucibleModuleEnvironment
        "${CMAKE_CXX_COMPILER}")
else()
    set(crucibleModuleOptions "\"-fno-exceptions\", \"-fno-rtti\"")
    set(crucibleModuleDefinitions "")
    set(crucibleModuleEnvironment "")
endif()
set(crucibleModuleIncludes "$<TARGET_PROPERTY:crucible_core,INTERFACE_INCLUDE_DIRECTORIES>")
set(crucibleModuleCoreDefinitions "$<TARGET_PROPERTY:crucible_core,INTERFACE_COMPILE_DEFINITIONS>")
file(GENERATE OUTPUT "$<TARGET_FILE_DIR:crucible_launcher>/CrucibleModule.json" CONTENT "{
  \"Cmake\": \"${CMAKE_COMMAND}\",
  \"Ninja\": \"${CMAKE_MAKE_PROGRAM}\",
  \"CxxCompiler\": \"${CMAKE_CXX_COMPILER}\",
  \"Environment\": \"${crucibleModuleEnvironment}\",
  \"BuildType\": \"$<CONFIG>\",
  \"IncludeDirectories\": [\"$<JOIN:$<REMOVE_DUPLICATES:${crucibleModuleIncludes}>,\"$<COMMA> \">\"],
  \"CompileDefinitions\": [${crucibleModuleDefinitions}$<$<AND:$<BOOL:${crucibleModuleDefinitions}>,$<BOOL:${crucibleModuleCoreDefinitions}>>:$<COMMA> >$<$<BOOL:${crucibleModuleCoreDefinitions}>:\"$<JOIN:${crucibleModuleCoreDefinitions},\"$<COMMA> \">\">],
  \"CompileOptions\": [${crucibleModuleOptions}],
  \"CoreLibrary\": \"$<TARGET_LINKER_FILE:crucible_core>\",
  \"CoreHeaders\": \"${PROJECT_SOURCE_DIR}/src/native/core/include/crucible\",
  \"Dotnet\": \"${DOTNET_EXECUTABLE}\",
  \"Bindgen\": \"${CRUCIBLE_BINDGEN_DLL}\",
  \"Libclang\": \"${CRUCIBLE_LIBCLANG}\",
  \"Clang\": \"${CRUCIBLE_CLANG}\"
}
")

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

    # The M3 demonstration (ADR 0037): the sample game loaded with --game, its
    # C# and C++ systems run for 60 fixed steps, and its report printed.
    set(particlesGame
        "${CRUCIBLE_DOTNET_ARTIFACTS_DIR}/bin/Crucible.Samples.Particles/${CRUCIBLE_DOTNET_CONFIGURATION_DIR}/Crucible.Samples.Particles.dll")
    add_test(NAME launcher.jit.particles
        COMMAND crucible_launcher --managed-dir "${engineDirectory}" --headless --game "${particlesGame}"
            --game-arg particles=5000 --game-arg steps=60)
    set_tests_properties(launcher.jit.particles PROPERTIES LABELS integration ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "Crucible.Particles: 5000 particles, 60 fixed steps")

    # The M5 demonstration (ADR 0056): the Diorama imported and rendered on
    # Vulkan, and on Windows on D3D12 too, the two images compared. The asset
    # pipeline's native library sits beside the core.
    set(dioramaGame
        "${CRUCIBLE_DOTNET_ARTIFACTS_DIR}/bin/Crucible.Samples.Diorama/${CRUCIBLE_DOTNET_CONFIGURATION_DIR}/Crucible.Samples.Diorama.dll")
    if(WIN32)
        set(_libraryPath PATH)
    else()
        set(_libraryPath LD_LIBRARY_PATH)
    endif()
    add_test(NAME samples.diorama
        COMMAND "${CMAKE_COMMAND}" "-DLAUNCHER=$<TARGET_FILE:crucible_launcher>" "-DENGINE=${engineDirectory}"
            "-DGAME=${dioramaGame}" "-DPROJECT=${PROJECT_SOURCE_DIR}/samples/Diorama/Project/Diorama.crproject"
            "-DOUT=${CMAKE_CURRENT_BINARY_DIR}/diorama" -P "${PROJECT_SOURCE_DIR}/samples/Diorama/run_diorama.cmake")
    set_tests_properties(samples.diorama PROPERTIES LABELS "integration;gpu"
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        ENVIRONMENT_MODIFICATION "${_libraryPath}=path_list_prepend:$<TARGET_FILE_DIR:crucible_core>")

    # The batched command API from C# (ADR 0058): a game whose C# render step
    # draws through command records, checked by reading the frame back. It
    # draws with the native tests' probe shader.
    set(renderStepGame
        "${CRUCIBLE_DOTNET_ARTIFACTS_DIR}/bin/Crucible.RenderStepGame/${CRUCIBLE_DOTNET_CONFIGURATION_DIR}/Crucible.RenderStepGame.dll")
    add_test(NAME launcher.jit.render-steps
        COMMAND crucible_launcher --managed-dir "${engineDirectory}" --headless --frames 100 --game "${renderStepGame}"
            --game-arg "shader=${PROJECT_BINARY_DIR}/tests/native/shaders/StepProbe.crshader")
    set_tests_properties(launcher.jit.render-steps PROPERTIES LABELS "integration;gpu"
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "RenderStepGame: all [0-9]+ checks passed")

    # The UI's Canvas (ADR 0059): every primitive drawn by the same game as
    # the UI step, checked at chosen pixels; the frame is saved as canvas.png.
    add_test(NAME launcher.jit.canvas
        COMMAND crucible_launcher --managed-dir "${engineDirectory}" --headless --frames 100 --game "${renderStepGame}"
            --game-arg test=canvas
            --game-arg "font=${PROJECT_SOURCE_DIR}/tests/native/fonts/NotoSans-Regular.ttf"
            --game-arg "capture=${CMAKE_CURRENT_BINARY_DIR}/canvas.png")
    set_tests_properties(launcher.jit.canvas PROPERTIES LABELS "integration;gpu"
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "CanvasChecks: all [0-9]+ checks passed")

    # The UI's element tree (ADR 0060): every panel kind at a DPI scale of
    # 1.25, checked at chosen pixels; the frame is saved as layout.png.
    add_test(NAME launcher.jit.layout
        COMMAND crucible_launcher --managed-dir "${engineDirectory}" --headless --frames 100 --game "${renderStepGame}"
            --game-arg test=layout --game-arg "capture=${CMAKE_CURRENT_BINARY_DIR}/layout.png")
    set_tests_properties(launcher.jit.layout PROPERTIES LABELS "integration;gpu"
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "LayoutChecks: all [0-9]+ checks passed")

    # Styles and animation (ADR 0061): a themed tree, and a hover transition
    # waited out in real time; the frame is saved as styles.png. Frames
    # aren't capped here: the transition needs wall-clock time.
    add_test(NAME launcher.jit.styles
        COMMAND crucible_launcher --managed-dir "${engineDirectory}" --headless --frames 100000 --game "${renderStepGame}"
            --game-arg test=styles --game-arg "capture=${CMAKE_CURRENT_BINARY_DIR}/styles.png")
    set_tests_properties(launcher.jit.styles PROPERTIES LABELS "integration;gpu" TIMEOUT 60
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "StyleChecks: all [0-9]+ checks passed")

    # The controls (ADR 0063) with the default theme, driven by synthetic
    # input, checked at pixels once their transitions have run (real time);
    # the frame is saved as controls.png.
    add_test(NAME launcher.jit.controls
        COMMAND crucible_launcher --managed-dir "${engineDirectory}" --headless --frames 100000 --game "${renderStepGame}"
            --game-arg test=controls
            --game-arg "font=${PROJECT_SOURCE_DIR}/tests/native/fonts/NotoSans-Regular.ttf"
            --game-arg "capture=${CMAKE_CURRENT_BINARY_DIR}/controls.png")
    set_tests_properties(launcher.jit.controls PROPERTIES LABELS "integration;gpu" TIMEOUT 60
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "ControlsChecks: all [0-9]+ checks passed")
    add_test(NAME launcher.jit.textfields
        COMMAND crucible_launcher --managed-dir "${engineDirectory}" --headless --frames 100000 --game "${renderStepGame}"
            --game-arg test=textfields
            --game-arg "font=${PROJECT_SOURCE_DIR}/tests/native/fonts/NotoSans-Regular.ttf"
            --game-arg "capture=${CMAKE_CURRENT_BINARY_DIR}/textfields.png")
    set_tests_properties(launcher.jit.textfields PROPERTIES LABELS "integration;gpu" TIMEOUT 60
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "TextFieldChecks: all [0-9]+ checks passed")

    # The editor checking itself (ADR 0066): menus, tab drags with the drop
    # preview, a panel torn off into a floating window and docked back,
    # checked at pixels; its settings kept in the build tree.
    add_test(NAME launcher.jit.editor
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 600
            --game-arg "check=${CMAKE_CURRENT_BINARY_DIR}/editor.png"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/editor-settings.json")
    set_tests_properties(launcher.jit.editor PROPERTIES LABELS "integration;gpu" TIMEOUT 60
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "EditorChecks: all [0-9]+ checks passed")

    # The viewport checking itself (ADR 0067): the Diorama's village opened in
    # the editor, picked, outlined, and moved by its handles, checked at
    # pixels; the window saved as viewport.png. The project's cache stays in
    # the source tree's ignored Cache/, as when the editor opens it by hand.
    add_test(NAME launcher.jit.viewport
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 600
            --project "${PROJECT_SOURCE_DIR}/samples/Diorama/Project/Diorama.crproject"
            --scene "${PROJECT_SOURCE_DIR}/samples/Diorama/Project/Assets/Scenes/Village.crscene"
            --game-arg "check-viewport=${CMAKE_CURRENT_BINARY_DIR}/viewport.png"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/viewport-settings.json")
    set_tests_properties(launcher.jit.viewport PROPERTIES LABELS "integration;gpu" TIMEOUT 120
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "ViewportChecks: all [0-9]+ checks passed")

    # The hierarchy, undo, and saving checking themselves (ADR 0068): the
    # village listed, renamed, edited, undone, and saved beside the capture
    # (never over the village); the window saved as hierarchy.png mid-drag.
    add_test(NAME launcher.jit.hierarchy
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 600
            --project "${PROJECT_SOURCE_DIR}/samples/Diorama/Project/Diorama.crproject"
            --scene "${PROJECT_SOURCE_DIR}/samples/Diorama/Project/Assets/Scenes/Village.crscene"
            --game-arg "check-hierarchy=${CMAKE_CURRENT_BINARY_DIR}/hierarchy.png"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/hierarchy-settings.json")
    set_tests_properties(launcher.jit.hierarchy PROPERTIES LABELS "integration;gpu" TIMEOUT 120
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "HierarchyChecks: all [0-9]+ checks passed")

    # The inspector checking itself (ADR 0069): the village's house edited
    # by typing and scrubbing, a part overridden and reverted, components
    # added and removed; the window saved as inspector.png.
    add_test(NAME launcher.jit.inspector
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 600
            --project "${PROJECT_SOURCE_DIR}/samples/Diorama/Project/Diorama.crproject"
            --scene "${PROJECT_SOURCE_DIR}/samples/Diorama/Project/Assets/Scenes/Village.crscene"
            --game-arg "check-inspector=${CMAKE_CURRENT_BINARY_DIR}/inspector.png"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/inspector-settings.json")
    set_tests_properties(launcher.jit.inspector PROPERTIES LABELS "integration;gpu" TIMEOUT 120
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "InspectorChecks: all [0-9]+ checks passed")

    # The asset browser checking itself (ADR 0071): a copy of the Diorama's
    # project (beside the capture, never the sources) browsed, a file added
    # and imported, models dragged into the viewport and the hierarchy; the
    # window saved as assets.png mid-drag.
    add_test(NAME launcher.jit.assets
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 100000
            --project "${PROJECT_SOURCE_DIR}/samples/Diorama/Project/Diorama.crproject"
            --game-arg "check-assets=${CMAKE_CURRENT_BINARY_DIR}/assets.png"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/assets-settings.json")
    set_tests_properties(launcher.jit.assets PROPERTIES LABELS "integration;gpu" TIMEOUT 120
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "AssetChecks: all [0-9]+ checks passed")

    # Play mode and the project's code checking themselves (ADR 0072): a copy
    # of the Diorama's project (beside the capture) built, played, stopped,
    # and hot-reloaded; the window saved as play.png while playing.
    add_test(NAME launcher.jit.play
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 100000
            --project "${PROJECT_SOURCE_DIR}/samples/Diorama/Project/Diorama.crproject"
            --game-arg "check-play=${CMAKE_CURRENT_BINARY_DIR}/play.png"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/play-settings.json")
    set_tests_properties(launcher.jit.play PROPERTIES LABELS "integration;gpu" TIMEOUT 300
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "PlayChecks: all [0-9]+ checks passed")

    # Run Standalone checking itself (ADR 0073): a copy of the Diorama's
    # project (in the test's folder) run in the game launcher, headless.
    add_test(NAME launcher.jit.standalone
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 100000
            --project "${PROJECT_SOURCE_DIR}/samples/Diorama/Project/Diorama.crproject"
            --game-arg "check-standalone=${CMAKE_CURRENT_BINARY_DIR}"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/standalone-settings.json")
    set_tests_properties(launcher.jit.standalone PROPERTIES LABELS "integration;gpu" TIMEOUT 300
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "StandaloneChecks: all [0-9]+ checks passed")

    # Native modules checking themselves (ADR 0074): a copy of the Diorama's
    # project (in the test's folder) with its module built, loaded, played,
    # run standalone, and broken.
    add_test(NAME launcher.jit.native
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 100000
            --project "${PROJECT_SOURCE_DIR}/samples/Diorama/Project/Diorama.crproject"
            --game-arg "check-native=${CMAKE_CURRENT_BINARY_DIR}"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/native-settings.json")
    set_tests_properties(launcher.jit.native PROPERTIES LABELS "integration;gpu" TIMEOUT 600
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "NativeChecks: all [0-9]+ checks passed")

    # The editor's checks open the Diorama's project before copying it, and
    # opening builds its native module and code into its Cache (ADR 0072,
    # 0074): one at a time, or the builds race.
    set_tests_properties(launcher.jit.viewport launcher.jit.hierarchy launcher.jit.inspector launcher.jit.assets
        launcher.jit.play launcher.jit.standalone launcher.jit.native PROPERTIES RESOURCE_LOCK diorama-project-cache)

    # The M6 demonstration (ADR 0064): the UI showcase's menu driven by mouse,
    # keyboard, and gamepad scripts, and captured at four DPI scales; on
    # Windows, on D3D12 too, the captures compared.
    set(uiShowcaseGame
        "${CRUCIBLE_DOTNET_ARTIFACTS_DIR}/bin/Crucible.Samples.UiShowcase/${CRUCIBLE_DOTNET_CONFIGURATION_DIR}/Crucible.Samples.UiShowcase.dll")
    add_test(NAME samples.ui
        COMMAND "${CMAKE_COMMAND}" "-DLAUNCHER=$<TARGET_FILE:crucible_launcher>" "-DENGINE=${engineDirectory}"
            "-DGAME=${uiShowcaseGame}" "-DPROJECT=${PROJECT_SOURCE_DIR}/samples/UiShowcase/Project/UiShowcase.crproject"
            "-DOUT=${CMAKE_CURRENT_BINARY_DIR}/ui" -P "${PROJECT_SOURCE_DIR}/samples/UiShowcase/run_ui.cmake")
    set_tests_properties(samples.ui PROPERTIES LABELS "integration;gpu" TIMEOUT 180
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        ENVIRONMENT_MODIFICATION "${_libraryPath}=path_list_prepend:$<TARGET_FILE_DIR:crucible_core>")

    # A game option needs a key and a value.
    add_test(NAME launcher.bad-game-arg COMMAND crucible_launcher --game-arg particles)
    set_tests_properties(launcher.bad-game-arg PROPERTIES LABELS integration WILL_FAIL TRUE)

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

        # A NativeAOT engine compiles its game in, so it refuses --game.
        add_test(NAME launcher.aot.game-refused
            COMMAND crucible_launcher_aot --headless --frames 1 --game "${particlesGame}")
        set_tests_properties(launcher.aot.game-refused PROPERTIES LABELS "integration;aot"
            ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen" PASS_REGULAR_EXPRESSION "--game needs the JIT engine")
    endif()
endif()
