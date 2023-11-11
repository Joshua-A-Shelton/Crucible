cmake_minimum_required(VERSION 3.24)
project(Crucible)

set(CMAKE_CXX_STANDARD 20)

find_package(SDL2 REQUIRED)
find_package(Vulkan REQUIRED)
find_package(Boost REQUIRED COMPONENTS filesystem)

add_subdirectory(third-party/slag)
add_subdirectory(third-party/entt)

if(WIN32)
    set(CRUCIBLE_PLATFORM_DIR "Windows")
    set(CRUCIBLE_DYN_EXT ".dll")
elseif(UNIX)
    set(CRUCIBLE_PLATFORM_DIR "Linux")
    set(CRUCIBLE_DYN_EXT ".so")
endif()
if(CMAKE_BUILD_TYPE STREQUAL "Debug")
    set(CRUCIBLE_BUILD_TYPE "Debug")
else()
    set(CRUCIBLE_BUILD_TYPE "Release")
endif()

link_directories(third-party/nethost/lib/${CRUCIBLE_PLATFORM_DIR})

list(APPEND CRUCIBLE_DEAR_IMGUI third-party/dear-imgui/imgui.cpp)
list(APPEND CRUCIBLE_DEAR_IMGUI third-party/dear-imgui/imgui_draw.cpp)
list(APPEND CRUCIBLE_DEAR_IMGUI third-party/dear-imgui/imgui_tables.cpp)
list(APPEND CRUCIBLE_DEAR_IMGUI third-party/dear-imgui/imgui_widgets.cpp)
list(APPEND CRUCIBLE_DEAR_IMGUI third-party/dear-imgui/backends/imgui_impl_sdl2.cpp)
list(APPEND CRUCIBLE_DEAR_IMGUI third-party/dear-imgui/backends/imgui_impl_vulkan.cpp)


add_library(Crucible src/crucible/Engine.cpp src/crucible/Engine.h src/crucible/Graphics/Model.cpp src/crucible/Graphics/Model.h src/crucible/Graphics/Mesh.cpp src/crucible/Graphics/Mesh.h src/crucible/Graphics/Vertex3D.h src/crucible/GameObject.cpp src/crucible/GameObject.h src/crucible/EntityComponentSystem.cpp src/crucible/EntityComponentSystem.h src/crucible/Scripting/ScriptingEngine.cpp src/crucible/Scripting/ScriptingEngine.h src/crucible/Scripting/Interop.cpp src/crucible/Scripting/Interop.h src/crucible/Scripting/ManagedObjectHandle.h src/crucible/Scripting/ManagedObjectHandle.cpp)
add_executable(CrucibleEditor ${CRUCIBLE_DEAR_IMGUI} editor/Program.cpp)

target_include_directories(Crucible PUBLIC third-party/glm third-party/nethost/include ${SDL2_LIBRARIES} ${Boost_INCLUDE_DIRS})
target_include_directories(CrucibleEditor PRIVATE third-party/dear-imgui ${Vulkan_INCLUDE_DIR} src)


target_link_libraries(Crucible Slag SDL2::SDL2 EnTT::EnTT nethost ${Boost_LIBRARIES})

target_link_libraries(CrucibleEditor Crucible)


#==============================================================================
#Compile dotnet
#
set(DOTNET_OUTPUT_TARGET ${CMAKE_CURRENT_BINARY_DIR}/Crucible.dll)
file(GLOB_RECURSE DOTNETRUNTIME CONFIGURE_DEPENDS dotnet/Crucible/*.cs)
add_custom_target(dotnetruntime-build
        DEPENDS ${DOTNETRUNTIME}
        WORKING_DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR}/dotnet/Crucible
        COMMAND dotnet build -c ${CRUCIBLE_BUILD_TYPE} -p 'AnyCPU'
        COMMAND ${CMAKE_COMMAND} -E copy ${CMAKE_SOURCE_DIR}/dotnet/Crucible/Crucible/bin/${CRUCIBLE_BUILD_TYPE}/net7.0/Crucible.dll ${DOTNET_OUTPUT_TARGET}
        COMMENT "Building .net runtime")
ADD_DEPENDENCIES(Crucible dotnetruntime-build)


#==============================================================================
# COMPILE SHADERS
#

set(SHADER_SOURCE_DIR ${CMAKE_CURRENT_SOURCE_DIR}/editor/Graphics/DefaultShaders)
set(SHADER_BINARY_DIR ${CMAKE_CURRENT_BINARY_DIR}/Graphics/DefaultShaders)

file(GLOB SHADERS
        ${SHADER_SOURCE_DIR}/*.vert
        ${SHADER_SOURCE_DIR}/*.frag)

add_custom_command(
        COMMAND
        ${CMAKE_COMMAND} -E make_directory ${SHADER_BINARY_DIR}
        OUTPUT ${SHADER_BINARY_DIR}
        COMMENT "Creating ${SHADER_BINARY_DIR}"
)

foreach(source IN LISTS SHADERS)
    get_filename_component(FILENAME ${source} NAME)
    add_custom_command(
            COMMAND
            ${glslc_executable}
            #      -MD -MF ${SHADER_BINARY_DIR}/${FILENAME}.d
            -o ${SHADER_BINARY_DIR}/${FILENAME}.spv
            ${source}
            OUTPUT ${SHADER_BINARY_DIR}/${FILENAME}.spv
            DEPENDS ${source} ${SHADER_BINARY_DIR}
            COMMENT "Compiling ${FILENAME}"
    )
    list(APPEND SPV_SHADERS ${SHADER_BINARY_DIR}/${FILENAME}.spv)
endforeach()

add_custom_target(shaders ALL DEPENDS ${SPV_SHADERS})

if(WIN32)
    file(COPY third-party/nethost/shared/${CRUCIBLE_PLATFORM_DIR}/nethost${CRUCIBLE_DYN_EXT} DESTINATION ${CMAKE_CURRENT_BINARY_DIR})
endif()
    file(COPY config DESTINATION ${CMAKE_CURRENT_BINARY_DIR})