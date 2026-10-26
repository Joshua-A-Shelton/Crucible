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
    string(TOLOWER ${CRUCIBLE_DOTNET_CONFIGURATION} configuration)
    add_test(NAME ${project}
        COMMAND "${CRUCIBLE_DOTNET_ARTIFACTS_DIR}/bin/${project}/${configuration}/${project}${CMAKE_EXECUTABLE_SUFFIX}")
    set_tests_properties(${project} PROPERTIES LABELS managed)
endfunction()
