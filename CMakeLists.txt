cmake_minimum_required(VERSION 3.24)
project(Crucible)

set(CMAKE_CXX_STANDARD 17)
find_package(SDL2 REQUIRED)

add_subdirectory(third-party/slag)
add_library(Crucible src/crucible/Window.cpp src/crucible/Window.h src/crucible/Engine.cpp src/crucible/Engine.h src/crucible/Graphics/Model.cpp src/crucible/Graphics/Model.h src/crucible/Graphics/Mesh.cpp src/crucible/Graphics/Mesh.h src/crucible/Graphics/Vertex3D.h src/crucible/Graphics/dear-imgui/imgui_impl_slag.cpp src/crucible/Graphics/dear-imgui/imgui_impl_slag.h)
add_executable(CrucibleEditor editor/Program.cpp)

target_include_directories(Crucible PUBLIC third-party/glm third-party/dear-imgui PRIVATE ${SDL2_LIBRARIES})
target_include_directories(CrucibleEditor PRIVATE src)


target_link_libraries(Crucible Slag SDL2::SDL2)

target_link_libraries(CrucibleEditor Crucible)


