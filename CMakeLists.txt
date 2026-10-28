cmake_minimum_required(VERSION 3.25)

project(Crucible
    VERSION 0.1.0
    DESCRIPTION "Game engine built on Slag"
    LANGUAGES CXX)

# ---------------------------------------------------------------------------
# Options
# ---------------------------------------------------------------------------

option(CRUCIBLE_BUILD_TESTS "Build Crucible tests" ${PROJECT_IS_TOP_LEVEL})
option(CRUCIBLE_BUILD_AOT "Build the NativeAOT variants: the AOT launcher and AOT test suites" ON)

# CMake's default MSVC flags turn exceptions on (/EHsc); Crucible builds
# without them (ADR 0030), and adding /EHs-c- on top makes MSVC warn about
# the override on every file. Remove the default instead, for Slag too.
if(MSVC)
    string(REPLACE "/EHsc" "" CMAKE_CXX_FLAGS "${CMAKE_CXX_FLAGS}")
endif()

# The x64 instruction-set level (ADR 0024's amendment): x86-64-v3 (AVX2, FMA,
# BMI) by default, v2 (SSE4.2) for games that target older PCs. Set here, before
# any dependency is fetched, so everything is compiled for it: Slag and SDL
# too, and the NativeAOT engine (IlcInstructionSet, CrucibleDotnet.cmake). The
# launcher checks the CPU before anything else runs.
set(CRUCIBLE_X86_LEVEL "v3" CACHE STRING "x64 instruction-set level: v3 (AVX2) or v2 (SSE4.2)")
set_property(CACHE CRUCIBLE_X86_LEVEL PROPERTY STRINGS v2 v3)
if(NOT CRUCIBLE_X86_LEVEL MATCHES "^v[23]$")
    message(FATAL_ERROR "CRUCIBLE_X86_LEVEL must be v2 or v3, not '${CRUCIBLE_X86_LEVEL}'")
endif()
if(CMAKE_SYSTEM_PROCESSOR MATCHES "^(x86_64|AMD64|amd64)$")
    set(CRUCIBLE_X64 ON)
    if(MSVC)
        if(CRUCIBLE_X86_LEVEL STREQUAL "v3")
            set(CRUCIBLE_X86_FLAGS "/arch:AVX2")
        else()
            set(CRUCIBLE_X86_FLAGS "/arch:SSE4.2")
        endif()
    else()
        set(CRUCIBLE_X86_FLAGS "-march=x86-64-${CRUCIBLE_X86_LEVEL}")
    endif()
    string(APPEND CMAKE_C_FLAGS " ${CRUCIBLE_X86_FLAGS}")
    string(APPEND CMAKE_CXX_FLAGS " ${CRUCIBLE_X86_FLAGS}")
    message(STATUS "Crucible: x64 level ${CRUCIBLE_X86_LEVEL} (${CRUCIBLE_X86_FLAGS})")
endif()

# For testing the SIMD types' scalar paths, which ARM64 and unknown CPUs rely
# on (ADR 0024): every SIMD type uses its plain C++ path.
option(CRUCIBLE_FORCE_SCALAR_SIMD "Use the SIMD types' scalar paths (for testing them)" OFF)

# The static release C runtime (/MT) everywhere on Windows, in every
# configuration and for Slag too (ADR 0031): NativeAOT's runtime is built with
# it, and an executable can't mix C runtimes.
set(CMAKE_MSVC_RUNTIME_LIBRARY MultiThreaded)

# The core is also a shared library (ADR 0011), so every static library linked
# into it, Slag included, must be position-independent.
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

list(APPEND CMAKE_MODULE_PATH "${CMAKE_CURRENT_SOURCE_DIR}/cmake")
include(CrucibleCompileOptions)
include(CrucibleDependencies)
include(CrucibleDotnet)
include(CrucibleHosting)
include(CrucibleBindgen)

if(CRUCIBLE_BUILD_TESTS)
    enable_testing()
endif()

# ---------------------------------------------------------------------------
# Native (C++)
# ---------------------------------------------------------------------------

add_subdirectory(src/native/core)
# Editor-only: decoders, mipmaps, and GPU compression (ADR 0042).
add_subdirectory(src/native/importers)

# ---------------------------------------------------------------------------
# Managed (C#): one dotnet invocation for the whole solution, so projects that
# reference each other are never built by two processes at once.
# ---------------------------------------------------------------------------

crucible_add_dotnet_build(crucible_managed "${CMAKE_CURRENT_SOURCE_DIR}/Crucible.slnx")
# The engine compiles the generated C# bindings, and Crucible.Assets the
# importers' (whose tests load the importers' library).
add_dependencies(crucible_managed crucible_bindings crucible_importers_bindings crucible_importers)

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
