cmake_minimum_required(VERSION 3.29)
project(Crucible)

set(CMAKE_CXX_STANDARD 20)
#get slag library
include(FetchContent)
fetchcontent_declare(
        Slag
        GIT_REPOSITORY https://github.com/Joshua-A-Shelton/Slag.git
        GIT_TAG origin/master
        GIT_SHALLOW TRUE
        OVERRIDE_FIND_PACKAGE
)
fetchcontent_makeavailable(Slag)

find_package(Slag REQUIRED)

find_package(Boost REQUIRED)

#SDL windowing and input library
fetchcontent_declare(
        SDL3
        GIT_REPOSITORY https://github.com/libsdl-org/SDL
        GIT_TAG origin/main
        GIT_SHALLOW TRUE
        OVERRIDE_FIND_PACKAGE
)
fetchcontent_makeavailable(SDL3)
find_package(SDL3 REQUIRED)

#Glm linear algebra library
fetchcontent_declare(
        glm
        GIT_REPOSITORY https://github.com/g-truc/glm
        GIT_TAG origin/master
        GIT_SHALLOW TRUE
        OVERRIDE_FIND_PACKAGE
)
FetchContent_MakeAvailable(glm)
find_package(glm)

if(WIN32)
    set(DOT_NET_HOST_LOCATION "C:/Program Files/dotnet/packs/Microsoft.NETCore.App.Host.win-x64/9.0.1/runtimes/win-x64/native")
    set(NETHOST_DYNAMIC_LIBRARY "nethost.dll")
else()
    set(DOT_NET_HOST_LOCATION "/usr/lib/dotnet/packs/Microsoft.NETCore.App.Host.ubuntu.22.04-x64/9.0.2/runtimes/ubuntu.22.04-x64/native/")
    set(NETHOST_DYNAMIC_LIBRARY "libnethost.so")
endif()

add_library(Crucible
        src/crucible/Crucible.cpp
        src/crucible/Crucible.h
        src/crucible/Game.cpp
        src/crucible/Game.h
        src/crucible/CrucibleCore.h
        src/crucible/scripting/ScriptingEngine.cpp
        src/crucible/scripting/ScriptingEngine.h
        src/crucible/scripting/ManagedType.h
        src/crucible/scripting/ManagedType.cpp
        src/crucible/scripting/CoreFunctions.cpp
        src/crucible/scripting/CoreFunctions.h
        src/crucible/core/Transform.cpp
        src/crucible/core/Transform.h
        src/crucible/core/Mesh.cpp
        src/crucible/core/Mesh.h
        src/crucible/core/VertexAttributeStream.cpp
        src/crucible/core/VertexAttributeStream.h
        src/crucible/core/scenes/Node.cpp
        src/crucible/core/scenes/Node.h
        src/crucible/scripting/ManagedInstance.cpp
        src/crucible/scripting/ManagedInstance.h
)

#include_directories(${Boost_INCLUDE_DIRS})

target_link_directories(Crucible PUBLIC ${DOT_NET_HOST_LOCATION})
target_link_libraries(Crucible PUBLIC Slag SDL3::SDL3 glm nethost ${Boost_LIBRARIES})
target_include_directories(Crucible PRIVATE third-party/stb_image PUBLIC ${DOT_NET_HOST_LOCATION} ${Boost_INCLUDE_DIRS})

if(CMAKE_BUILD_TYPE STREQUAL "Debug")
    set(CRUCIBLE_BUILD_TYPE "Debug")
else()
    set(CRUCIBLE_BUILD_TYPE "Release")
endif()

#copy over c# config file (configure_file() is the copy operation, not related to the "configure file" that we're copying)
configure_file(config/CSharpConfig.json ${CMAKE_BINARY_DIR}/config/CSharpConfig.json COPYONLY)
#copy over library that allows us to enter c# code
configure_file(${DOT_NET_HOST_LOCATION}/${NETHOST_DYNAMIC_LIBRARY} ${CMAKE_BINARY_DIR}/${NETHOST_DYNAMIC_LIBRARY} COPYONLY)
#build c# dll
file(GLOB_RECURSE CRUCIBLE_CSHARP_FILES CONFIGURE_DEPENDS ${CMAKE_CURRENT_SOURCE_DIR}/dotnet/*.cs)
add_custom_command(
        OUTPUT ${CMAKE_BINARY_DIR}/Crucible-Runtime.dll
        WORKING_DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR}/dotnet/Crucible-Runtime
        COMMAND dotnet publish Crucible-Runtime /p:Configuration=${CRUCIBLE_BUILD_TYPE} /p:Platform="Any CPU" -o ${CMAKE_BINARY_DIR}
        DEPENDS ${CRUCIBLE_CSHARP_FILES}
        COMMENT "Building Crucible .net runtime"
)
add_custom_target(Crucible-Runtime ALL DEPENDS ${CMAKE_BINARY_DIR}/Crucible-Runtime.dll)
add_dependencies(Crucible Crucible-Runtime)

option(CrucibleTests "Build crucible tests" ON)
if(CrucibleTests)
    message("building Crucible tests")
    find_package(SDL2 REQUIRED)
    find_package(glm CONFIG REQUIRED)

    file(GLOB_RECURSE CRUCIBLE_TEST_CSHARP_FILES CONFIGURE_DEPENDS ${CMAKE_CURRENT_SOURCE_DIR}/test/dotnet/*.cs)
    add_custom_command(
            OUTPUT ${CMAKE_BINARY_DIR}/plugins/Crucible-Runtime-Tests.dll
            WORKING_DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR}/test/dotnet/Crucible-Runtime-Tests
            COMMAND dotnet publish Crucible-Runtime-Tests /p:Configuration=${CRUCIBLE_BUILD_TYPE} /p:Platform="Any CPU" -o ${CMAKE_BINARY_DIR}/plugins
            DEPENDS ${CRUCIBLE_TEST_CSHARP_FILES}
            COMMENT "Building Crucible .net runtime Tests"
    )

    include(FetchContent)
    FetchContent_Declare(
            googletest
            URL https://github.com/google/googletest/archive/03597a01ee50ed33e9dfd640b249b4be3799d395.zip
    )
    # For Windows: Prevent overriding the parent project's compiler/linker settings
    set(gtest_force_shared_crt ON CACHE BOOL "" FORCE)
    #add_definitions(-Dgtest_disable_pthreads=ON)
    FetchContent_MakeAvailable(googletest)

    enable_testing()

    add_executable(CrucibleTests
            test/Main.cpp
            test/tests/NodeTest.cpp
            test/tests/NodeBehaviorTest.cpp
            test/tests/ScriptingTest.cpp
            test/tests/TransformTest.cpp
    )

    target_link_libraries(CrucibleTests Crucible GTest::gtest)
    target_include_directories(CrucibleTests PRIVATE src)
    include(GoogleTest)
    gtest_discover_tests(CrucibleTests)

    add_custom_target(Crucible-Runtime-Tests ALL DEPENDS ${CMAKE_BINARY_DIR}/plugins/Crucible-Runtime-Tests.dll)
    add_dependencies(Crucible-Runtime-Tests Crucible-Runtime)
    add_dependencies(CrucibleTests Crucible-Runtime-Tests)
endif ()

target_include_directories(Crucible PUBLIC src)