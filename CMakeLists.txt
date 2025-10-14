cmake_minimum_required(VERSION 3.29)
project(Crucible)

set(CMAKE_CXX_STANDARD 20)
include(FetchContent)

OPTION(RUN_CRUCIBLE_TESTS "Run Crucible Tests" ON)
OPTION(SLAG_VULKAN_BACKEND "Include Vulkan Renderer backend" ON)
OPTION(SLAG_DX12_BACKEND "Include DX12 Renderer backend" OFF)

add_library(Crucible SHARED
        src/crucible/Crucible.cpp
        src/crucible/Crucible.h
        src/crucible/CrucibleCore.h)

if(RUN_CRUCIBLE_TESTS)

    FetchContent_Declare(
            googletest
            GIT_REPOSITORY https://github.com/google/googletest
            GIT_TAG v1.17.0
            GIT_SHALLOW TRUE
            OVERRIDE_FIND_PACKAGE
    )
    set(gtest_force_shared_crt ON CACHE BOOL "" FORCE)
    add_definitions(-Dgtest_disable_pthreads=ON)
    FetchContent_MakeAvailable(googletest)

    add_executable(CrucibleTests
            tests/CrucibleTestsMain.cpp)

    target_link_libraries(CrucibleTests Crucible GTest::gtest)
    include(GoogleTest)
    gtest_discover_tests(CrucibleTests)
endif ()

target_include_directories(Crucible PUBLIC src)