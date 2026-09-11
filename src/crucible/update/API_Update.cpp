#include "API_Update.h"
#include <SDL3/SDL.h>

#include "crucible/rendering/core/ResourceManager.h"
#ifdef CRUCIBLE_RENDERING_MODULE
#include "crucible/rendering/API_Rendering.h"
#endif

#ifdef CRUCIBLE_SCRIPTING_MODULE
#include "crucible/scripting/API_Scripting.h"
#endif


namespace crucible
{
    namespace update
    {
        bool KEEP_ALIVE = false;

        void run()
        {
#ifdef CRUCIBLE_RENDERING_MODULE
            auto graphicsCard = crucible::rendering::getGraphicsCard();
            auto timelineSemaphore = graphicsCard->newSemaphore(0);

            rendering::Window mainWindow("Welcome to the Crucible",500,500);
            mainWindow.show();
#endif

            KEEP_ALIVE = true;
            uint32_t last_tick_time = 0;
            double delta = 0;
            while (KEEP_ALIVE)
            {
                uint32_t tick_time = SDL_GetTicks();
                delta = ((double)tick_time - (double)last_tick_time)/1000;
                last_tick_time = tick_time;

                SDL_Event event;
                while (SDL_PollEvent(&event))
                {
                    if (event.type == SDL_EVENT_QUIT)
                    {
                        KEEP_ALIVE = false;
                        break;
                    }
#ifdef CRUCIBLE_RENDERING_MODULE
                    else if (event.type == SDL_EVENT_WINDOW_CLOSE_REQUESTED)
                    {
                        SDL_Window* window = SDL_GetWindowFromID(event.window.windowID);
                        rendering::Window* crucibleWindow = (rendering::Window*)SDL_GetPointerProperty(SDL_GetWindowProperties(window),crucible::rendering::OWNING_CRUCIBLE_WINDOW_OBJECT_ID,nullptr);
                        crucibleWindow->attemptClose();
                    }
                    else if (event.type == SDL_EVENT_WINDOW_RESIZED)
                    {
                        SDL_Window* window = SDL_GetWindowFromID(event.window.windowID);
                        rendering::Window* crucibleWindow = (rendering::Window*)SDL_GetPointerProperty(SDL_GetWindowProperties(window),crucible::rendering::OWNING_CRUCIBLE_WINDOW_OBJECT_ID,nullptr);

                        int width, height;
                        SDL_GetWindowSize(window, &width, &height);
                        crucibleWindow->rebuildRenderTargets(width, height);
                    }
#endif

                }

#ifdef CRUCIBLE_SCRIPTING_MODULE
                if (scripting::ScriptingUpdateCallback!=nullptr)
                {
                    scripting::ScriptingUpdateCallback(delta);
                }
#endif
#ifdef CRUCIBLE_RENDERING_MODULE
                //do rendering stuff


                uint64_t waitValue = rendering::ResourceManager::getFrameCounter();
                rendering::ResourceManager::nextFrame();
                uint64_t signalValue = rendering::ResourceManager::getFrameCounter();
                auto commandBuffer = rendering::ResourceManager::currentCommandBuffer();
                commandBuffer->begin();

                auto openWindows = rendering::Window::getOpenedWindows();
                std::vector<slag::TextureBarrier> barriers(openWindows.size());
                auto totalBarriers = 0;
                for (auto i=0; i<openWindows.size(); i++)
                {
                    auto window = openWindows[i];
                    if (window->swapChain()->next())
                    {
                        barriers[totalBarriers] = slag::TextureBarrier
                        {
                            .texture = window->swapChain()->currentFrame()->renderBuffer(),
                            .baseMipLevel = 0,
                            .mipCount = 1,
                            .baseLayer = 0,
                            .layerCount = 1,
                            .syncBefore = slag::SyncStages::NONE,
                            .syncAfter = slag::SyncStages::COPY,
                            .flush = slag::MemoryCaches::NONE,
                            .invalidate = slag::MemoryCaches::NONE,
                            .layoutBefore = slag::TextureLayout::UNKNOWN,
                            .layoutAfter = slag::TextureLayout::PRESENT
                        };
                        totalBarriers++;
                    }

                }
                if (totalBarriers > 0)
                {
                    commandBuffer->insertBarriers(barriers.data(),totalBarriers);
                }
                commandBuffer->end();
                slag::SemaphoreValue wait{.semaphore = timelineSemaphore, .value = waitValue};
                slag::SemaphoreValue signal{.semaphore = timelineSemaphore, .value = signalValue};
                slag::SubmissionBatch batch
                {
                    .waitSemaphores = &wait,
                    .waitSemaphoreCount = 1,
                    .commandBuffers = &commandBuffer,
                    .commandBufferCount = 1,
                    .signalSemaphores = &signal,
                    .signalSemaphoreCount = 1
                };

                graphicsCard->graphicsQueue()->submit(batch);

                //TODO: mulithread this portion? I don't know for sure if it's possible, my early tests seem really wonky
                {
                    timelineSemaphore->waitForValue(signalValue);
                    for (auto window : openWindows)
                    {
                        window->swapChain()->present();
                    }
                }
#endif


            }

#ifdef CRUCIBLE_RENDERING_MODULE
            timelineSemaphore->waitForValue(rendering::ResourceManager::getFrameCounter());
            delete timelineSemaphore;
#endif

        }
    } // update
} // crucible
void CRUCIBLE_API_finish()
{
    crucible::update::KEEP_ALIVE = false;
}
