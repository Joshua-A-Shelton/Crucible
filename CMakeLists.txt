cmake_minimum_required(VERSION 3.24)
project(Crucible)

set(CMAKE_CXX_STANDARD 20)

find_package(SDL2 REQUIRED)
find_package(unofficial-nethost REQUIRED)
find_package(Vulkan REQUIRED)

add_subdirectory(third-party/slag)
add_subdirectory(third-party/entt)

if(WIN32)
    set(CRUCIBLE_PLATFORM_DIR "Windows")
    set(CRUCIBLE_DYN_EXT ".dll")
elseif()
    set(CRUCIBLE_PLATFORM_DIR "Linux")
    set(CRUCIBLE_DYN_EXT ".so")
endif()
link_directories(third-party/nethost/lib/${CRUCIBLE_PLATFORM_DIR})

list(APPEND CRUCIBLE_DEAR_IMGUI third-party/dear-imgui/imgui.cpp)
list(APPEND CRUCIBLE_DEAR_IMGUI third-party/dear-imgui/imgui_demo.cpp)
list(APPEND CRUCIBLE_DEAR_IMGUI third-party/dear-imgui/imgui_draw.cpp)
list(APPEND CRUCIBLE_DEAR_IMGUI third-party/dear-imgui/imgui_tables.cpp)
list(APPEND CRUCIBLE_DEAR_IMGUI third-party/dear-imgui/imgui_widgets.cpp)
list(APPEND CRUCIBLE_DEAR_IMGUI third-party/dear-imgui/backends/imgui_impl_sdl2.cpp)
list(APPEND CRUCIBLE_DEAR_IMGUI third-party/dear-imgui/backends/imgui_impl_vulkan.cpp)


add_library(Crucible src/crucible/Engine.cpp src/crucible/Engine.h src/crucible/Graphics/Model.cpp src/crucible/Graphics/Model.h src/crucible/Graphics/Mesh.cpp src/crucible/Graphics/Mesh.h src/crucible/Graphics/Vertex3D.h src/crucible/GameObject.cpp src/crucible/GameObject.h src/crucible/EntityComponentSystem.cpp src/crucible/EntityComponentSystem.h src/crucible/Scripting/ScriptingEngine.cpp src/crucible/Scripting/ScriptingEngine.h)
add_executable(CrucibleEditor ${CRUCIBLE_DEAR_IMGUI} editor/Program.cpp)

target_include_directories(Crucible PUBLIC third-party/glm third-party/nethost ${SDL2_LIBRARIES})
target_include_directories(CrucibleEditor PRIVATE third-party/dear-imgui ${Vulkan_INCLUDE_DIR} src)


target_link_libraries(Crucible Slag SDL2::SDL2 EnTT::EnTT nethost)

target_link_libraries(CrucibleEditor Crucible)


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

file(COPY third-party/nethost/shared/${CRUCIBLE_PLATFORM_DIR}/nethost${CRUCIBLE_DYN_EXT} DESTINATION ${CMAKE_CURRENT_BINARY_DIR})