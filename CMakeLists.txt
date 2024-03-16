cmake_minimum_required(VERSION 3.24)
project(Crucible)

set(CMAKE_CXX_STANDARD 20)

find_package(SDL2 REQUIRED)
find_package(Boost REQUIRED COMPONENTS filesystem)

add_subdirectory(third-party/slag)

if(WIN32)
    set(CRUCIBLE_PLATFORM_DIR "Windows")
    set(CRUCIBLE_DYN_EXT ".dll")
    set(NETHOST_LOCATION third-party/nethost/lib/${CRUCIBLE_PLATFORM_DIR})
elseif(UNIX)
    set(CRUCIBLE_PLATFORM_DIR "Linux")
    set(CRUCIBLE_DYN_EXT ".so")
    set(NETHOST_LOCATION /usr/lib/dotnet/packs/Microsoft.NETCore.App.Host.ubuntu.22.04-x64/7.0.16/runtimes/ubuntu.22.04-x64/native/)
endif()
if(CMAKE_BUILD_TYPE STREQUAL "Debug")
    set(CRUCIBLE_BUILD_TYPE "Debug")
else()
    set(CRUCIBLE_BUILD_TYPE "Release")
endif()

#TODO: make this work as shared library
add_library(Crucible src/crucible/Engine.cpp src/crucible/Engine.h src/crucible/Graphics/Model.cpp src/crucible/Graphics/Model.h src/crucible/Graphics/Mesh.cpp src/crucible/Graphics/Mesh.h src/crucible/Graphics/Vertex3D.h src/crucible/Scripting/ScriptingEngine.cpp src/crucible/Scripting/ScriptingEngine.h src/crucible/Scripting/Interop.cpp src/crucible/Scripting/Interop.h src/crucible/Scripting/ManagedObjectGCHandle.h src/crucible/Scripting/ManagedObjectGCHandle.cpp src/crucible/Scripting/ManagedType.cpp src/crucible/Scripting/ManagedType.h src/crucible/Crucible.h src/crucible/Graphics/RenderRegistry.cpp src/crucible/Graphics/RenderRegistry.h src/crucible/Game.cpp src/crucible/Game.h src/crucible/Graphics/CameraViewData.h src/crucible/Scripting/GameWorld.cpp src/crucible/Scripting/GameWorld.h src/crucible/Graphics/CameraViewData.cpp src/crucible/Graphics/Vertex2D.h)
target_include_directories(Crucible PUBLIC third-party/glm third-party/nethost/include ${SDL2_LIBRARIES} ${Boost_INCLUDE_DIRS})
target_link_directories(Crucible PUBLIC ${NETHOST_LOCATION})
target_link_libraries(Crucible Slag SDL2::SDL2 nethost ${Boost_LIBRARIES})


#==============================================================================
#Compile dotnet
#
file(GLOB_RECURSE DOTNETRUNTIME CONFIGURE_DEPENDS dotnet/Crucible/*.cs)
add_custom_target(dotnetruntime-build
        DEPENDS ${DOTNETRUNTIME}
        WORKING_DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR}/dotnet/Crucible/Crucible-Runtime
        COMMAND dotnet publish -c ${CRUCIBLE_BUILD_TYPE} -o ${CMAKE_BINARY_DIR}
        COMMENT "Building .net runtime")
ADD_DEPENDENCIES(Crucible dotnetruntime-build)

if(WIN32)
    file(COPY third-party/nethost/shared/${CRUCIBLE_PLATFORM_DIR}/nethost${CRUCIBLE_DYN_EXT} DESTINATION ${CMAKE_BINARY_DIR})
endif()

# Copy over resources
file(COPY config DESTINATION ${CMAKE_BINARY_DIR})

target_include_directories(Crucible PUBLIC src)
