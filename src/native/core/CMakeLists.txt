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

target_sources(crucible_core_objects PRIVATE
    Core.cpp)

# Only CRUCIBLE_EXPORT symbols are visible from the shared library.
target_compile_definitions(crucible_core_objects PRIVATE CRUCIBLE_CORE_EXPORTS)
set_target_properties(crucible_core_objects PROPERTIES
    CXX_VISIBILITY_PRESET hidden
    VISIBILITY_INLINES_HIDDEN ON)

target_link_libraries(crucible_core_objects PUBLIC slag::slag)

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
