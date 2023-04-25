#ifndef CRUCIBLE_WINDOW_H
#define CRUCIBLE_WINDOW_H
#include <slag/Swapchain.h>
#include <SDL3/SDL.h>
#include <slag/Shader.h>
#include "Vertex.h"
#include "Mesh.h"

namespace crucible
{
    class Window
    {
    private:
        SDL_Window* _handle = nullptr;
        slag::Swapchain* _swapchain = nullptr;
        VkSurfaceKHR _surface = nullptr;
        std::shared_ptr<slag::Shader> _testShader;
        Mesh* _testMesh = nullptr;
        void draw();
        void resize();
    public:
        Window()=delete;
        Window(const Window&)=delete;
        Window& operator=(const Window&)=delete;
        Window(int width, int height, const char* title);
        ~Window();
        void show();
    };
} // crucible
#endif //CRUCIBLE_WINDOW_H