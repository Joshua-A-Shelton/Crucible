cmake_minimum_required(VERSION 3.24)
project(crucible)

set(CMAKE_CXX_STANDARD 20)

set(SDL_STATIC ON)
set(SDL_SHARED OFF)
add_subdirectory(third-party/sdl)
add_subdirectory(third-party/slag)
add_subdirectory(third-party/glm)

add_library(crucible-lib src/Window.cpp src/Window.h src/CrucibleLib.cpp src/CrucibleLib.h src/Vertex.cpp src/Vertex.h src/Mesh.cpp src/Mesh.h)
add_executable(crucible editor/main.cpp)
target_link_libraries(crucible-lib slag SDL3::SDL3)
target_link_libraries(crucible crucible-lib)

target_include_directories(crucible-lib PUBLIC third-party/glm third-party/sdl/include)
target_include_directories(crucible PRIVATE src)




#compile shaders
if (${CMAKE_HOST_SYSTEM_PROCESSOR} STREQUAL "AMD64")
    set(GLSL_VALIDATOR "glslc")
else()
    set(GLSL_VALIDATOR "glslc")
endif()

file(GLOB_RECURSE GLSL_SOURCE_FILES
        "assets/shaders/*.frag"
        "assets/shaders/*.vert"
        )

foreach(GLSL ${GLSL_SOURCE_FILES})
    get_filename_component(FILE_NAME ${GLSL} NAME)
    set(SPIRV "${PROJECT_BINARY_DIR}/assets/shaders/${FILE_NAME}.spv")
    add_custom_command(
            OUTPUT ${SPIRV}
            COMMAND ${CMAKE_COMMAND} -E make_directory "${PROJECT_BINARY_DIR}/shaders/"
            COMMAND ${GLSL_VALIDATOR} ${GLSL} -o ${SPIRV}
            DEPENDS ${GLSL})
    list(APPEND SPIRV_BINARY_FILES ${SPIRV})
endforeach(GLSL)

add_custom_target(
        Shaders
        DEPENDS ${SPIRV_BINARY_FILES}
)

add_dependencies(crucible Shaders)

add_custom_command(TARGET crucible POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E make_directory "$<TARGET_FILE_DIR:crucible>/assets/shaders/"
        COMMAND ${CMAKE_COMMAND} -E copy_directory
        "${PROJECT_BINARY_DIR}/assets/shaders/"
        "$<TARGET_FILE_DIR:crucible>/assets/shaders/"
        )