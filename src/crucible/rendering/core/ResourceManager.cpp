#include "ResourceManager.h"

#include <stdexcept>

#include "crucible/rendering/API_Rendering.h"

namespace crucible
{
    namespace rendering
    {
        void ResourceManager::initialize()
        {
            auto card = getGraphicsCard();
            FRAME_COUNTER = 0;
            for (auto i=0; i<3; i++)
            {
                RESOURCE_MANAGER_FRAME_DATA[i].commandBuffer = card->newCommandBuffer(slag::QueueType::GRAPHICS);
            }
        }

        void ResourceManager::cleanup()
        {
            for (int i=0; i<3; i++)
            {
                auto& frameData = RESOURCE_MANAGER_FRAME_DATA[i];
                for (auto buffer : frameData.buffersToDelete)
                {
                    delete buffer;
                }
                for (auto texture : frameData.texturesToDelete)
                {
                    delete texture;
                }
                for (auto semaphore : frameData.semaphoresToDelete)
                {
                    delete semaphore;
                }
                for (auto commandBuffer : frameData.commandBuffersToDelete)
                {
                    delete commandBuffer;
                }

                delete frameData.commandBuffer;
                frameData.commandBuffer = nullptr;
            }
        }

        void ResourceManager::queueDelete(slag::Buffer* buffer)
        {
            RESOURCE_MANAGER_FRAME_DATA[CURRENT_RESOURCE_FRAME].buffersToDelete.push_back(buffer);
        }

        void ResourceManager::queueDelete(slag::Texture* texture)
        {
            RESOURCE_MANAGER_FRAME_DATA[CURRENT_RESOURCE_FRAME].texturesToDelete.push_back(texture);
        }

        void ResourceManager::queueDelete(slag::Semaphore* semaphore)
        {
            RESOURCE_MANAGER_FRAME_DATA[CURRENT_RESOURCE_FRAME].semaphoresToDelete.push_back(semaphore);
        }

        void ResourceManager::queueDelete(slag::CommandBuffer* commandBuffer)
        {
            RESOURCE_MANAGER_FRAME_DATA[CURRENT_RESOURCE_FRAME].commandBuffersToDelete.push_back(commandBuffer);
        }

        void ResourceManager::queueDelete(Window* window)
        {
            RESOURCE_MANAGER_FRAME_DATA[CURRENT_RESOURCE_FRAME].windowsToDelete.push_back(window);
        }

        void ResourceManager::nextFrame()
        {

            auto& currentFrameData = RESOURCE_MANAGER_FRAME_DATA[CURRENT_RESOURCE_FRAME];
            //we can delete windows immediately, no need to wait for them to come around again;
            if (currentFrameData.windowsToDelete.size() > 0)
            {
                for (auto window : currentFrameData.windowsToDelete)
                {
                    delete window;
                }
                currentFrameData.windowsToDelete.clear();
            }

            CURRENT_RESOURCE_FRAME++;
            if (CURRENT_RESOURCE_FRAME >= 3)
            {
                CURRENT_RESOURCE_FRAME = 0;
            }
            //advance frame
            auto& nextFrameData = RESOURCE_MANAGER_FRAME_DATA[CURRENT_RESOURCE_FRAME];

            //clear buffers
            if (!nextFrameData.buffersToDelete.empty())
            {
                for (auto buffer : nextFrameData.buffersToDelete)
                {
                    delete buffer;
                }
                nextFrameData.buffersToDelete.clear();
            }
            //clear textures
            if (!nextFrameData.texturesToDelete.empty())
            {
                for (auto texture : nextFrameData.texturesToDelete)
                {
                    delete texture;
                }
                nextFrameData.texturesToDelete.clear();
            }
            //clear semaphores
            if (!nextFrameData.semaphoresToDelete.empty())
            {
                for (auto semaphore : nextFrameData.semaphoresToDelete)
                {
                    delete semaphore;
                }
                nextFrameData.semaphoresToDelete.clear();
            }
            //clear commandBuffers
            if (!nextFrameData.commandBuffersToDelete.empty())
            {
                for (auto commandBuffer : nextFrameData.commandBuffersToDelete)
                {
                    delete commandBuffer;
                }
                nextFrameData.commandBuffersToDelete.clear();
            }

            FRAME_COUNTER++;
        }

        slag::CommandBuffer* ResourceManager::currentCommandBuffer()
        {
            auto& currentFrameData = RESOURCE_MANAGER_FRAME_DATA[CURRENT_RESOURCE_FRAME];
            return currentFrameData.commandBuffer;
        }


        uint64_t ResourceManager::getFrameCounter()
        {
            return FRAME_COUNTER;
        }
    } // rendering
} // crucible