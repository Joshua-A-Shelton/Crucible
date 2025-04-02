#include "Game.h"
#include <stb_image.h>

#include "core/Camera.h"
#include "core/Mesh.h"
#include "core/VirtualUniformBuffer.h"
#include "core/scenes/World.h"
#include "scripting/ScriptingEngine.h"

namespace crucible
{
    namespace core
    {
        class World;
    }

    class CrucibleFrameResources: public slag::FrameResources
    {
    public:
        slag::CommandBuffer* commandBuffer = nullptr;
        slag::DescriptorPool* descriptorPool = nullptr;
        core::VirtualUniformBuffer* uniformBuffer = nullptr;
        CrucibleFrameResources()
        {
            commandBuffer = slag::CommandBuffer::newCommandBuffer(slag::GpuQueue::GRAPHICS);
            descriptorPool = slag::DescriptorPool::newDescriptorPool();
            uniformBuffer = new core::VirtualUniformBuffer(640000);
        }
        ~CrucibleFrameResources()override
        {
            delete commandBuffer;
            delete descriptorPool;
            delete uniformBuffer;
        }
        void waitForResourcesToFinish()override
        {
            commandBuffer->waitUntilFinished();
        }
        bool isFinished()override
        {
            return commandBuffer->isFinished();
        }
    };

    slag::FrameResources* CRUCIBLE_GAME_RESOURCE_CREATE(size_t, slag::Swapchain*)
    {
        return new CrucibleFrameResources();
    }

    Game::Game(const std::string& name, const std::filesystem::path& icon, const std::filesystem::path& gameDll,  SDL_Window* inWindow)
    {
        if(inWindow == nullptr)
        {
            _windowManaged = true;
            SDL_WindowFlags flags = SDL_WINDOW_RESIZABLE | SDL_WINDOW_MAXIMIZED;
            if(slag::SlagLib::usingBackEnd() == slag::VULKAN)
            {
                flags |= SDL_WINDOW_VULKAN;
            }
            _window = SDL_CreateWindow(name.c_str(),500,500,flags);
        }
        else
        {
            _windowManaged = false;
            _window = inWindow;
        }

        SDL_SetWindowTitle(_window,name.c_str());
        if(!icon.empty())
        {
            int x,y,channels;
            auto iconData = stbi_load(icon.string().c_str(),&x,&y,&channels,4);
            _icon = SDL_CreateSurfaceFrom(x,y,SDL_PIXELFORMAT_RGBA8888,iconData,static_cast<int>(x*sizeof(char)*4));

            auto success = SDL_SetWindowIcon(_window,_icon);
            stbi_image_free(iconData);
        }

        crucible::scripting::ScriptingEngine::loadManagedDll("Game",gameDll.string().c_str(),true);
        auto gameManagerType = scripting::ScriptingEngine::getManagedType("Crucible.Core.GameManager");
        auto gameManagerInitialize = gameManagerType.getFunction<void(*)()>("Initialize");
        gameManagerInitialize();
        _managedUpdate = gameManagerType.getFunction<void(*)(double)>("RunLoop");

        auto properties = SDL_GetWindowProperties(_window);
        slag::PlatformData platformData{};


#ifdef _WIN32
        platformData.platform = slag::PlatformData::WIN_32;
        platformData.data.win32.hwnd = SDL_GetPointerProperty(properties,SDL_PROP_WINDOW_WIN32_HWND_POINTER, nullptr);
        platformData.data.win32.hinstance = SDL_GetPointerProperty(properties,SDL_PROP_WINDOW_WIN32_INSTANCE_POINTER, nullptr);
#else
        const char* driverName = SDL_GetCurrentVideoDriver();
            if (strcmp(driverName, "wayland") == 0)
            {
                platformData.platform = slag::PlatformData::WAYLAND;
                platformData.data.wayland.surface = SDL_GetPointerProperty(properties,SDL_PROP_WINDOW_WAYLAND_SURFACE_POINTER, nullptr);
                platformData.data.wayland.display = SDL_GetPointerProperty(properties,SDL_PROP_WINDOW_WAYLAND_DISPLAY_POINTER, nullptr);
            }
            else
            {
                platformData.platform = slag::PlatformData::X11;
                platformData.data.x11.display = SDL_GetPointerProperty(properties,SDL_PROP_WINDOW_X11_DISPLAY_POINTER, nullptr);
                platformData.data.x11.window = reinterpret_cast<void*>(SDL_GetNumberProperty(properties,SDL_PROP_WINDOW_X11_WINDOW_NUMBER, 0));
            }
#endif

        int w,h;
        SDL_GetWindowSize(_window,&w,&h);
        _swapChain = slag::Swapchain::newSwapchain(platformData, w, h, 3, slag::Swapchain::MAILBOX, slag::Pixels::B8G8R8A8_UNORM_SRGB, CRUCIBLE_GAME_RESOURCE_CREATE);
        ecs_query_desc_t queryDesc{.terms = {{core::NodeECSReference::NodeEcsReferenceID()}, {core::World::RegisterOrRetrieveScriptingType("Crucible.Core.Camera")}}};
        _cameraQuery= ecs_query_init(core::World::ECS.world_, &queryDesc);

    }

    Game::~Game()
    {
        if(_windowManaged && _window)
        {
            SDL_DestroyWindow(_window);
        }
        if(_icon)
        {
            SDL_DestroySurface(_icon);
        }
        if(_swapChain)
        {
            delete _swapChain;
        }

        ecs_query_fini(_cameraQuery);
    }

    Game::Game(Game&& from)
    {
        move(from);
    }

    Game& Game::operator=(Game&& from)
    {
        move(from);
        return *this;
    }

    void Game::move(Game& from)
    {
        std::swap(_window,from._window);
        std::swap(_windowManaged,from._windowManaged);
        std::swap(_icon,from._icon);
        std::swap(_swapChain,from._swapChain);
        std::swap(_managedUpdate,from._managedUpdate);
        std::swap(_keepOpen,from._keepOpen);
    }

    void Game::handleEvent(SDL_Event& event)
    {
        switch (event.type)
        {
            case SDL_EVENT_WINDOW_CLOSE_REQUESTED:
                close();
                break;
            case SDL_EVENT_WINDOW_RESIZED:
            case SDL_EVENT_WINDOW_RESTORED:
                resize();
                break;
            case SDL_EVENT_WINDOW_MINIMIZED:
                minimize();
                break;
        }
    }

    void Game::update(double deltaTime)
    {
        _managedUpdate(deltaTime);
    }

    void Game::draw(slag::CommandBuffer* commandBuffer, slag::Texture* drawBuffer, slag::DescriptorPool* descriptorPool, core::VirtualUniformBuffer* uniformBuffer)
    {
        descriptorPool->reset();
        uniformBuffer->reset();
        commandBuffer->begin();
        commandBuffer->bindDescriptorPool(descriptorPool);
        slag::Texture* mainTexture = nullptr;

        auto root = core::World::RootNode;
        if (root)
        {
            auto basicShader = core::ShaderManager::getShaderReference("flat-test");
            //bind globals
            glm::vec4 wind(0,0,0,0);
            auto location = uniformBuffer->write(&wind, sizeof(glm::vec4));
            auto globalBundle = descriptorPool->makeBundle(basicShader.pipeline()->descriptorGroup(0));
            globalBundle.setUniformBuffer(0,0,location.buffer,location.offset,location.length);
            commandBuffer->bindGraphicsDescriptorBundle(basicShader.pipeline(),0,globalBundle);


            struct RenderCameraReference
            {
                core::Node* node;
                core::Camera* camera;
                static bool compare(RenderCameraReference& a, RenderCameraReference& b)
                {
                    return core::Camera::compare(a.camera,b.camera);
                }
            };
            std::vector<RenderCameraReference> cameras;
            ecs_iter_t iter = ecs_query_iter(core::World::ECS.world_,_cameraQuery);
            while (ecs_query_next(&iter))
            {
                auto nodes = ((static_cast<core::NodeECSReference*>(ecs_field_w_size(&iter, sizeof(core::NodeECSReference), 0))));

                auto cameraInstances = ((static_cast<scripting::ManagedInstance*>(ecs_field_w_size(&iter, sizeof(scripting::ManagedInstance), 1))));
                for (int i=0; i< iter.count; i++)
                {
                    auto& node = nodes[i];
                    auto& cameraInstance = cameraInstances[i];
                    auto camera = scripting::ScriptingEngine::getCamera(cameraInstance);
                    cameras.push_back({.node= node.node,.camera = camera});
                }
            }

            std::sort(cameras.begin(),cameras.end(),RenderCameraReference::compare);
            auto transformType = core::World::RegisterOrRetrieveType("Crucible.Core.Transform",sizeof(core::Transform),alignof(core::Transform));
            auto meshRendererType = core::World::RegisterOrRetrieveScriptingType("Crucible.Core.MeshRenderer");
            for (auto camera: cameras)
            {
                auto color = camera.camera->colorTarget();
                auto depth = camera.camera->depthTarget();
                auto node = camera.node;

                commandBuffer->setScissors(slag::Rectangle{.offset = {0,0}, .extent = {color->width(),color->height()}});
                commandBuffer->setViewPort(0,0,color->width(),color->height(),0,1);
                slag::ImageBarrier imageBarriers[2];
                imageBarriers[0] = slag::ImageBarrier
                    {
                        .texture = color,
                        .baseLayer = 0,
                        .layerCount = 1,
                        .baseMipLevel = 0,
                        .mipCount = 1,
                        .oldLayout = slag::Texture::UNDEFINED,
                        .newLayout = slag::Texture::RENDER_TARGET,
                        .accessBefore = slag::BarrierAccessFlags::NONE,
                        .accessAfter = slag::BarrierAccessFlags::SHADER_WRITE,
                        .syncBefore = slag::PipelineStageFlags::NONE,
                        .syncAfter = slag::PipelineStageFlags::ALL_GRAPHICS
                    };
                imageBarriers[1] = slag::ImageBarrier
                    {
                        .texture = depth,
                        .baseLayer = 0,
                        .layerCount = 1,
                        .baseMipLevel = 0,
                        .mipCount = 1,
                        .oldLayout = slag::Texture::UNDEFINED,
                        .newLayout = slag::Texture::DEPTH_TARGET,
                        .accessBefore = slag::BarrierAccessFlags::NONE,
                        .accessAfter = slag::BarrierAccessFlags::SHADER_WRITE,
                        .syncBefore = slag::PipelineStageFlags::NONE,
                        .syncAfter = slag::PipelineStageFlags::ALL_GRAPHICS
                    };

                commandBuffer->insertBarriers(imageBarriers,2,nullptr,0,nullptr,0);

                slag::ClearValue clearColor{1.0f,.15f,.1f,1.0f};
                slag::Attachment attachment(color,slag::Texture::RENDER_TARGET,true,clearColor);
                slag::Attachment depthAttachment{depth, slag::Texture::DEPTH_TARGET, true,{1}};
                commandBuffer->beginRendering(&attachment,1,&depthAttachment,slag::Rectangle{.offset = {0,0},.extent = {color->width(),color->height()}});

                //bind view
                slag::Buffer* projectionView = slag::Buffer::newBuffer(sizeof(glm::mat4)*2,slag::Buffer::CPU_AND_GPU,slag::Buffer::UNIFORM_BUFFER);
                glm::mat4 projectionMatrix = glm::perspective(90.0f,1920.0f/1080.0f,.001f,1000.0f);
                auto cameraTransform = core::Transform::cumulativeFrom(node);
                glm::mat4 viewMatrix = glm::inverse(cameraTransform.matrix());
                projectionView->update(0,&projectionMatrix,sizeof(glm::mat4));
                projectionView->update(sizeof(glm::mat4),&viewMatrix,sizeof(glm::mat4));
                auto viewBundle = descriptorPool->makeBundle(basicShader.pipeline()->descriptorGroup(1));
                viewBundle.setUniformBuffer(0,0,projectionView,0,projectionView->size());
                commandBuffer->bindGraphicsDescriptorBundle(basicShader.pipeline(),1,viewBundle);
                delete projectionView;


                core::Transform rootTransform;

                root->registerDraw(descriptorPool,uniformBuffer,&rootTransform,transformType,meshRendererType);
                core::World::MeshDrawPass->drawMeshes(commandBuffer);
                commandBuffer->endRendering();

                //TODO: there's got to be a better way to flag the camera as the main one, but for now, if it's the last one, it'll be set here
                mainTexture = color;

            }
        }

        slag::Texture::Layout finalImageLayout = slag::Texture::UNDEFINED;

        if (mainTexture)
        {
            slag::ImageBarrier imageBarriers[2];
            imageBarriers[0] = slag::ImageBarrier
            {
                .texture = drawBuffer,
                .baseLayer = 0,
                .layerCount = 1,
                .baseMipLevel = 0,
                .mipCount = 1,
                .oldLayout = slag::Texture::UNDEFINED,
                .newLayout = slag::Texture::TRANSFER_DESTINATION,
                .accessBefore = slag::BarrierAccessFlags::NONE,
                .accessAfter = slag::BarrierAccessFlags::TRANSFER_WRITE,
                .syncBefore = slag::PipelineStageFlags::NONE,
                .syncAfter = slag::PipelineStageFlags::TRANSFER,
            };
            imageBarriers[1] = slag::ImageBarrier
            {
                .texture = mainTexture,
                .baseLayer = 0,
                .layerCount = 1,
                .baseMipLevel = 0,
                .mipCount = 1,
                .oldLayout = slag::Texture::RENDER_TARGET,
                .newLayout = slag::Texture::TRANSFER_SOURCE,
                .accessBefore = slag::BarrierAccessFlags::SHADER_WRITE,
                .accessAfter = slag::BarrierAccessFlags::TRANSFER_READ,
                .syncBefore = slag::PipelineStageFlags::ALL_GRAPHICS,
                .syncAfter = slag::PipelineStageFlags::TRANSFER,
            };
            commandBuffer->insertBarriers(imageBarriers,2,nullptr,0,nullptr,0);
            commandBuffer->blit(mainTexture,slag::Texture::TRANSFER_SOURCE,0,0,slag::Rectangle{.extent = {mainTexture->width(),mainTexture->height()}},drawBuffer,slag::Texture::TRANSFER_DESTINATION,0,0,slag::Rectangle{.extent = {drawBuffer->width(),drawBuffer->height()}},slag::Sampler::NEAREST);

            finalImageLayout = slag::Texture::TRANSFER_DESTINATION;
        }



        commandBuffer->insertBarrier(slag::ImageBarrier
            {
                .texture = drawBuffer,
                .baseLayer = 0,
                .layerCount = 1,
                .baseMipLevel = 0,
                .mipCount = 1,
                .oldLayout = finalImageLayout,
                .newLayout = slag::Texture::PRESENT,
                .accessBefore = slag::BarrierAccessFlags::TRANSFER_WRITE,
                .accessAfter = slag::BarrierAccessFlags::TRANSFER_READ,
                .syncBefore = slag::PipelineStageFlags::ALL_GRAPHICS,
                .syncAfter = slag::PipelineStageFlags::TRANSFER,
            });

        commandBuffer->end();
    }

    void Game::close()
    {
       _keepOpen = false;
    }

    void Game::resize()
    {
        int w,h;
        SDL_GetWindowSize(_window,&w,&h);
        _swapChain->resize(w,h);
    }

    void Game::minimize()
    {
        _swapChain->resize(0,0);
    }

    void Game::run()
    {
        Uint64 previousCounter = 0;
        Uint64 currentCounter = 0;
        double deltaTime = 0.0;

        while(_keepOpen)
        {
            currentCounter = SDL_GetPerformanceCounter();
            deltaTime = (currentCounter - previousCounter) / (double)SDL_GetPerformanceFrequency();
            previousCounter = currentCounter;

            SDL_Event event;
            while(SDL_PollEvent(&event))
            {
                handleEvent(event);
            }
            update(deltaTime);
            if(auto frame = _swapChain->next())
            {
                auto resources = static_cast<CrucibleFrameResources*>(frame->resources);
                draw(resources->commandBuffer,frame->backBuffer(),resources->descriptorPool, resources->uniformBuffer);
                slag::SlagLib::graphicsCard()->graphicsQueue()->submit(&resources->commandBuffer,1, nullptr,0, nullptr,0,frame);
            }
        }
    }
} // crucible