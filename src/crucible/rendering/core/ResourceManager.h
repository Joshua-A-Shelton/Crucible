#ifndef CRUCIBLE_RESOURCEMANAGER_H
#define CRUCIBLE_RESOURCEMANAGER_H
#include <slag/Slag.h>

namespace crucible
{
    namespace rendering
    {
        class Window;
        class ResourceManager
        {
        public:
            static void initialize();
            static void cleanup();

            static void queueDelete(slag::Buffer* buffer);
            static void queueDelete(slag::Texture* texture);
            static void queueDelete(slag::Semaphore* semaphore);
            static void queueDelete(slag::CommandBuffer* commandBuffer);
            static void queueDelete(Window* window);

            static void nextFrame();
            static slag::CommandBuffer* currentCommandBuffer();
            static uint64_t getFrameCounter();
        private:
            struct FrameData
            {
                std::vector<slag::Buffer*> buffersToDelete;
                std::vector<slag::Texture*> texturesToDelete;
                std::vector<slag::Semaphore*> semaphoresToDelete;
                std::vector<slag::CommandBuffer*> commandBuffersToDelete;
                std::vector<Window*> windowsToDelete;
                slag::CommandBuffer* commandBuffer;
            };
            inline static uint64_t FRAME_COUNTER = 0;
            inline static FrameData RESOURCE_MANAGER_FRAME_DATA[3];
            inline static uint32_t CURRENT_RESOURCE_FRAME = 0;
        };
    } // rendering
} // crucible

#endif //CRUCIBLE_RESOURCEMANAGER_H