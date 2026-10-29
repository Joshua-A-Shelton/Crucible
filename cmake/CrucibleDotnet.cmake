# Builds C# with the .NET SDK and registers C# test executables with CTest.
#
# CMake can't compile C# with Ninja, so the managed build is a custom target
# that runs `dotnet build`. It runs on every build; MSBuild's own incremental
# build decides what actually recompiles. Output goes under the CMake build
# directory (the SDK's artifacts layout), so each preset keeps its own.

find_program(DOTNET_EXECUTABLE dotnet REQUIRED)

# The command CMake runs for dotnet build and publish. Visual Studio's
# developer environment (vcvars64.bat) sets Platform=x64 for the C++ tools;
# MSBuild reads environment variables as properties, so it would build the
# C# solution for a platform that doesn't exist. Clear it first.
set(CRUCIBLE_DOTNET "${CMAKE_COMMAND}" -E env --unset=Platform "${DOTNET_EXECUTABLE}")

if(CMAKE_BUILD_TYPE STREQUAL "Debug")
    set(CRUCIBLE_DOTNET_CONFIGURATION Debug)
else()
    set(CRUCIBLE_DOTNET_CONFIGURATION Release)
endif()

string(TOLOWER ${CRUCIBLE_DOTNET_CONFIGURATION} CRUCIBLE_DOTNET_CONFIGURATION_DIR)
set(CRUCIBLE_DOTNET_ARTIFACTS_DIR "${CMAKE_BINARY_DIR}/artifacts")

# crucible_add_dotnet_build(<target> <solution or project file>)
function(crucible_add_dotnet_build target project)
    add_custom_target(${target} ALL
        COMMAND ${CRUCIBLE_DOTNET} build "${project}"
            --configuration ${CRUCIBLE_DOTNET_CONFIGURATION}
            --nologo
            "-p:ArtifactsPath=${CRUCIBLE_DOTNET_ARTIFACTS_DIR}"
            "-p:Version=${PROJECT_VERSION}"
            "-p:CrucibleGeneratedDir=${CRUCIBLE_GENERATED_DIR}"
        WORKING_DIRECTORY "${CMAKE_SOURCE_DIR}"
        USES_TERMINAL
        VERBATIM)
endfunction()

# ---------------------------------------------------------------------------
# NativeAOT (ADR 0001, 0011)
# ---------------------------------------------------------------------------

if(WIN32)
    set(CRUCIBLE_DOTNET_RID win-x64)
    set(_crucible_aot_static_suffix .lib)
else()
    set(CRUCIBLE_DOTNET_RID linux-x64)
    set(_crucible_aot_static_suffix .a)
endif()

# MSBuild props for AOT builds: direct P/Invoke into the C++ core, and for AOT
# executables, the core's static libraries for the .NET toolchain to link.
# Imported by Directory.Build.targets.
set(CRUCIBLE_NATIVE_PROPS "${CMAKE_BINARY_DIR}/CrucibleNative.props")
# NativeAOT links with plain clang and only a small C++ compatibility shim of
# its own, so the C++ standard library that Slag and the core need is added
# here. As a NativeSystemLibrary it comes after the static libraries on the
# link line, which is where it has to be.
# On Windows: d3d12 and dxgi for Slag; for SDL linked statically, the system
# libraries SDL's documentation lists that NativeAOT doesn't already link
# (setupapi, winmm, imm32, version, gdi32, shell32).
if(WIN32)
    set(_crucible_core_system_libraries "<NativeLibrary Include=\"d3d12.lib\" /><NativeLibrary Include=\"dxgi.lib\" /><NativeLibrary Include=\"setupapi.lib\" /><NativeLibrary Include=\"winmm.lib\" /><NativeLibrary Include=\"imm32.lib\" /><NativeLibrary Include=\"version.lib\" /><NativeLibrary Include=\"gdi32.lib\" /><NativeLibrary Include=\"shell32.lib\" />")
else()
    set(_crucible_core_system_libraries "<NativeSystemLibrary Include=\"stdc++\" />")
endif()
file(GENERATE OUTPUT "${CRUCIBLE_NATIVE_PROPS}" CONTENT "<Project>
  <ItemGroup Condition=\"'$(PublishAot)' == 'true'\">
    <DirectPInvoke Include=\"crucible_core\" />
  </ItemGroup>
  <ItemGroup Condition=\"'$(PublishAot)' == 'true' and '$(NativeLib)' == '' and '$(OutputType)' == 'Exe'\">
    <NativeLibrary Include=\"$<TARGET_LINKER_FILE:crucible_core_static>\" />
    <NativeLibrary Include=\"$<TARGET_LINKER_FILE:slag_rendergraph>\" />
    <NativeLibrary Include=\"$<TARGET_LINKER_FILE:slag>\" />
    <NativeLibrary Include=\"$<TARGET_LINKER_FILE:SDL3-static>\" />
    <NativeLibrary Include=\"$<TARGET_LINKER_FILE:libzstd_static>\" />
    ${_crucible_core_system_libraries}
  </ItemGroup>
</Project>
")

# crucible_dotnet_publish_directory(<project name> <output variable>)
function(crucible_dotnet_publish_directory projectName outputVariable)
    set(${outputVariable}
        "${CRUCIBLE_DOTNET_ARTIFACTS_DIR}/publish/${projectName}/${CRUCIBLE_DOTNET_CONFIGURATION_DIR}_${CRUCIBLE_DOTNET_RID}"
        PARENT_SCOPE)
endfunction()

# crucible_add_dotnet_aot_publish(<target> <project file>
#     [BYPRODUCTS <files>...] [ARGUMENTS <extra dotnet arguments>...])
# Publishes a project with NativeAOT. BYPRODUCTS names the files other targets
# link against, so Ninja knows this target makes them. Runs after the JIT
# build: both build the same referenced projects into the same output
# folders, so they must not run at once.
function(crucible_add_dotnet_aot_publish target project)
    cmake_parse_arguments(PARSE_ARGV 2 publish "" "" "BYPRODUCTS;ARGUMENTS")
    # The NativeAOT compiler targets the same x64 level as the C++ (ADR
    # 0024's amendment); a JIT picks the CPU's own instructions at run time.
    set(instructionSet "")
    if(CRUCIBLE_X64)
        set(instructionSet "-p:IlcInstructionSet=x86-64-${CRUCIBLE_X86_LEVEL}")
    endif()
    add_custom_target(${target} ALL
        COMMAND ${CRUCIBLE_DOTNET} publish "${project}"
            --configuration ${CRUCIBLE_DOTNET_CONFIGURATION}
            --runtime ${CRUCIBLE_DOTNET_RID}
            --nologo
            -p:PublishAot=true
            "-p:ArtifactsPath=${CRUCIBLE_DOTNET_ARTIFACTS_DIR}"
            "-p:Version=${PROJECT_VERSION}"
            "-p:CrucibleGeneratedDir=${CRUCIBLE_GENERATED_DIR}"
            "-p:CrucibleNativeProps=${CRUCIBLE_NATIVE_PROPS}"
            ${instructionSet}
            ${publish_ARGUMENTS}
        BYPRODUCTS ${publish_BYPRODUCTS}
        WORKING_DIRECTORY "${CMAKE_SOURCE_DIR}"
        USES_TERMINAL
        VERBATIM)
    add_dependencies(${target} crucible_managed crucible_core_static)
endfunction()

# crucible_add_dotnet_aot_test(<project name> <project file>)
# The same test suite as crucible_add_dotnet_test, compiled with NativeAOT and
# the core linked in statically, so no library search path is needed.
function(crucible_add_dotnet_aot_test project projectFile after)
    crucible_dotnet_publish_directory(${project} publishDirectory)
    crucible_add_dotnet_aot_publish(${project}.aot "${projectFile}")
    add_dependencies(${project}.aot ${after})
    add_test(NAME ${project}.aot COMMAND "${publishDirectory}/${project}${CMAKE_EXECUTABLE_SUFFIX}")
    set_tests_properties(${project}.aot PROPERTIES LABELS "managed;aot")
endfunction()

# crucible_add_dotnet_test(<project name>)
# Test projects run on Microsoft.Testing.Platform (ADR 0029), so each one is an
# executable that CTest runs directly.
function(crucible_add_dotnet_test project)
    add_test(NAME ${project}
        COMMAND "${CRUCIBLE_DOTNET_ARTIFACTS_DIR}/bin/${project}/${CRUCIBLE_DOTNET_CONFIGURATION_DIR}/${project}${CMAKE_EXECUTABLE_SUFFIX}")
    # The shared core lives in its own build folder, which nothing points .NET
    # at, so add it to the library search path for test runs. Shipped games
    # keep the core next to the executable and need nothing like this.
    if(WIN32)
        set(_crucible_library_path_variable PATH)
    else()
        set(_crucible_library_path_variable LD_LIBRARY_PATH)
    endif()
    set_tests_properties(${project} PROPERTIES
        LABELS managed
        ENVIRONMENT_MODIFICATION "${_crucible_library_path_variable}=path_list_prepend:$<TARGET_FILE_DIR:crucible_core>")
endfunction()
