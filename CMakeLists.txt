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
    set(NETHOST_FILE_EXTENSION "dll")
else()
    set(DOT_NET_HOST_LOCATION "/usr/lib/dotnet/packs/Microsoft.NETCore.App.Host.ubuntu.22.04-x64/9.0.1/runtimes/ubuntu.22.04-x64/native/")
    set(NETHOST_FILE_EXTENSION "so")
endif()

add_library(Crucible
        src/crucible/core/Window.cpp
        src/crucible/core/Window.h
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
)

target_link_directories(Crucible PUBLIC ${DOT_NET_HOST_LOCATION})
target_link_libraries(Crucible PUBLIC Slag SDL3::SDL3 glm nethost)
target_include_directories(Crucible PRIVATE third-party/stb_image ${DOT_NET_HOST_LOCATION})

if(CMAKE_BUILD_TYPE STREQUAL "Debug")
    set(CRUCIBLE_BUILD_TYPE "Debug")
else()
    set(CRUCIBLE_BUILD_TYPE "Release")
endif()

#copy over c# config file (configure_file() is the copy operation, not related to the "configure file" that we're copying)
configure_file(config/CSharpConfig.json ${CMAKE_BINARY_DIR}/config/CSharpConfig.json COPYONLY)
#copy over library that allows us to enter c# code
configure_file(${DOT_NET_HOST_LOCATION}/nethost.${NETHOST_FILE_EXTENSION} ${CMAKE_BINARY_DIR}/nethost.${NETHOST_FILE_EXTENSION} COPYONLY)
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

option(CrucibleExampleProject "Build an example of crucible project" OFF)
if(CrucibleExampleProject)
    add_executable(CrucibleExample
            tests/example.cpp)
    target_link_libraries(CrucibleExample Crucible)
    target_include_directories(CrucibleExample PRIVATE src)
    configure_file(tests/crucible.png ${CMAKE_BINARY_DIR}/crucible.png COPYONLY)
endif ()

target_include_directories(Crucible PUBLIC src)