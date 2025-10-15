cmake_minimum_required(VERSION 3.29)
project(Crucible)

set(CMAKE_CXX_STANDARD 20)
include(FetchContent)

OPTION(SLAG_LIBRARY_VERSION "v0.9.11-rc2")

OPTION(RUN_CRUCIBLE_TESTS "Run Crucible Tests" ON)
OPTION(SLAG_VULKAN_BACKEND "Include Vulkan Renderer backend" ON)
OPTION(SLAG_DX12_BACKEND "Include DX12 Renderer backend" OFF)

include(FetchContent)
# Get required
fetchcontent_declare(
        Slag
        GIT_REPOSITORY https://github.com/Joshua-A-Shelton/Slag.git
        GIT_TAG ${SLAG_LIBRARY_VERSION}
        GIT_SHALLOW TRUE
        OVERRIDE_FIND_PACKAGE
)
fetchcontent_makeavailable(Slag)

if(NOT WIN32)
target_compile_options(vk-bootstrap PUBLIC -fPIC)
endif ()

find_package(Slag REQUIRED)

set(BUILD_SHARED_LIBS OFF)
set(SDL_SHARED OFF)
set(SDL_STATIC ON)
fetchcontent_declare(
        SDL3
        GIT_REPOSITORY https://github.com/libsdl-org/SDL
        GIT_TAG release-3.2.24
        GIT_SHALLOW TRUE
        OVERRIDE_FIND_PACKAGE
)
fetchcontent_makeavailable(SDL3)
find_package(SDL3 REQUIRED)

MACRO(SUBDIRLIST result curdir)
    FILE(GLOB children RELATIVE ${curdir} ${curdir}/*)
    SET(dirlist "")
    FOREACH(child ${children})
        IF(IS_DIRECTORY ${curdir}/${child})
            LIST(APPEND dirlist ${child})
        ENDIF()
    ENDFOREACH()
    SET(${result} ${dirlist})
ENDMACRO()
if(WIN32)
    SUBDIRLIST(DOTNETDIRS "C:/Program Files/dotnet/packs/Microsoft.NETCore.App.Host.win-x64/")
    list(GET DOTNETDIRS -1 DOTNETDIR)
    set(DOT_NET_HOST_LOCATION "C:/Program Files/dotnet/packs/Microsoft.NETCore.App.Host.win-x64/${DOTNETDIR}/runtimes/win-x64/native")
    set(NETHOST_DYNAMIC_LIBRARY "nethost.dll")
else()
    execute_process(COMMAND find /usr/share/dotnet/packs -name libnethost.so OUTPUT_VARIABLE NETHOST_PATH)
    execute_process(COMMAND dirname ${NETHOST_PATH} OUTPUT_VARIABLE NETHOST_DIR OUTPUT_STRIP_TRAILING_WHITESPACE)
    set(DOT_NET_HOST_LOCATION ${NETHOST_DIR})
    set(NETHOST_DYNAMIC_LIBRARY "libnethost.so")
endif()


add_library(Crucible SHARED
        src/crucible/Crucible.cpp
        src/crucible/Crucible.h
        src/crucible/CrucibleCore.h
        src/crucible/scripting/ScriptingEngine.cpp
        src/crucible/scripting/ScriptingEngine.h
        src/crucible/scripting/ManagedFunctionPointers.h
        src/crucible/scripting/ManagedType.cpp
        src/crucible/scripting/ManagedType.h
        src/crucible/scripting/BindingFlags.h
        src/crucible/scripting/ManagedFunction.cpp
        src/crucible/scripting/ManagedFunction.h)

target_link_directories(Crucible PUBLIC ${DOT_NET_HOST_LOCATION})
target_link_libraries(Crucible PRIVATE SDL3::SDL3-static Slag nethost)
target_include_directories(Crucible PRIVATE ${DOT_NET_HOST_LOCATION})

#copy over c# config file (configure_file() is the copy operation, not related to the "configure file" that we're copying)
configure_file(config/CSharpConfig.json "${CMAKE_BINARY_DIR}/config/CSharpConfig.json" COPYONLY)
#copy over library that allows us to enter c# code
configure_file("${DOT_NET_HOST_LOCATION}/${NETHOST_DYNAMIC_LIBRARY}" "${CMAKE_BINARY_DIR}/${NETHOST_DYNAMIC_LIBRARY}" COPYONLY)
#build c# dll
file(GLOB_RECURSE CRUCIBLE_CSHARP_FILES CONFIGURE_DEPENDS ${CMAKE_CURRENT_SOURCE_DIR}/dotnet/*.cs)
add_custom_command(
        OUTPUT ${CMAKE_BINARY_DIR}/Crucible-Runtime.dll
        WORKING_DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR}/dotnet/Crucible-Runtime
        COMMAND dotnet publish Crucible-Runtime /p:Configuration=${CRUCIBLE_BUILD_TYPE} /p:Platform="Any CPU" /p:AllowUnsafeBlocks=true -o ${CMAKE_BINARY_DIR}
        DEPENDS ${CRUCIBLE_CSHARP_FILES}
        COMMENT "Building Crucible .net runtime"
)
add_custom_target(Crucible-Runtime ALL DEPENDS ${CMAKE_BINARY_DIR}/Crucible-Runtime.dll)
add_dependencies(Crucible Crucible-Runtime)


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
            tests/CrucibleTestsMain.cpp
            tests/tests/Textures.cpp)

    target_link_libraries(CrucibleTests Crucible GTest::gtest)
    include(GoogleTest)
    gtest_discover_tests(CrucibleTests PROPERTIES DISCOVERY_TIMEOUT 600 DISCOVERY_MODE PRE_TEST)
endif ()

target_include_directories(Crucible PUBLIC src)