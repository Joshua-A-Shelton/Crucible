cmake_minimum_required(VERSION 3.24)
project(Crucible)

set(CMAKE_CXX_STANDARD 20)
find_package(SDL2 REQUIRED)
find_package(Boost REQUIRED COMPONENTS filesystem)

add_subdirectory(third-party/slag)
add_subdirectory(third-party/entt)
#get mono details
set(MONO_BIN_FOLDER "${CMAKE_CURRENT_SOURCE_DIR}/third-party/mono/bin")
if(CMAKE_BUILD_TYPE STREQUAL "Debug")
    set(MONO_BUILD_FOLDER "debug")
elseif(CMAKE_BUILD_TYPE STREQUAL "Release")
    set(MONO_BUILD_FOLDER "release")
endif()

if(WIN32)
    set(MONO_PLATFORM_FOLDER "Windows")

    set(MONO_DIR ${MONO_BIN_FOLDER}/${MONO_BUILD_FOLDER}/${MONO_PLATFORM_FOLDER})
    list(APPEND MONO_LIBS ${MONO_DIR}/eglib.lib)
    list(APPEND MONO_LIBS ${MONO_DIR}/libgcmonosgen.lib)
    list(APPEND MONO_LIBS ${MONO_DIR}/libmini-sgen.lib)
    list(APPEND MONO_LIBS ${MONO_DIR}/libmono-static-sgen.lib)
    list(APPEND MONO_LIBS ${MONO_DIR}/libmonoruntime-sgen.lib)
    list(APPEND MONO_LIBS ${MONO_DIR}/libmonoutils.lib)
    list(APPEND MONO_LIBS ${MONO_DIR}/mono-2.0-sgen.lib)
    list(APPEND MONO_LIBS ${MONO_DIR}/MonoPosixHelper.lib)
    list(APPEND MONO_LIBS version)
    list(APPEND MONO_LIBS Ws2_32)
    list(APPEND MONO_LIBS bcrypt)
    list(APPEND MONO_LIBS winmm)

    file(COPY third-party/mono/bin/${MONO_BUILD_FOLDER}/Windows/mono-2.0-sgen.dll DESTINATION ${CMAKE_CURRENT_BINARY_DIR})
    file(COPY third-party/mono/bin/${MONO_BUILD_FOLDER}/Windows/MonoPosixHelper.dll DESTINATION ${CMAKE_CURRENT_BINARY_DIR})
elseif(LINUX)
    set(MONO_PLATFORM_FOLDER "Linux")
endif()

message("using mono folder ${MONO_DIR}")





add_library(Crucible src/crucible/Engine.cpp src/crucible/Engine.h src/crucible/Graphics/Model.cpp src/crucible/Graphics/Model.h src/crucible/Graphics/Mesh.cpp src/crucible/Graphics/Mesh.h src/crucible/Graphics/Vertex3D.h src/crucible/GameObject.cpp src/crucible/GameObject.h src/crucible/EntityComponentSystem.cpp src/crucible/EntityComponentSystem.h)
add_executable(CrucibleEditor editor/Program.cpp)

target_include_directories(Crucible PUBLIC third-party/glm third-party/dear-imgui third-party/mono ${SDL2_LIBRARIES} ${Boost_INCLUDE_DIR})
target_include_directories(CrucibleEditor PRIVATE src)


target_link_libraries(Crucible Slag SDL2::SDL2 EnTT::EnTT ${MONO_LIBS} Boost::boost Boost::filesystem)

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



#===================================================
#Copy mono runtime libs
#
file(COPY third-party/mono/lib/4.5 DESTINATION ${CMAKE_CURRENT_BINARY_DIR}/mono)


