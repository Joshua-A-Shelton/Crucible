cmake_minimum_required(VERSION 3.25)

project(Crucible
    VERSION 0.1.0
    DESCRIPTION "Game engine built on Slag"
    LANGUAGES CXX)

# ---------------------------------------------------------------------------
# Options
# ---------------------------------------------------------------------------

option(CRUCIBLE_BUILD_TESTS "Build Crucible tests" ${PROJECT_IS_TOP_LEVEL})

list(APPEND CMAKE_MODULE_PATH "${CMAKE_CURRENT_SOURCE_DIR}/cmake")
include(CrucibleDependencies)
include(CrucibleDotnet)

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

# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------

if(CRUCIBLE_BUILD_TESTS)
    add_subdirectory(tests/native)
    add_subdirectory(tests/managed)
endif()
