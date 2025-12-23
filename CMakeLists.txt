cmake_minimum_required(VERSION 3.29)
project(Crucible)

set(CMAKE_CXX_STANDARD 20)
include(FetchContent)
cmake_policy(SET CMP0167 NEW)

SET(SLAG_LIBRARY_VERSION "d6f32c4")
SET(SDL3_LIBRARY_VERSION "release-3.2.24")
SET(GLM_LIBRARY_VERSION "1.0.2")
SET(LZ4_LIBRARY_VERSION "v1.10.0")
SET(BOOST_LIBRARY_VERSION "boost-1.84.0")
SET(FLECS_LIBRARY_VERSION "v4.1.2")

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
        GIT_TAG ${SDL3_LIBRARY_VERSION}
        GIT_SHALLOW TRUE
        OVERRIDE_FIND_PACKAGE
)
fetchcontent_makeavailable(SDL3)
if(NOT WIN32)
    target_compile_options(SDL3-static PUBLIC -fPIC)
endif ()
find_package(SDL3 REQUIRED)

fetchcontent_declare(
        glm
        GIT_REPOSITORY https://github.com/g-truc/glm
        GIT_TAG ${GLM_LIBRARY_VERSION}
        GIT_SHALLOW TRUE
        OVERRIDE_FIND_PACKAGE
)
fetchcontent_makeavailable(glm)
find_package(glm)

set(LZ4_BUNDLED_MODE ON)
fetchcontent_declare(
        lz4
        GIT_REPOSITORY https://github.com/lz4/lz4
        GIT_TAG ${LZ4_LIBRARY_VERSION}
        GIT_SHALLOW TRUE
        OVERRIDE_FIND_PACKAGE
)
FetchContent_MakeAvailable(lz4)
find_package(lz4)
add_subdirectory(${lz4_SOURCE_DIR}/build/cmake)

fetchcontent_declare(
        STB_IMAGE
        GIT_REPOSITORY https://github.com/nothings/stb
        GIT_TAG origin/master
        GIT_SHALLOW TRUE
        OVERRIDE_FIND_PACKAGE
)
FetchContent_MakeAvailable(STB_IMAGE)
find_package(STB_IMAGE)

fetchcontent_declare(
        Boost
        URL https://github.com/boostorg/boost/releases/download/${BOOST_LIBRARY_VERSION}/${BOOST_LIBRARY_VERSION}.7z
        USES_TERMINAL_DOWNLOAD TRUE
        GIT_PROGRESS TRUE
        DOWNLOAD_NO_EXTRACT FALSE
        OVERRIDE_FIND_PACKAGE
)
FetchContent_MakeAvailable(Boost)
find_package(Boost REQUIRED COMPONENTS uuid)


fetchcontent_declare(
        flecs
        GIT_REPOSITORY https://github.com/SanderMertens/flecs
        GIT_TAG ${FLECS_LIBRARY_VERSION}
        GIT_SHALLOW TRUE
        OVERRIDE_FIND_PACKAGE
)
FetchContent_MakeAvailable(flecs)
find_package(flecs)

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
        src/crucible/scripting/CSharpFunctionPointers.h
        src/crucible/scripting/ManagedType.cpp
        src/crucible/scripting/ManagedType.h
        src/crucible/scripting/BindingFlags.h
        src/crucible/scripting/ManagedFunctionDelegate.cpp
        src/crucible/scripting/ManagedFunctionDelegate.h
        src/crucible/Game.cpp
        src/crucible/Game.h
        src/crucible/scripting/ManagedInstance.cpp
        src/crucible/scripting/ManagedInstance.h
        src/crucible/scripting/API.cpp
        src/crucible/scripting/API.h
        src/crucible/serialization/BufferUtils.h
        src/crucible/Node.cpp
        src/crucible/Node.h
        src/crucible/ecs/World.cpp
        src/crucible/ecs/World.h
        src/crucible/Transform.cpp
        src/crucible/Transform.h
        src/crucible/Mesh.cpp
        src/crucible/Mesh.h
        src/crucible/BoneWeights.h
        src/crucible/DeferredJobQueue.cpp
        src/crucible/DeferredJobQueue.h
        src/crucible/utils/GPUUtils.cpp
        src/crucible/utils/GPUUtils.h
        src/crucible/Material.cpp
        src/crucible/Material.h
        src/crucible/ShaderManager.cpp
        src/crucible/ShaderManager.h
)

target_link_directories(Crucible PUBLIC ${DOT_NET_HOST_LOCATION})
target_link_libraries(Crucible PUBLIC glm Slag SDL3::SDL3-static flecs::flecs_static lz4 Boost::endian Boost::uuid PRIVATE nethost)
target_include_directories(Crucible PRIVATE ${DOT_NET_HOST_LOCATION} ${stb_image_SOURCE_DIR} ${lz4_SOURCE_DIR}/lib ${flecs_INCLUDE_DIRS})

#copy over c# config file (configure_file() is the copy operation, not related to the "configure file" that we're copying)
configure_file(config/CSharpConfig.json "${CMAKE_BINARY_DIR}/config/CSharpConfig.json" COPYONLY)
#copy over library that allows us to enter c# code
configure_file("${DOT_NET_HOST_LOCATION}/${NETHOST_DYNAMIC_LIBRARY}" "${CMAKE_BINARY_DIR}/${NETHOST_DYNAMIC_LIBRARY}" COPYONLY)
#build c# dll
file(GLOB_RECURSE CRUCIBLE_CSHARP_FILES CONFIGURE_DEPENDS ${CMAKE_CURRENT_SOURCE_DIR}/dotnet/Crucible-Runtime/Crucible-Runtime/*.cs)
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
            tests/tests/ScriptingEngineTest.cpp
            tests/tests/ManagedInstanceTest.cpp
            tests/tests/API_Test.cpp
            tests/tests/MeshTest.cpp
            tests/tests/TextureTest.cpp
    )

    target_link_libraries(CrucibleTests Crucible GTest::gtest)
    include(GoogleTest)
    gtest_discover_tests(CrucibleTests PROPERTIES DISCOVERY_TIMEOUT 600 DISCOVERY_MODE PRE_TEST)

    file(GLOB_RECURSE CRUCIBLE_TESTS_CHARP_FILES CONFIGURE_DEPENDS ${CMAKE_CURRENT_SOURCE_DIR}/dotnet/Crucible-Runtime/Crucible-Runtime-Tests/*.cs)
    add_custom_command(
            OUTPUT ${CMAKE_BINARY_DIR}/Crucible-Runtime-Tests.dll
            WORKING_DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR}/dotnet/Crucible-Runtime
            COMMAND dotnet publish Crucible-Runtime-Tests /p:Configuration=${CRUCIBLE_BUILD_TYPE} /p:Platform="Any CPU" /p:AllowUnsafeBlocks=true -o ${CMAKE_BINARY_DIR}
            DEPENDS ${CRUCIBLE_TESTS_CHARP_FILES}
            COMMENT "Building Crucible .net runtime tests"
    )
    add_custom_target(Crucible-Runtime-Tests DEPENDS ${CMAKE_BINARY_DIR}/Crucible-Runtime-Tests.dll)
    add_dependencies(CrucibleTests Crucible-Runtime-Tests)
    add_dependencies(Crucible-Runtime-Tests Crucible-Runtime)

    configure_file(${CMAKE_CURRENT_SOURCE_DIR}/Crucible.png ${CMAKE_BINARY_DIR}/Crucible.png COPYONLY)
    add_custom_command(TARGET CrucibleTests POST_BUILD COMMAND ${CMAKE_COMMAND} -E copy_directory ${CMAKE_CURRENT_SOURCE_DIR}/tests/resources ${CMAKE_BINARY_DIR}/resources)
endif ()

target_include_directories(Crucible PUBLIC src)