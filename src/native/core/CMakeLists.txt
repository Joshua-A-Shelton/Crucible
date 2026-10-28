# The C++ core library, in two forms built from the same objects (ADR 0011):
#   crucible_core         shared, for JIT and editor builds; C# binds to it by name
#   crucible_core_static  static, for AOT builds (linked into one executable) and C++ tests

add_library(crucible_core_objects OBJECT)
crucible_set_compile_options(crucible_core_objects)

# crucible/Version.h is generated from the project version.
set(CRUCIBLE_GENERATED_INCLUDE_DIR "${CMAKE_CURRENT_BINARY_DIR}/include")
configure_file("${PROJECT_SOURCE_DIR}/cmake/Version.h.in" "${CRUCIBLE_GENERATED_INCLUDE_DIR}/crucible/Version.h" @ONLY)

target_include_directories(crucible_core_objects PUBLIC
    "${CMAKE_CURRENT_SOURCE_DIR}/include"
    "${CRUCIBLE_GENERATED_INCLUDE_DIR}")

# Every public header is given to the binding generator (ADR 0017); only
# CRUCIBLE_EXPOSE declarations produce bindings, so exposing something new
# takes one annotation and no build-file edit.
file(GLOB CRUCIBLE_CORE_HEADERS CONFIGURE_DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/include/crucible/*.h")
crucible_generate_bindings(
    TARGET crucible_bindings
    OUTPUT_CPP "${CRUCIBLE_GENERATED_DIR}/native/Bindings.g.cpp"
    OUTPUT_CS "${CRUCIBLE_GENERATED_DIR}/managed/Bindings.g.cs"
    INCLUDE_DIRECTORIES "${CMAKE_CURRENT_SOURCE_DIR}/include"
    HEADERS ${CRUCIBLE_CORE_HEADERS})

target_sources(crucible_core_objects PRIVATE
    BinaryFile.cpp
    BindingProbe.cpp
    Checksum.cpp
    Core.cpp
    Demo.cpp
    Ecs.cpp
    Jobs.cpp
    Launch.cpp
    Math.cpp
    MathProbe.cpp
    Memory.cpp
    Platform.cpp
    Render.cpp
    Systems.cpp
    "${CRUCIBLE_GENERATED_DIR}/native/Bindings.g.cpp")

# Only CRUCIBLE_EXPORT symbols are visible from the shared library.
target_compile_definitions(crucible_core_objects PRIVATE CRUCIBLE_CORE_EXPORTS)
set_target_properties(crucible_core_objects PROPERTIES
    CXX_VISIBILITY_PRESET hidden
    VISIBILITY_INLINES_HIDDEN ON)

target_link_libraries(crucible_core_objects PUBLIC slag::slag)

# Public, so tests and the launcher see the same SIMD path as the core.
if(CRUCIBLE_FORCE_SCALAR_SIMD)
    target_compile_definitions(crucible_core_objects PUBLIC CRUCIBLE_SIMD_SCALAR=1)
endif()
# SDL stays private: it never appears in a public header (ADR 0018).
target_link_libraries(crucible_core_objects PRIVATE SDL3::SDL3-static)
# Compression for binary files (ADR 0039).
target_link_libraries(crucible_core_objects PRIVATE libzstd_static)

add_library(crucible_core SHARED)
add_library(crucible::core ALIAS crucible_core)
target_link_libraries(crucible_core PUBLIC crucible_core_objects)
if(NOT WIN32 AND NOT APPLE)
    # Keep Slag's symbols (and any other static library's) out of the
    # shared library's export table.
    target_link_options(crucible_core PRIVATE "LINKER:--exclude-libs,ALL")
endif()

add_library(crucible_core_static STATIC)
add_library(crucible::core_static ALIAS crucible_core_static)
target_link_libraries(crucible_core_static PUBLIC crucible_core_objects)
