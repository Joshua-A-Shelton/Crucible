cmake_minimum_required(VERSION 3.25)

project(Crucible
    VERSION 0.1.0
    DESCRIPTION "Game engine built on Slag"
    LANGUAGES CXX)

# ---------------------------------------------------------------------------
# Options
# ---------------------------------------------------------------------------

option(CRUCIBLE_BUILD_TESTS "Build Crucible tests" ${PROJECT_IS_TOP_LEVEL})
# Off on Windows until M1 step 8 brings the AOT launcher's link there (the
# linker response file is written in Unix linker syntax for now).
if(WIN32)
    set(_crucible_aot_default OFF)
else()
    set(_crucible_aot_default ON)
endif()
option(CRUCIBLE_BUILD_AOT "Build the NativeAOT variants: the AOT launcher and AOT test suites" ${_crucible_aot_default})

# The core is also a shared library (ADR 0011), so every static library linked
# into it, Slag included, must be position-independent.
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

list(APPEND CMAKE_MODULE_PATH "${CMAKE_CURRENT_SOURCE_DIR}/cmake")
include(CrucibleCompileOptions)
include(CrucibleDependencies)
include(CrucibleDotnet)
include(CrucibleHosting)

if(CRUCIBLE_BUILD_TESTS)
    enable_testing()
endif()

# ---------------------------------------------------------------------------
# Native (C++)
# ---------------------------------------------------------------------------

add_subdirectory(src/native/core)

# ---------------------------------------------------------------------------
# Managed (C#): one dotnet invocation for the whole solution, so projects that
# reference each other are never built by two processes at once.
# ---------------------------------------------------------------------------

crucible_add_dotnet_build(crucible_managed "${CMAKE_CURRENT_SOURCE_DIR}/Crucible.slnx")

if(CRUCIBLE_BUILD_AOT)
    # The engine's C# layer as a NativeAOT static library, for the AOT
    # launcher. NativeAOT also writes the runtime's support libraries to a
    # linker response file (Directory.Build.targets).
    crucible_dotnet_publish_directory(Crucible.Engine engineAotDirectory)
    set(CRUCIBLE_ENGINE_AOT_LIBRARY "${engineAotDirectory}/Crucible.Engine${_crucible_aot_static_suffix}")
    set(CRUCIBLE_ENGINE_AOT_LINK_INPUTS "${CMAKE_BINARY_DIR}/crucible_engine_aot.rsp")
    crucible_add_dotnet_aot_publish(crucible_engine_aot
        "${CMAKE_CURRENT_SOURCE_DIR}/src/managed/Crucible.Engine/Crucible.Engine.csproj"
        BYPRODUCTS "${CRUCIBLE_ENGINE_AOT_LIBRARY}" "${CRUCIBLE_ENGINE_AOT_LINK_INPUTS}"
        ARGUMENTS -p:NativeLib=Static "-p:CrucibleLinkInputsFile=${CRUCIBLE_ENGINE_AOT_LINK_INPUTS}")
endif()

# ---------------------------------------------------------------------------
# Launcher: after the managed section, which defines the AOT engine it links.
# ---------------------------------------------------------------------------

add_subdirectory(src/native/launcher)

# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------

if(CRUCIBLE_BUILD_TESTS)
    add_subdirectory(tests/native)
    add_subdirectory(tests/managed)
endif()
