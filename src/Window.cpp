#include "Window.h"
#include <slag/SlagLib.h>
#include <slag/Swapchain.h>
#include <SDL2/SDL_vulkan.h>
#include <iostream>


namespace crucible
{
    Window::Window(int width, int height, const char* title)
    {
        SDL_WindowFlags flags = static_cast<SDL_WindowFlags>(SDL_WINDOW_VULKAN | SDL_WINDOW_RESIZABLE);
        _handle = SDL_CreateWindow(title,SDL_WINDOWPOS_UNDEFINED,SDL_WINDOWPOS_UNDEFINED, width, height,flags);
        SDL_Vulkan_CreateSurface(_handle,slag::SlagLib::instance(),&_surface);
        _swapchain = new slag::Swapchain(_surface,width,height,slag::RenderMode::VSYNC);
        _swapchain->clearColor = {0,0,0,1};
        auto description = _swapchain->currentFramebuffer().description();
        _testShader = slag::Shader::Create("assets/shaders/triangle.vert.spv","assets/shaders/triangle.frag.spv",description,Vertex::description());
        std::vector<Vertex> verts(3);
        //vertex positions
        verts[0].position = { 1.f, 1.f, 0.0f };
        verts[1].position = {-1.f, 1.f, 0.0f };
        verts[2].position = { 0.f,-1.f, 0.0f };

        verts[0].normal = {.5,.5,.5};
        verts[1].normal = {.5,.5,.5};
        verts[2].normal = {.5,.5,.5};


        //vertex colors, all green
        verts[0].color = { 1.f, 0.f, 0.0f }; //pure green
        verts[1].color = { 0.f, 1.f, 0.0f }; //pure green
        verts[2].color = { 0.f, 0.f, 1.0f }; //pure green

        verts[0].uv = {.5,.5};
        verts[1].uv = {.5,.5};
        verts[2].uv = {.5,.5};


        _testMesh = new Mesh(verts);
    }

    Window::~Window()
    {
        vkDeviceWaitIdle(slag::SlagLib::graphicsCard()->device());
        delete _testMesh;
        _testShader = nullptr;
        delete _swapchain;
        vkDestroySurfaceKHR(slag::SlagLib::instance(),_surface, nullptr);
        SDL_DestroyWindow(_handle);
    }

    void Window::show()
    {
        SDL_Event e;
        bool quit = false;

        while (!quit)
        {
            //Handle events on queue
            while (SDL_PollEvent(&e) != 0)
            {
                switch (e.type)
                {
                    case SDL_QUIT:
                        quit = true;
                        break;
                    case SDL_WINDOWEVENT:
                        switch (e.window.event)
                        {
                            case SDL_WINDOWEVENT_SIZE_CHANGED:
                                resize();
                                break;
                        }
                        break;
                }

            }

            draw();
        }
    }

    void Window::draw()
    {
        if(auto frame = _swapchain->next())
        {
            frame->begin();
            auto cmd = frame->commandBuffer();
            std::vector<slag::FramebufferAttachment> attachments;
            auto defaultAttachment = frame->swapchainColorAttachment();
            attachments.push_back(defaultAttachment);
            int w,h;
            SDL_GetWindowSize(_handle,&w,&h);
            slag::Framebuffer defaultFramebuffer(attachments,w,h);
            cmd.setTargetFramebuffer(defaultFramebuffer);
            cmd.useShader(_testShader);
            cmd.bindVertexBuffer(_testMesh->vertexBuffer());
            cmd.draw(3,1,0,0);
            cmd.endTargetFramebuffer();
            frame->end();
        }
    }

    void Window::resize()
    {
        int w,h;
        SDL_GetWindowSize(_handle,&w,&h);
        _swapchain->resize(w,h);
    }
} // crucible