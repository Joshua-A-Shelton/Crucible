cmake_minimum_required(VERSION 3.24)
project(Crucible)

set(CMAKE_CXX_STANDARD 17)
find_package(SDL2 REQUIRED)

add_subdirectory(third-party/slag)

add_library(Crucible src/crucible/Window.cpp src/crucible/Window.h src/crucible/Engine.cpp src/crucible/Engine.h src/crucible/Graphics/Model.cpp src/crucible/Graphics/Model.h src/crucible/Graphics/Mesh.cpp src/crucible/Graphics/Mesh.h)
add_executable(CrucibleEditor editor/Program.cpp)

target_include_directories(Crucible PUBLIC third-party/glm PRIVATE ${SDL2_LIBRARIES})
target_include_directories(CrucibleEditor PRIVATE src)


target_link_libraries(Crucible Slag SDL2::SDL2)

target_link_libraries(CrucibleEditor Crucible)


