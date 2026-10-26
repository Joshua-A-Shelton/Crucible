# The C++ core library. Static for now; ADR 0011 makes it a shared library for
# JIT and editor builds, which M1 sets up along with the C# boundary.

add_library(crucible_core STATIC)
add_library(crucible::core ALIAS crucible_core)

target_compile_features(crucible_core PUBLIC cxx_std_20)

# crucible/Version.h is generated from the project version.
set(CRUCIBLE_GENERATED_INCLUDE_DIR "${CMAKE_CURRENT_BINARY_DIR}/include")
configure_file("${PROJECT_SOURCE_DIR}/cmake/Version.h.in" "${CRUCIBLE_GENERATED_INCLUDE_DIR}/crucible/Version.h" @ONLY)

target_include_directories(crucible_core PUBLIC
    "${CMAKE_CURRENT_SOURCE_DIR}/include"
    "${CRUCIBLE_GENERATED_INCLUDE_DIR}")

target_sources(crucible_core PRIVATE
    Core.cpp)

target_link_libraries(crucible_core PUBLIC slag::slag)
