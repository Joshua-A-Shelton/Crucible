# The launcher (ADR 0011): a thin C++ executable around the core library.
#   crucible_launcher      JIT (CrucibleLauncher): hosts .NET and loads the
#                          engine's C# assembly
#   crucible_launcher_aot  AOT (CrucibleLauncherAot): the core and the NativeAOT-compiled engine
#                          statically linked into one executable

add_executable(crucible_launcher
    CpuCheck.cpp
    Main.cpp
    JitHost.cpp)

crucible_set_compile_options(crucible_launcher)
# A package's copy, renamed after its game, finds the core beside it (ADR 0100).
set_target_properties(crucible_launcher PROPERTIES OUTPUT_NAME CrucibleLauncher BUILD_RPATH "\$ORIGIN")

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
crucible_d3d12_agility_exports(crucible_launcher)

# The editor launcher (ADR 0016, 0066): the JIT launcher loading the editor's
# assembly as its game. The editor is JIT only, like games in development.
add_executable(crucible_editor
    CpuCheck.cpp
    Main.cpp
    JitHost.cpp)
crucible_set_compile_options(crucible_editor)
set_target_properties(crucible_editor PROPERTIES OUTPUT_NAME CrucibleEditor)
target_compile_definitions(crucible_editor PRIVATE CRUCIBLE_EDITOR_LAUNCHER=1)
target_link_libraries(crucible_editor PRIVATE crucible::core crucible::nethost)
crucible_d3d12_agility_exports(crucible_editor)
# On Windows, what the launchers in this folder load from beside them, copied
# once for all of them: the core (where CoreLibraryResolver looks), .NET's
# nethost, and the Agility SDK's D3D12 runtime. Each executable copying them
# after linking raced when two linked at once (Permission denied).
if(WIN32)
    set(crucibleLauncherFiles
        COMMAND "${CMAKE_COMMAND}" -E copy_if_different "$<TARGET_FILE:crucible_core>" "${CMAKE_CURRENT_BINARY_DIR}"
        COMMAND "${CMAKE_COMMAND}" -E copy_if_different "${CRUCIBLE_NETHOST_DIR}/nethost.dll" "${CMAKE_CURRENT_BINARY_DIR}")
    if(CRUCIBLE_AGILITY_SDK_VERSION)
        list(APPEND crucibleLauncherFiles
            COMMAND "${CMAKE_COMMAND}" -E make_directory "${CMAKE_CURRENT_BINARY_DIR}/D3D12"
            COMMAND "${CMAKE_COMMAND}" -E copy_if_different "${SLAG_AGILITY_SDK_BIN_DIR}/D3D12Core.dll"
                "${SLAG_AGILITY_SDK_BIN_DIR}/d3d12SDKLayers.dll" "${CMAKE_CURRENT_BINARY_DIR}/D3D12/")
    endif()
    add_custom_target(crucible_launcher_files ${crucibleLauncherFiles} VERBATIM)
    add_dependencies(crucible_launcher_files crucible_core)
    add_dependencies(crucible_launcher crucible_launcher_files)
    add_dependencies(crucible_editor crucible_launcher_files)
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
if(CRUCIBLE_SHIPPING)
    # A Shipping build's modules compile information messages out too (ADR 0103).
    if(crucibleModuleDefinitions)
        string(APPEND crucibleModuleDefinitions ", ")
    endif()
    string(APPEND crucibleModuleDefinitions "\"CRUCIBLE_SHIPPING\"")
endif()
# Whether there are any, as one word: the list has commas, which a
# generator expression would read as arguments.
if(crucibleModuleDefinitions)
    set(crucibleHasModuleDefinitions 1)
else()
    set(crucibleHasModuleDefinitions 0)
endif()
# Where the shipping preset's build is (ADR 0103): build/shipping beside this
# build's folder unless set; packages made Shipping come from it.
set(CRUCIBLE_SHIPPING_DIR "${CMAKE_BINARY_DIR}/../shipping" CACHE PATH "The shipping preset's build folder, for Shipping packages")
cmake_path(ABSOLUTE_PATH CRUCIBLE_SHIPPING_DIR NORMALIZE OUTPUT_VARIABLE crucibleShippingDir)
set(crucibleModuleIncludes "$<TARGET_PROPERTY:crucible_core,INTERFACE_INCLUDE_DIRECTORIES>")
# And what it needs to link a game's NativeAOT executable (ADR 0101): the
# AOT launcher's objects, the core's static libraries and the system's, in
# link order, and the instruction set the NativeAOT compiler targets.
set(crucibleAotInstructionSet "")
if(CRUCIBLE_X64)
    set(crucibleAotInstructionSet "x86-64-${CRUCIBLE_X86_LEVEL}")
endif()
set(crucibleModuleCoreDefinitions "$<TARGET_PROPERTY:crucible_core,INTERFACE_COMPILE_DEFINITIONS>")
file(GENERATE OUTPUT "$<TARGET_FILE_DIR:crucible_launcher>/CrucibleModule.json" CONTENT "{
  \"Cmake\": \"${CMAKE_COMMAND}\",
  \"Ninja\": \"${CMAKE_MAKE_PROGRAM}\",
  \"CxxCompiler\": \"${CMAKE_CXX_COMPILER}\",
  \"Environment\": \"${crucibleModuleEnvironment}\",
  \"BuildType\": \"$<CONFIG>\",
  \"IncludeDirectories\": [\"$<JOIN:$<REMOVE_DUPLICATES:${crucibleModuleIncludes}>,\"$<COMMA> \">\"],
  \"CompileDefinitions\": [${crucibleModuleDefinitions}$<$<AND:$<BOOL:${crucibleHasModuleDefinitions}>,$<BOOL:${crucibleModuleCoreDefinitions}>>:$<COMMA> >$<$<BOOL:${crucibleModuleCoreDefinitions}>:\"$<JOIN:${crucibleModuleCoreDefinitions},\"$<COMMA> \">\">],
  \"CompileOptions\": [${crucibleModuleOptions}],
  \"CoreLibrary\": \"$<TARGET_LINKER_FILE:crucible_core>\",
  \"CoreHeaders\": \"${PROJECT_SOURCE_DIR}/src/native/core/include/crucible\",
  \"Dotnet\": \"${DOTNET_EXECUTABLE}\",
  \"Bindgen\": \"${CRUCIBLE_BINDGEN_DLL}\",
  \"Libclang\": \"${CRUCIBLE_LIBCLANG}\",
  \"Clang\": \"${CRUCIBLE_CLANG}\",
  \"AotObjects\": [\"$<JOIN:$<TARGET_OBJECTS:crucible_launcher_aot_objects>,\"$<COMMA> \">\"],
  \"AotLibraries\": [\"$<JOIN:${CRUCIBLE_AOT_NATIVE_LIBRARIES},\"$<COMMA> \">\"],
  \"AotSystemLibraries\": [\"$<JOIN:${CRUCIBLE_AOT_SYSTEM_LIBRARIES},\"$<COMMA> \">\"],
  \"AotInstructionSet\": \"${crucibleAotInstructionSet}\",
  \"Launcher\": \"$<TARGET_FILE:crucible_launcher>\",
  \"CoreRuntime\": \"$<TARGET_FILE:crucible_core>\",
  \"EngineDirectory\": \"${CRUCIBLE_DOTNET_ARTIFACTS_DIR}/bin/Crucible.Engine/${CRUCIBLE_DOTNET_CONFIGURATION_DIR}\",
  \"Shipping\": $<IF:$<BOOL:${CRUCIBLE_SHIPPING}>,true,false>,
  \"ShippingToolkit\": \"${crucibleShippingDir}/src/native/launcher/CrucibleModule.json\"
}
")

# The AOT launcher's code as object files, for the AOT launcher and for the
# editor to link into a game's NativeAOT executable (ADR 0101), with the
# Agility SDK's exports, which must be in the executable itself.
add_library(crucible_launcher_aot_objects OBJECT
    CpuCheck.cpp
    Main.cpp
    AotHost.cpp)
crucible_set_compile_options(crucible_launcher_aot_objects)
target_link_libraries(crucible_launcher_aot_objects PRIVATE crucible::core_static)
if(WIN32 AND CRUCIBLE_AGILITY_SDK_VERSION)
    target_sources(crucible_launcher_aot_objects PRIVATE D3D12Agility.cpp)
    target_compile_definitions(crucible_launcher_aot_objects PRIVATE
        CRUCIBLE_AGILITY_SDK_VERSION=${CRUCIBLE_AGILITY_SDK_VERSION})
endif()
# Built with the editor, which links them into AOT packages.
add_dependencies(crucible_editor crucible_launcher_aot_objects crucible_core_static)

if(CRUCIBLE_BUILD_AOT)
    add_executable(crucible_launcher_aot $<TARGET_OBJECTS:crucible_launcher_aot_objects>)
    set_target_properties(crucible_launcher_aot PROPERTIES OUTPUT_NAME CrucibleLauncherAot)

    # Static libraries resolve left to right: the engine refers to the core
    # (its LibraryImport calls became direct references) and to the .NET
    # runtime, so the engine comes first and the runtime's libraries last.
    target_link_libraries(crucible_launcher_aot PRIVATE
        "${CRUCIBLE_ENGINE_AOT_LIBRARY}"
        crucible::core_static
        "LINKER:@${CRUCIBLE_ENGINE_AOT_LINK_INPUTS}")
    if(WIN32)
        add_dependencies(crucible_launcher_aot crucible_launcher_files)
    endif()
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

    # Crucible's mixer (ADR 0082): the Metronome sample headless, mixing
    # offline, its clicks scheduled from frames and checked to the frame.
    set(metronomeGame
        "${CRUCIBLE_DOTNET_ARTIFACTS_DIR}/bin/Crucible.Samples.Metronome/${CRUCIBLE_DOTNET_CONFIGURATION_DIR}/Crucible.Samples.Metronome.dll")
    add_test(NAME samples.metronome
        COMMAND crucible_launcher --managed-dir "${engineDirectory}" --headless --game "${metronomeGame}"
            --game-arg check=1 --game-arg seconds=3)
    set_tests_properties(samples.metronome PROPERTIES LABELS integration ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "Crucible.Metronome: [56] clicks, at most 0 frames from their times, 0 late")

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

    # The samples warmed up (ADR 0110): each copied without its Cache, opened
    # cold (everything imported, its code built) and again (nothing imported,
    # the build skipped as up to date). The checks that aren't about the cold
    # open copy the warm copy, Cache and all, and skip both.
    set(warmProject "${PROJECT_SOURCE_DIR}/tests/warm/warm_project.cmake")
    set(walkerWarm "${CMAKE_CURRENT_BINARY_DIR}/warm/Walker")
    set(dioramaWarm "${CMAKE_CURRENT_BINARY_DIR}/warm/Diorama")
    function(crucible_add_warm_test name project game out fixture)
        add_test(NAME ${name}
            COMMAND "${CMAKE_COMMAND}" "-DEDITOR=$<TARGET_FILE:crucible_editor>" "-DENGINE=${engineDirectory}"
                "-DPROJECT=${project}" -DGAME=${game} "-DOUT=${out}" -P "${warmProject}")
        # Started first (CTest runs the costliest first): the tests copying it wait on it.
        set_tests_properties(${name} PROPERTIES LABELS "integration;gpu" TIMEOUT 600 FIXTURES_SETUP ${fixture} COST 10000
            ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
            PASS_REGULAR_EXPRESSION "warm_project: ${game} warm in")
    endfunction()
    crucible_add_warm_test(samples.walker-warm "${PROJECT_SOURCE_DIR}/samples/Walker/Project/Walker.crproject"
        Walker "${walkerWarm}" walker-warm)
    crucible_add_warm_test(samples.diorama-warm "${PROJECT_SOURCE_DIR}/samples/Diorama/Project/Diorama.crproject"
        Diorama "${dioramaWarm}" diorama-warm)

    # The viewport checking itself (ADR 0067): the Diorama's village opened in
    # the editor, picked, outlined, and moved by its handles, checked at
    # pixels; the window saved as viewport.png. The project's cache stays in
    # the source tree's ignored Cache/, as when the editor opens it by hand.
    add_test(NAME launcher.jit.viewport
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 100000
            --game-arg "warm-project=${dioramaWarm}/Diorama.crproject"
            --game-arg "check-viewport=${CMAKE_CURRENT_BINARY_DIR}/viewport.png"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/viewport-settings.json")
    set_tests_properties(launcher.jit.viewport PROPERTIES LABELS "integration;gpu" TIMEOUT 120
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "ViewportChecks: all [0-9]+ checks passed")

    # The hierarchy, undo, and saving checking themselves (ADR 0068): the
    # village listed, renamed, edited, undone, and saved beside the capture
    # (never over the village); the window saved as hierarchy.png mid-drag.
    add_test(NAME launcher.jit.hierarchy
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 100000
            --game-arg "warm-project=${dioramaWarm}/Diorama.crproject"
            --game-arg "check-hierarchy=${CMAKE_CURRENT_BINARY_DIR}/hierarchy.png"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/hierarchy-settings.json")
    set_tests_properties(launcher.jit.hierarchy PROPERTIES LABELS "integration;gpu" TIMEOUT 120
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "HierarchyChecks: all [0-9]+ checks passed")

    # The inspector checking itself (ADR 0069): the village's house edited
    # by typing and scrubbing, a part overridden and reverted, components
    # added and removed; the window saved as inspector.png.
    add_test(NAME launcher.jit.inspector
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 100000
            --game-arg "warm-project=${dioramaWarm}/Diorama.crproject"
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
            --game-arg "source-project=${PROJECT_SOURCE_DIR}/samples/Diorama/Project/Diorama.crproject"
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
            --game-arg "warm-project=${dioramaWarm}/Diorama.crproject"
            --game-arg "check-play=${CMAKE_CURRENT_BINARY_DIR}/play.png"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/play-settings.json")
    set_tests_properties(launcher.jit.play PROPERTIES LABELS "integration;gpu" TIMEOUT 300
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "PlayChecks: all [0-9]+ checks passed")

    # Run Standalone checking itself (ADR 0073): a copy of the Diorama's
    # project (in the test's folder) run in the game launcher, headless.
    add_test(NAME launcher.jit.standalone
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 100000
            --game-arg "warm-project=${dioramaWarm}/Diorama.crproject"
            --game-arg "check-standalone=${CMAKE_CURRENT_BINARY_DIR}"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/standalone-settings.json")
    set_tests_properties(launcher.jit.standalone PROPERTIES LABELS "integration;gpu" TIMEOUT 300
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "StandaloneChecks: all [0-9]+ checks passed")

    # The M8 demonstration (ADR 0084): a copy of the Walker's project run
    # standalone, headless, at 30 and 144 fps, its footsteps checked against
    # their times in the offline mix, and the two runs compared.
    add_test(NAME samples.walker
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 100000
            --game-arg "warm-project=${walkerWarm}/Walker.crproject"
            --game-arg "check-walker=${CMAKE_CURRENT_BINARY_DIR}"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/walker-settings.json")
    set_tests_properties(samples.walker PROPERTIES LABELS "integration;gpu" TIMEOUT 300
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "WalkerChecks: all [0-9]+ checks passed")

    # The three Walker checks below cap the editor's own frames at 60 a
    # second: it only waits while the game runs, and uncapped it shared the
    # GPU with that run, slowing its frames (ADR 0111).
    # The M9 demonstration (ADR 0097): a copy of the Walker's project, its
    # Level scene run standalone, headless, at 30 and 240 fps: the swing's
    # hit inside its window, the route walked, and the two runs' hits
    # compared.
    add_test(NAME samples.walker-level
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 1000000 --frame-rate 60
            --game-arg "warm-project=${walkerWarm}/Walker.crproject"
            --game-arg "check-level=${CMAKE_CURRENT_BINARY_DIR}"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/level-settings.json")
    set_tests_properties(samples.walker-level PROPERTIES LABELS "integration;gpu" TIMEOUT 400
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "LevelChecks: all [0-9]+ checks passed")

    # Running from an archive (ADR 0099): a copy of the Walker's project, its
    # Level scene run standalone from the Cache, then cooked and run from
    # Game.crpak with the Cache's imported files moved aside; the two runs
    # must play out the same.
    add_test(NAME samples.walker-archive
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 1000000 --frame-rate 60
            --game-arg "warm-project=${walkerWarm}/Walker.crproject"
            --game-arg "check-archive=${CMAKE_CURRENT_BINARY_DIR}"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/archive-settings.json")
    set_tests_properties(samples.walker-archive PROPERTIES LABELS "integration;gpu" TIMEOUT 400
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "LevelChecks: all [0-9]+ checks passed")

    # The M10 demonstration (ADR 0102): on a copy of the Walker's project,
    # the Level run from the Cache, then from its JIT and NativeAOT packages,
    # each as a player runs it; the three runs must play out the same.
    add_test(NAME samples.walker-packaged
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 10000000 --frame-rate 60
            --game-arg "warm-project=${walkerWarm}/Walker.crproject"
            --game-arg "check-packaged=${CMAKE_CURRENT_BINARY_DIR}"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/packaged-settings.json")
    set_tests_properties(samples.walker-packaged PROPERTIES LABELS "integration;gpu;aot" TIMEOUT 900
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "LevelChecks: all [0-9]+ checks passed")

    # Packages from the command line (ADR 0100, 0101, 0103): the editor's
    # --build makes a copy of a project's package, which runs from a copy
    # elsewhere, as a player's would, with no options and no .NET but its own
    # (JIT) or none (NativeAOT). Development packages: the Walker in both
    # runtimes and the Diorama, which has a C++ module, as NativeAOT, its
    # module linked in; each logs to its file. Shipping packages of the
    # Walker in both runtimes, from the shipping preset's build (skipped
    # without it), print no information.
    set(packageGame "${PROJECT_SOURCE_DIR}/tests/packaging/package_game.cmake")
    set(walkerLevel 28fe2d80-33ef-4ee4-a409-cc43b935bcc6)
    # Optionally INCLUDE <asset path> packed besides the scene, and EXPECT
    # <text> the Development run must print and log.
    function(crucible_add_package_test name project game scene assets entities runtime configuration)
        cmake_parse_arguments(PARSE_ARGV 8 package "" "INCLUDE;EXPECT" "")
        add_test(NAME ${name}
            COMMAND "${CMAKE_COMMAND}" "-DEDITOR=$<TARGET_FILE:crucible_editor>" "-DENGINE=${engineDirectory}"
                "-DPROJECT=${project}" -DGAME=${game} -DSCENE=${scene} -DASSETS=${assets} -DENTITIES=${entities}
                -DRUNTIME=${runtime} -DCONFIGURATION=${configuration}
                "-DINCLUDE=${package_INCLUDE}" "-DEXPECT=${package_EXPECT}"
                "-DSHIPPING_TOOLKIT=${crucibleShippingDir}/src/native/launcher/CrucibleModule.json"
                "-DOUT=${CMAKE_CURRENT_BINARY_DIR}/${name}" -P "${packageGame}")
        set(labels "integration;gpu")
        if(runtime STREQUAL "NativeAot")
            set(labels "integration;gpu;aot")
        endif()
        set_tests_properties(${name} PROPERTIES LABELS "${labels}" TIMEOUT 900 SKIP_RETURN_CODE 77
            ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
            PASS_REGULAR_EXPRESSION "package_game: the ${game} package \\(${runtime}, ${configuration}\\) ran from elsewhere")
    endfunction()
    set(walkerProject "${walkerWarm}/Walker.crproject")
    crucible_add_package_test(samples.walker-package "${walkerProject}" Walker ${walkerLevel} 8 12 Jit Development)
    crucible_add_package_test(samples.walker-package-shipping "${walkerProject}" Walker ${walkerLevel} 8 12 Jit Shipping)
    if(CRUCIBLE_BUILD_AOT)
        crucible_add_package_test(samples.walker-package-aot "${walkerProject}" Walker ${walkerLevel} 8 12
            NativeAot Development)
        crucible_add_package_test(samples.walker-package-aot-shipping "${walkerProject}" Walker ${walkerLevel} 8 12
            NativeAot Shipping)
        crucible_add_package_test(samples.diorama-package-aot
            "${dioramaWarm}/Diorama.crproject" Diorama
            6ea19c76-2e22-4a7a-be4d-df460ee6ee56 8 25 NativeAot Development)
    endif()
    # A game using a compiled plugin (ADR 0118), the copy launcher.jit.plugins
    # leaves: the plugin's assembly published with it or compiled in, its
    # asset cooked, and its system run.
    set(pluginsProject "${CMAKE_CURRENT_BINARY_DIR}/plugins-compiled-project/Walker.crproject")
    crucible_add_package_test(samples.plugins-package "${pluginsProject}" Walker ${walkerLevel} 9 12 Jit Development
        INCLUDE "Waypoints:Paths" EXPECT "the plugin's system started")
    set_property(TEST samples.plugins-package APPEND PROPERTY FIXTURES_REQUIRED plugins-project)
    if(CRUCIBLE_BUILD_AOT)
        crucible_add_package_test(samples.plugins-package-aot "${pluginsProject}" Walker ${walkerLevel} 9 12
            NativeAot Development INCLUDE "Waypoints:Paths" EXPECT "the plugin's system started")
        set_property(TEST samples.plugins-package-aot APPEND PROPERTY FIXTURES_REQUIRED plugins-project)
    endif()

    # The Build Settings window checking itself (ADR 0098, 0100): on a copy
    # of the Walker's project (in the test's folder), scenes added from the
    # drop-down, reordered, and removed; a folder dragged in from the Assets
    # panel and an asset picked into Always Include; ReadyToRun turned off
    # and on; a build; the window saved as build-settings.png.
    add_test(NAME launcher.jit.build-settings
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 100000
            --game-arg "warm-project=${walkerWarm}/Walker.crproject"
            --game-arg "check-build-settings=${CMAKE_CURRENT_BINARY_DIR}"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/build-settings-settings.json")
    set_tests_properties(launcher.jit.build-settings PROPERTIES LABELS "integration;gpu" TIMEOUT 600
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "BuildSettingsChecks: all [0-9]+ checks passed")

    # Import settings and engine messages checking themselves (ADR 0087): a
    # copy of the Walker's project (in the test's folder), a collider's
    # notice cooked, the core's warning in the Log, and the model's settings
    # edited, reverted, and applied in the inspector.
    add_test(NAME launcher.jit.import-settings
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 100000
            --game-arg "warm-project=${walkerWarm}/Walker.crproject"
            --game-arg "check-import-settings=${CMAKE_CURRENT_BINARY_DIR}"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/import-settings-settings.json")
    set_tests_properties(launcher.jit.import-settings PROPERTIES LABELS "integration;gpu" TIMEOUT 300
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "ImportSettingsChecks: all [0-9]+ checks passed")

    # The Sprite Sheet Editor checking itself (ADR 0107): a copy of the
    # Walker's project with a sheet added, opened from the inspector and by
    # becoming a sprite, sliced into a grid, a clip applied as a sub-asset.
    add_test(NAME launcher.jit.sprite-sheet
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 100000
            --game-arg "warm-project=${walkerWarm}/Walker.crproject"
            --game-arg "check-sprite-sheet=${CMAKE_CURRENT_BINARY_DIR}"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/sprite-sheet-settings.json")
    set_tests_properties(launcher.jit.sprite-sheet PROPERTIES LABELS "integration;gpu" TIMEOUT 300
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "SpriteSheetChecks: all [0-9]+ checks passed")

    # Tilesets and tilemaps checking themselves (ADR 0108): a copy of the
    # Walker's project with a sheet added, a tilemap entity made from the
    # hierarchy, a tileset made by dropping the sheet on the Tiles panel,
    # tile shapes set, the map painted, erased, filled, and picked from with
    # undo, and its file renamed, copied, and deleted with the entity.
    add_test(NAME launcher.jit.tilemaps
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 100000
            --game-arg "warm-project=${walkerWarm}/Walker.crproject"
            --game-arg "check-tilemaps=${CMAKE_CURRENT_BINARY_DIR}"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/tilemaps-settings.json")
    set_tests_properties(launcher.jit.tilemaps PROPERTIES LABELS "integration;gpu" TIMEOUT 300
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "TilemapChecks: all [0-9]+ checks passed")

    # The M11 demonstration (ADR 0114): a copy of the Crossing sample, its
    # scene run standalone with the hero walked by a script under the bridge
    # and over it, then drawn in the editor at four window sizes: exactly its
    # 320x180 image scaled up, the sea round the island, a lamp's light, and
    # the hero under the deck or over it by its floor.
    add_test(NAME samples.crossing
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 100000
            --game-arg "source-project=${PROJECT_SOURCE_DIR}/samples/Crossing/Project/Crossing.crproject"
            --game-arg "check-crossing=${CMAKE_CURRENT_BINARY_DIR}"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/crossing-settings.json")
    set_tests_properties(samples.crossing PROPERTIES LABELS "integration;gpu" TIMEOUT 400
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "CrossingChecks: all [0-9]+ checks passed")

    # 2D lights checking themselves (ADR 0113): on a copy of the Walker's
    # project, Create > 2D's lights made with their defaults and undone, a
    # scene of them saved and opened again, a spot's cone drawn (the window
    # saved as lights-2d.png), and an Unlit sorting layer reaching the renderer.
    add_test(NAME launcher.jit.lights-2d
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 100000
            --game-arg "warm-project=${walkerWarm}/Walker.crproject"
            --game-arg "check-lights-2d=${CMAKE_CURRENT_BINARY_DIR}"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/lights-2d-settings.json")
    set_tests_properties(launcher.jit.lights-2d PROPERTIES LABELS "integration;gpu" TIMEOUT 300
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "Light2DChecks: all [0-9]+ checks passed")

    # Editor assemblies checking themselves (ADR 0116): on a copy of the
    # Walker's project, Code/Editor built into Walker.Editor.dll and loaded
    # in the game's context, reloaded with it, game code needing
    # CRUCIBLE_EDITOR warned about, and a package without the editor code.
    add_test(NAME launcher.jit.editor-code
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 100000
            --game-arg "warm-project=${walkerWarm}/Walker.crproject"
            --game-arg "check-editor-code=${CMAKE_CURRENT_BINARY_DIR}"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/editor-code-settings.json")
    set_tests_properties(launcher.jit.editor-code PROPERTIES LABELS "integration;gpu" TIMEOUT 600
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "EditorCodeChecks: all [0-9]+ checks passed")

    # The public editor API checking itself (ADR 0117): on a copy of the
    # Walker's project, a plugin in Code/Editor changed version by version:
    # its panel, commands, and menus, its undoable edits, its panel's state
    # across reloads and in the layout, and nothing of an old version left.
    add_test(NAME launcher.jit.editor-api
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 100000
            --game-arg "warm-project=${walkerWarm}/Walker.crproject"
            --game-arg "check-editor-api=${CMAKE_CURRENT_BINARY_DIR}"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/editor-api-settings.json")
    set_tests_properties(launcher.jit.editor-api PROPERTIES LABELS "integration;gpu" TIMEOUT 600
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "EditorApiChecks: all [0-9]+ checks passed")

    # Workspaces checking themselves (ADR 0119): on a copy of the Walker's
    # project, an extension's workspaces opened as tabs for entities, shown
    # again, moved, their views rendered, their keys first while focused,
    # closed when their entity goes, and back after a restore or a reload.
    add_test(NAME launcher.jit.workspaces
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 100000
            --game-arg "warm-project=${walkerWarm}/Walker.crproject"
            --game-arg "check-workspaces=${CMAKE_CURRENT_BINARY_DIR}"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/workspaces-settings.json")
    set_tests_properties(launcher.jit.workspaces PROPERTIES LABELS "integration;gpu" TIMEOUT 600
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "WorkspaceChecks: all [0-9]+ checks passed")

    # Opening editors checking itself (ADR 0120): on a copy of the Walker's
    # project, an extension's workspace opening textures and transforms:
    # asked which, Always Open With remembered and forgotten, and the
    # Inspector's Open button.
    add_test(NAME launcher.jit.open-with
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 100000
            --game-arg "warm-project=${walkerWarm}/Walker.crproject"
            --game-arg "check-open-with=${CMAKE_CURRENT_BINARY_DIR}"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/open-with-settings.json")
    set_tests_properties(launcher.jit.open-with PROPERTIES LABELS "integration;gpu" TIMEOUT 600
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "OpenWithChecks: all [0-9]+ checks passed")

    # Viewport tools checking themselves (ADR 0121): on a copy of the
    # Walker's project, an extension's tool chosen by its key and button,
    # taking the left button while the camera keeps the right, its picks,
    # overlay, and keys, Escape back to Move, and its code reloaded.
    add_test(NAME launcher.jit.viewport-tools
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 100000
            --game-arg "warm-project=${walkerWarm}/Walker.crproject"
            --game-arg "check-viewport-tools=${CMAKE_CURRENT_BINARY_DIR}"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/viewport-tools-settings.json")
    set_tests_properties(launcher.jit.viewport-tools PROPERTIES LABELS "integration;gpu" TIMEOUT 600
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "ViewportToolChecks: all [0-9]+ checks passed")

    # Plugins checking themselves (ADR 0118): on a copy of the Walker's
    # project, a source plugin built, loaded, run, and reloaded with the
    # game, packaged without its editor code, and built to share; a second
    # copy using the compiled build, which the package tests below run.
    add_test(NAME launcher.jit.plugins
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 100000
            --game-arg "warm-project=${walkerWarm}/Walker.crproject"
            --game-arg "check-plugins=${CMAKE_CURRENT_BINARY_DIR}"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/plugins-settings.json")
    set_tests_properties(launcher.jit.plugins PROPERTIES LABELS "integration;gpu" TIMEOUT 900
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen" FIXTURES_SETUP plugins-project
        PASS_REGULAR_EXPRESSION "PluginChecks: all [0-9]+ checks passed")

    # Importing dropped files checking itself (ADR 0115): on a copy of the
    # Walker's project, the platform's file drag events handed to the editor
    # as a drop would give them: the note and highlight, the image and name
    # questions, folders, .gltf buffers, and placing on the viewport and the
    # hierarchy.
    add_test(NAME launcher.jit.drops
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 100000
            --game-arg "warm-project=${walkerWarm}/Walker.crproject"
            --game-arg "check-drops=${CMAKE_CURRENT_BINARY_DIR}"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/drops-settings.json")
    set_tests_properties(launcher.jit.drops PROPERTIES LABELS "integration;gpu" TIMEOUT 300
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "DropChecks: all [0-9]+ checks passed")

    # The play toolbar checking itself (ADR 0088): a copy of the Walker's
    # project (in the test's folder), a ball timed through Pause before
    # Play, Step, Pause off, and Stop, each a click on the toolbar.
    add_test(NAME launcher.jit.play-toolbar
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 100000
            --game-arg "warm-project=${walkerWarm}/Walker.crproject"
            --game-arg "check-play-toolbar=${CMAKE_CURRENT_BINARY_DIR}"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/play-toolbar-settings.json")
    set_tests_properties(launcher.jit.play-toolbar PROPERTIES LABELS "integration;gpu" TIMEOUT 300
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "PlayToolbarChecks: all [0-9]+ checks passed")

    # Loading a scene while frames go on (ADR 0099): the Walker's Level
    # loaded into the editor's world with Scene.LoadAsync, its entities
    # coming in all in one frame, once nothing they need still loads.
    add_test(NAME launcher.jit.scene-loading
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 100000
            --game-arg "warm-project=${walkerWarm}/Walker.crproject"
            --game-arg "check-scene-loading=${CMAKE_CURRENT_BINARY_DIR}"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/scene-loading-settings.json")
    set_tests_properties(launcher.jit.scene-loading PROPERTIES LABELS "integration;gpu" TIMEOUT 300
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "SceneLoadingChecks: all [0-9]+ checks passed")

    # A chain's point handles checking themselves (ADR 0095): a copy of the
    # Walker's project (in the test's folder), a chain made, its points
    # inserted, moved, and deleted by drags and keys, undone and redone.
    add_test(NAME launcher.jit.chain-handles
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 100000
            --game-arg "warm-project=${walkerWarm}/Walker.crproject"
            --game-arg "check-chain-handles=${CMAKE_CURRENT_BINARY_DIR}"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/chain-handles-settings.json")
    set_tests_properties(launcher.jit.chain-handles PROPERTIES LABELS "integration;gpu" TIMEOUT 300
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "ChainHandleChecks: all [0-9]+ checks passed")

    # Pickers offering only valid choices (ADR 0089): a copy of the Walker's
    # project (in the test's folder), with the Diorama's house as a model
    # without a skeleton, each drop-down opened and its choices read.
    add_test(NAME launcher.jit.pickers
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 100000
            --game-arg "warm-project=${walkerWarm}/Walker.crproject"
            --game-arg "source-project=${PROJECT_SOURCE_DIR}/samples/Walker/Project/Walker.crproject"
            --game-arg "check-pickers=${CMAKE_CURRENT_BINARY_DIR}"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/pickers-settings.json")
    set_tests_properties(launcher.jit.pickers PROPERTIES LABELS "integration;gpu" TIMEOUT 300
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "PickerChecks: all [0-9]+ checks passed")

    # Native modules checking themselves (ADR 0074): a copy of the Diorama's
    # project (in the test's folder) with its module built, loaded, played,
    # run standalone, and broken.
    add_test(NAME launcher.jit.native
        COMMAND crucible_editor --managed-dir "${engineDirectory}" --headless --frames 100000
            --game-arg "warm-project=${dioramaWarm}/Diorama.crproject"
            --game-arg "check-native=${CMAKE_CURRENT_BINARY_DIR}"
            --game-arg "settings=${CMAKE_CURRENT_BINARY_DIR}/native-settings.json")
    set_tests_properties(launcher.jit.native PROPERTIES LABELS "integration;gpu" TIMEOUT 600
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen"
        PASS_REGULAR_EXPRESSION "NativeChecks: all [0-9]+ checks passed")

    # The M7 demonstration (ADR 0077): the "done when" as one session from a
    # brand-new project, on Vulkan; on Windows on D3D12 too, the captures
    # compared.
    add_test(NAME launcher.jit.demonstration
        COMMAND "${CMAKE_COMMAND}" "-DEDITOR=$<TARGET_FILE:crucible_editor>" "-DENGINE=${engineDirectory}"
            "-DREPOSITORY=${PROJECT_SOURCE_DIR}" "-DOUT=${CMAKE_CURRENT_BINARY_DIR}/demonstration"
            -P "${PROJECT_SOURCE_DIR}/tests/demonstration/compare_demonstration.cmake")
    set_tests_properties(launcher.jit.demonstration PROPERTIES LABELS "integration;gpu" TIMEOUT 600
        ENVIRONMENT "SDL_VIDEO_DRIVER=offscreen")

    # The editor's checks start with no project open and copy a sample into
    # their own folder, so nothing imports or builds into the samples
    # themselves: the checks, and the package tests copying the samples, run
    # at once (ADR 0109). Those not about the cold open copy a warm copy,
    # Cache and all, and wait for its warm-up (ADR 0110).
    set_property(TEST
        samples.walker
        samples.walker-level
        samples.walker-archive
        samples.walker-packaged
        launcher.jit.build-settings
        launcher.jit.import-settings
        launcher.jit.sprite-sheet
        launcher.jit.tilemaps
        launcher.jit.play-toolbar
        launcher.jit.scene-loading
        launcher.jit.chain-handles
        launcher.jit.pickers
        launcher.jit.lights-2d
        launcher.jit.drops
        launcher.jit.editor-code
        launcher.jit.editor-api
        launcher.jit.plugins
        launcher.jit.workspaces
        launcher.jit.open-with
        launcher.jit.viewport-tools
        samples.walker-package
        samples.walker-package-shipping
        APPEND PROPERTY FIXTURES_REQUIRED walker-warm)
    set_property(TEST
        launcher.jit.viewport
        launcher.jit.hierarchy
        launcher.jit.inspector
        launcher.jit.play
        launcher.jit.standalone
        launcher.jit.native
        APPEND PROPERTY FIXTURES_REQUIRED diorama-warm)
    if(CRUCIBLE_BUILD_AOT)
        set_property(TEST samples.walker-package-aot samples.walker-package-aot-shipping
            APPEND PROPERTY FIXTURES_REQUIRED walker-warm)
        set_property(TEST samples.diorama-package-aot APPEND PROPERTY FIXTURES_REQUIRED diorama-warm)
    endif()

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
    # A fixed clock steps by a frame (ADR 0105), so it needs --frame-rate.
    add_test(NAME launcher.fixed-clock-needs-frame-rate COMMAND crucible_launcher --fixed-clock)
    set_tests_properties(launcher.fixed-clock-needs-frame-rate PROPERTIES LABELS integration
        PASS_REGULAR_EXPRESSION "--fixed-clock needs --frame-rate")

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
