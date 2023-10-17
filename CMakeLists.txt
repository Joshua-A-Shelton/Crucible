cmake_minimum_required(VERSION 3.24)
project(Crucible)

set(CMAKE_CXX_STANDARD 20)
find_package(SDL2 REQUIRED)

add_subdirectory(third-party/slag)
add_subdirectory(third-party/entt)

add_library(Crucible src/crucible/Engine.cpp src/crucible/Engine.h src/crucible/Graphics/Model.cpp src/crucible/Graphics/Model.h src/crucible/Graphics/Mesh.cpp src/crucible/Graphics/Mesh.h src/crucible/Graphics/Vertex3D.h src/crucible/GameObject.cpp src/crucible/GameObject.h src/crucible/EntityComponentSystem.cpp src/crucible/EntityComponentSystem.h)
add_executable(CrucibleEditor editor/Program.cpp)

target_include_directories(Crucible PUBLIC third-party/glm third-party/dear-imgui ${SDL2_LIBRARIES})
target_include_directories(CrucibleEditor PRIVATE src)


target_link_libraries(Crucible Slag SDL2::SDL2 EnTT::EnTT)

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


