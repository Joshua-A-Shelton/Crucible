#ifndef CRUCIBLE_RENDERGRAPHRESOURCEDICTIONARY_H
#define CRUCIBLE_RENDERGRAPHRESOURCEDICTIONARY_H
#include <unordered_map>

#include "slag/GpuMemoryBarriers.h"
#include "slag/Texture.h"

namespace crucible
{
    namespace core
    {
        enum RenderGraphResourceType
        {
            TEXTURE,
            BUFFER
        };
        class RenderGraphTextureDictionaryEntry
        {
        public:
            slag::Texture* texture = nullptr;
            slag::Texture::Layout currentLayout = slag::Texture::UNDEFINED;
            slag::BarrierAccess currentAccess = slag::BarrierAccessFlags::NONE;
            slag::PipelineStages currentPipelineStages = slag::PipelineStageFlags::NONE;
        };
        class RenderGraphBufferDictionaryEntry
        {
        public:
            slag::Buffer* buffer = nullptr;
            slag::BarrierAccess currentAccess = slag::BarrierAccessFlags::NONE;
            slag::PipelineStages currentPipelineStage = slag::PipelineStageFlags::NONE;
        };
        union RenderGraphEntry
        {
            RenderGraphTextureDictionaryEntry texture;
            RenderGraphBufferDictionaryEntry buffer;
        };

        struct  RenderGraphDictionaryEntry
        {
            RenderGraphResourceType resourceType = RenderGraphResourceType::TEXTURE;
            RenderGraphEntry entry;
            bool persistent = false;

        };

        class RenderGraphTextureTransition
        {
        public:
            slag::Texture::Layout newLayout = slag::Texture::UNDEFINED;
            slag::BarrierAccess newAccess = slag::BarrierAccessFlags::NONE;
            slag::PipelineStages newPipelineStages = slag::PipelineStageFlags::NONE;
        };

        class RenderGraphBufferTransition
        {
        public:
            slag::BarrierAccess newAccess = slag::BarrierAccessFlags::NONE;
            slag::PipelineStages newPipelineStages = slag::PipelineStageFlags::NONE;
        };

        union RenderGraphTransition
        {
            RenderGraphTextureTransition textureTransition;
            RenderGraphBufferTransition bufferTransition;
        };

        class RenderGraphTransitionRequest
        {
        public:
            std::string resourceName;
            RenderGraphResourceType resourceType = RenderGraphResourceType::TEXTURE;
            RenderGraphTransition transition;
        };

        class RenderGraphResourceDictionary
        {
        public:
            void addResource(const std::string& resourceName, slag::Texture* texture, slag::Texture::Layout, slag::BarrierAccess currentAccess, slag::PipelineStages currentPipelineStage, bool persistent);
            void addResource(const std::string& resourceName, slag::Buffer* buffer, slag::BarrierAccess currentAccess, slag::PipelineStages currentPipelineStage, bool persistent);
            void removeResource(const std::string& resourceName);
            void transition(const std::vector<RenderGraphTransitionRequest>& transitions, slag::CommandBuffer* commandBuffer);
            void clear(bool removePersistent=false);
            slag::Texture* getTexture(const std::string& resourceName);
            slag::Buffer* getBuffer(const std::string& resourceName);
        private:
            std::unordered_map<std::string, RenderGraphDictionaryEntry> _entries;
        };
    } // core
} // crucible

#endif //CRUCIBLE_RENDERGRAPHRESOURCEDICTIONARY_H
