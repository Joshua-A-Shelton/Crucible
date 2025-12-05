//
// Created by josh on 10/15/25.
//

#include "Game.h"

#include "DeferredJobQueue.h"
#include "Node.h"
#include "scripting/ScriptingEngine.h"

namespace crucible
{
    struct CrucibleGameFrameResources: public slag::FrameResources
    {
    public:
        slag::CommandBuffer* commandBuffer = nullptr;
        slag::DescriptorPool* descriptorPool = nullptr;
        std::vector<slag::Texture*> texturesDeleteQueue;
        std::vector<slag::Buffer*> buffersDeleteQueue;
        CrucibleGameFrameResources()
        {
            commandBuffer = slag::CommandBuffer::newCommandBuffer(slag::GPUQueue::QueueType::GRAPHICS);
            descriptorPool = slag::DescriptorPool::newDescriptorPool();
        }
        ~CrucibleGameFrameResources()
        {
            delete commandBuffer;
            delete descriptorPool;
            deleteQueuedResources();
        }
        void deleteQueuedResources()
        {
            for (auto& texture : texturesDeleteQueue)
            {
                delete texture;
            }
            texturesDeleteQueue.clear();
            for (auto& buffer : buffersDeleteQueue)
            {
                delete buffer;
            }
            buffersDeleteQueue.clear();
        }
    };
    slag::FrameResources* createCrucibleFrameResources(uint8_t frame, slag::SwapChain* swapChain)
    {
        return new CrucibleGameFrameResources();
    }


    Game::Game(const char* gameName)
    {
        if (_instance != nullptr)
        {
            throw std::runtime_error("Tried to initialize more than one Game");
        }
        _instance = this;
        _window = SDL_CreateWindow(gameName,500,500,SDL_WINDOW_VULKAN);

        int windowWidth,windowHeight;
        SDL_GetWindowSize(_window,&windowWidth,&windowHeight);

        slag::PlatformData pd{};

        auto properties = SDL_GetWindowProperties(_window);
#ifdef _WIN32
        pd.platform = slag::Platform::WIN_32;
        pd.details.win32.hwnd = SDL_GetPointerProperty(properties,SDL_PROP_WINDOW_WIN32_HWND_POINTER, nullptr);
        pd.details.win32.hinstance = SDL_GetPointerProperty(properties,SDL_PROP_WINDOW_WIN32_INSTANCE_POINTER, nullptr);
#else
        const char* driverName = SDL_GetCurrentVideoDriver();
        if (strcmp(driverName, "wayland") == 0)
        {
            platformData.platform = slag::PlatformData::WAYLAND;
            pd.details.wayland.surface = SDL_GetPointerProperty(properties,SDL_PROP_WINDOW_WAYLAND_SURFACE_POINTER, nullptr);
            pd.details.display = SDL_GetPointerProperty(properties,SDL_PROP_WINDOW_WAYLAND_DISPLAY_POINTER, nullptr);
        }
        else
        {
            platformData.platform = slag::PlatformData::X11;
            pd.details.x11.display = SDL_GetPointerProperty(properties,SDL_PROP_WINDOW_X11_DISPLAY_POINTER, nullptr);
            pd.details.x11.window = reinterpret_cast<void*>(SDL_GetNumberProperty(properties,SDL_PROP_WINDOW_X11_WINDOW_NUMBER, 0));
        }
#endif

        slag::SwapChainDetails swapchainDetails{};
        swapchainDetails.presentMode = slag::SwapChain::PresentMode::BUFFER;
        swapchainDetails.frameCount = 2;
        swapchainDetails.alphaCompositing = slag::SwapChain::AlphaCompositing::PRE_MULTIPLY;
        swapchainDetails.createResourceFunction = createCrucibleFrameResources;

        _swapChain = slag::SwapChain::newSwapChain(pd,windowWidth,windowHeight,swapchainDetails);
        scripting::ScriptingEngine::gameManagerInitialize();
    }

    Game::~Game()
    {
        scripting::ScriptingEngine::gameManagerCleanUp();
        while (_nodeDeletionQueue.size() > 0)
        {
            auto node = _nodeDeletionQueue.front();
            _nodeDeletionQueue.pop();
            delete node;
        }
        delete _swapChain;
        SDL_DestroyWindow(_window);
        _instance = nullptr;
    }

    Game* Game::instance()
    {
        return _instance;
    }

    void Game::run()
    {
        Uint64 previousCounter = 0;
        Uint64 currentCounter = 0;
        double deltaTime = 0.0;
        while (_isRunning)
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
            //TODO: perhaps place some sort of limit on this, but for the time being, let's test it out to see
            while (_nodeDeletionQueue.size() > 0)
            {
                auto node = _nodeDeletionQueue.front();
                _nodeDeletionQueue.pop();
                delete node;
            }
            if(auto frame = _swapChain->next())
            {
                auto resources = frame->frameResources<CrucibleGameFrameResources>();
                resources->deleteQueuedResources();
                draw(resources->commandBuffer,resources->descriptorPool);
                slag::QueueSubmissionBatch batch
                {
                    .waitSemaphores = nullptr,
                    .waitSemaphoreCount = 0,
                    .commandBuffers = &resources->commandBuffer,
                    .commandBufferCount = 1,
                    .signalSemaphores = nullptr,
                    .signalSemaphoreCount = 0,
                };
                slag::slagGraphicsCard()->graphicsQueue()->submit(&batch,1);
            }
        }
    }

    void Game::queueForDeletion(slag::Texture* texture)
    {
        _swapChain->currentFrame()->frameResources<CrucibleGameFrameResources>()->texturesDeleteQueue.push_back(texture);
    }

    void Game::queueForDeletion(slag::Buffer* buffer)
    {
        _swapChain->currentFrame()->frameResources<CrucibleGameFrameResources>()->buffersDeleteQueue.push_back(buffer);
    }

    void Game::queueForDeletion(crucible::Node* node)
    {
        _nodeDeletionQueue.push(node);
    }

    void Game::signalQuit()
    {
        _isRunning = false;
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
        //TODO: call managed update
    }

    void Game::draw(slag::CommandBuffer* commandBuffer, slag::DescriptorPool* descriptorPool)
    {
        commandBuffer->begin();
        commandBuffer->bindDescriptorPool(descriptorPool);
        //TODO: draw stuff
        commandBuffer->end();
    }

    void Game::close()
    {
        _isRunning = false;
    }

    void Game::resize()
    {
        int w,h;
        SDL_GetWindowSize(_window,&w,&h);
        _swapChain->backBufferSize(w,h);
    }

    void Game::minimize()
    {
        _swapChain->backBufferSize(0,0);
    }
} // crucible