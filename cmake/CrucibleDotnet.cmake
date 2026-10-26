# Builds C# with the .NET SDK and registers C# test executables with CTest.
#
# CMake can't compile C# with Ninja, so the managed build is a custom target
# that runs `dotnet build`. It runs on every build; MSBuild's own incremental
# build decides what actually recompiles. Output goes under the CMake build
# directory (the SDK's artifacts layout), so each preset keeps its own.

find_program(DOTNET_EXECUTABLE dotnet REQUIRED)

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
        COMMAND "${DOTNET_EXECUTABLE}" build "${project}"
            --configuration ${CRUCIBLE_DOTNET_CONFIGURATION}
            --nologo
            "-p:ArtifactsPath=${CRUCIBLE_DOTNET_ARTIFACTS_DIR}"
            "-p:Version=${PROJECT_VERSION}"
        WORKING_DIRECTORY "${CMAKE_SOURCE_DIR}"
        USES_TERMINAL
        VERBATIM)
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
