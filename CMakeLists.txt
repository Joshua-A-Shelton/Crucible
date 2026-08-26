cmake_minimum_required(VERSION 3.29)
project(Crucible)

set(CMAKE_CXX_STANDARD 20)
include(FetchContent)

OPTION(CRUCIBLE_RENDERING_MODULE "Include Crucible Rendering Module" ON)
OPTION(CRUCIBLE_SCRIPTING_MODULE "Include Crucible C# Scripting Module" ON)


OPTION(CRUCIBLE_AOT_SCRIPTING "Compile C# to native code ahead of time" OFF)

SET(CRUCIBLE_AOT_SCRIPTING ON CACHE BOOL "Manually Set AOT" FORCE)

SET(CRUCIBLE_DOT_NET_VERSION 10)

OPTION(CRUCIBLE_BUILD_TESTS "Build Crucible Tests" ON)


SET(CRUCIBLE_INCLUDE_SDL OFF)

if(CMAKE_BUILD_TYPE STREQUAL "Debug")
    set(CRUCIBLE_BUILD_TYPE "Debug")
else()
    set(CRUCIBLE_BUILD_TYPE "Release")
endif()

if(CRUCIBLE_AOT_SCRIPTING)
    add_definitions(-DCRUCIBLE_AOT_SCRIPTING=1)
endif()


if(CRUCIBLE_RENDERING_MODULE)
    SET(CRUCIBLE_INCLUDE_SDL ON)
    add_definitions(-DCRUCIBLE_RENDERING_MODULE=1)
    fetchcontent_declare(
            Slag
            GIT_REPOSITORY https://github.com/Joshua-A-Shelton/Slag.git
            GIT_TAG origin/master
            GIT_SHALLOW TRUE
            OVERRIDE_FIND_PACKAGE
    )
    fetchcontent_makeavailable(Slag)
    find_package(Slag REQUIRED)
    SET(CRUCIBLE_RENDERING_SOURCES
            src/crucible/rendering/API_Rendering.cpp
            src/crucible/rendering/API_Rendering.h
            src/crucible/rendering/exceptions/ExceededHeapMemoryException.h
            src/crucible/rendering/core/Window.cpp
            src/crucible/rendering/core/Window.h
    )
endif ()

if(CRUCIBLE_SCRIPTING_MODULE)
    add_definitions(-DCRUCIBLE_SCRIPTING_MODULE=1)

    MACRO(SUBDIRLIST result curdir partial)
        FILE(GLOB children RELATIVE ${curdir} ${curdir}/${partial})
        SET(dirlist "")
        FOREACH(child ${children})
            IF(IS_DIRECTORY ${curdir}/${child})
                LIST(APPEND dirlist ${child})
            ENDIF()
        ENDFOREACH()
        SET(${result} ${dirlist})
    ENDMACRO()

    if(WIN32)
        SUBDIRLIST(DOTNETDIRS "C:/Program Files/dotnet/packs/Microsoft.NETCore.App.Host.win-x64/" "${CRUCIBLE_DOT_NET_VERSION}.*")
        list(GET DOTNETDIRS -1 DOTNETDIR)
        set(DOT_NET_HOST_LOCATION "C:/Program Files/dotnet/packs/Microsoft.NETCore.App.Host.win-x64/${DOTNETDIR}/runtimes/win-x64/native")
        set(NETHOST_DYNAMIC_LIBRARY "nethost.dll")
    else()
        SUBDIRLIST(DOTNETDIRS "/usr/lib/dotnet/packs/Microsoft.NETCore.App.Host.ubuntu.22.04-x64" "${CRUCIBLE_DOT_NET_VERSION}.*")
        list(GET DOTNETDIRS -1 DOTNETDIR)
        set(DOT_NET_HOST_LOCATION "/usr/lib/dotnet/packs/Microsoft.NETCore.App.Host.ubuntu.22.04-x64/${DOTNETDIR}/runtimes/ubuntu.22.04-x64/native/")
        set(NETHOST_DYNAMIC_LIBRARY "libnethost.so")
    endif()

    if(${DOTNETDIR} STREQUAL "NOTFOUND")
        message(FATAL_ERROR "Dot Net ${CRUCIBLE_DOT_NET_VERSION} is not installed")
    else ()
        message("Using Dot Net version found at ${DOT_NET_HOST_LOCATION}")
    endif ()


    SET(CRUCIBLE_SCRIPTING_SOURCES
            src/crucible/scripting/API_Scripting.cpp
            src/crucible/scripting/API_Scripting.h
            src/crucible/scripting/exceptions/InvalidManagedResourceException.cpp
            src/crucible/scripting/exceptions/InvalidManagedResourceException.h
            src/crucible/scripting/core/Callbacks.h
    )

endif ()

add_library(Crucible ${CRUCIBLE_RENDERING_SOURCES} ${CRUCIBLE_SCRIPTING_SOURCES}
        src/crucible/Crucible.cpp
        src/crucible/Crucible.h)

#Add SDL if any submodule depends on it
if(CRUCIBLE_INCLUDE_SDL)
    add_definitions(-DCRUCIBLE_INCLUDE_SDL=1)
    find_package(SDL3 QUIET)
    if(NOT SDL3_FOUND)
        fetchcontent_declare(
                SDL3
                GIT_REPOSITORY https://github.com/libsdl-org/SDL
                GIT_TAG release-3.4.14
                GIT_SHALLOW TRUE
                OVERRIDE_FIND_PACKAGE
        )
        FetchContent_MakeAvailable(SDL3)
        find_package(SDL3 REQUIRED)
    endif ()

    target_link_libraries(Crucible PUBLIC SDL3::SDL3-static)
endif ()

#set up library for consumption in other projects
target_include_directories(Crucible PUBLIC src)
add_library(crucible::Crucible ALIAS Crucible)

#-----------------------------------------------------------------------------------------------------------------------
# Modules
#-----------------------------------------------------------------------------------------------------------------------
if(CRUCIBLE_RENDERING_MODULE)
    target_link_libraries(Crucible PUBLIC slag::Slag)
endif ()

if(CRUCIBLE_SCRIPTING_MODULE)
    target_link_directories(Crucible PUBLIC ${DOT_NET_HOST_LOCATION})
    target_link_libraries(Crucible PUBLIC nethost)
    target_include_directories(Crucible PRIVATE ${DOT_NET_HOST_LOCATION})

    if(NOT CRUCIBLE_AOT_SCRIPTING)
        #copy over c# config file (configure_file() is the copy operation, not related to the "configure file" that we're copying)
        configure_file(src/crucible/scripting/config/CSharpConfig.json ${CMAKE_BINARY_DIR}/config/CSharpConfig.json COPYONLY)
        #copy over library that allows us to enter c# code
        configure_file(${DOT_NET_HOST_LOCATION}/${NETHOST_DYNAMIC_LIBRARY} ${CMAKE_BINARY_DIR}/${NETHOST_DYNAMIC_LIBRARY} COPYONLY)
    endif()
    #build c# dll
    file(GLOB_RECURSE CRUCIBLE_CSHARP_FILES CONFIGURE_DEPENDS ${CMAKE_CURRENT_SOURCE_DIR}/src/crucible/scripting/dotnet/Crucible-Runtime/*.cs)
    add_custom_command(
            OUTPUT ${CMAKE_BINARY_DIR}/Crucible-Runtime.dll
            WORKING_DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR}/src/crucible/scripting/dotnet/Crucible-Runtime
            COMMAND dotnet publish Crucible-Runtime /p:Configuration=${CRUCIBLE_BUILD_TYPE} /p:Platform="Any CPU" -o ${CMAKE_BINARY_DIR}
            DEPENDS ${CRUCIBLE_CSHARP_FILES}
            COMMENT "Building Crucible .net runtime"
    )
    add_custom_target(Crucible-Runtime ALL DEPENDS ${CMAKE_BINARY_DIR}/Crucible-Runtime.dll)
    add_dependencies(Crucible Crucible-Runtime)

endif ()

#-----------------------------------------------------------------------------------------------------------------------
# Tests
#-----------------------------------------------------------------------------------------------------------------------
if(CRUCIBLE_BUILD_TESTS)
    MESSAGE("Building Crucible Tests")
    FetchContent_Declare( googletest
            GIT_REPOSITORY https://github.com/google/googletest
            GIT_TAG v1.17.0
            GIT_SHALLOW TRUE
            OVERRIDE_FIND_PACKAGE)
    set(gtest_force_shared_crt ON CACHE BOOL "" FORCE)
    add_definitions(-Dgtest_disable_pthreads=ON)
    FetchContent_MakeAvailable(googletest)

    enable_testing()

    add_executable(CrucibleTests
            tests/CrucibleTestsMain.cpp
            tests/tests/RenderingModuleTests.cpp
            tests/tests/ScriptingModuleTests.cpp)

    target_link_libraries(CrucibleTests GTest::gtest crucible::Crucible)

    if(CRUCIBLE_SCRIPTING_MODULE)

        if(WIN32)
            SET(CRUCIBLE_NATIVE_TARGET win-x64)
        else()
            SET(CRUCIBLE_NATIVE_TARGET linux-x64)
        endif()

        if(CRUCIBLE_AOT_SCRIPTING)
            SET(CRUCIBLE_PUBLISH_AOT "/p:PublishAot=true")
        endif()

        file(GLOB_RECURSE CRUCIBLE_CSHARP_TEST_FILES CONFIGURE_DEPENDS ${CMAKE_CURRENT_SOURCE_DIR}/src/crucible/scripting/dotnet/Crucible-Runtime-Tests/*.cs)
        add_custom_command(
                OUTPUT ${CMAKE_BINARY_DIR}/Crucible-Runtime-Tests.dll
                WORKING_DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR}/src/crucible/scripting/dotnet/Crucible-Runtime
                COMMAND dotnet publish Crucible-Runtime-Tests -r ${CRUCIBLE_NATIVE_TARGET} /p:Configuration=${CRUCIBLE_BUILD_TYPE} /p:Platform="Any CPU" ${CRUCIBLE_PUBLISH_AOT} -o ${CMAKE_BINARY_DIR}
                DEPENDS ${CRUCIBLE_CSHARP_TEST_FILES} ${CRUCIBLE_CSHARP_FILES}
                COMMENT "Building Crucible .net tests"
        )
        add_custom_target(Crucible-Runtime-Tests ALL DEPENDS ${CMAKE_BINARY_DIR}/Crucible-Runtime-Tests.dll)
        add_dependencies(Crucible-Runtime-Tests Crucible-Runtime)
        add_dependencies(CrucibleTests Crucible-Runtime-Tests)
    endif()
endif()