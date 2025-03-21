#include "RenderGraphResourceDictionary.h"

#include "slag/CommandBuffer.h"
#include "slag/Resources/ResourceManager.h"

namespace crucible
{
    namespace core
    {
        void RenderGraphResourceDictionary::addResource(const std::string& resourceName, slag::Texture* texture, slag::Texture::Layout currentLayout, slag::BarrierAccess currentAccess,slag::PipelineStages currentPipelineStage, bool persistent)
        {
            auto entry = _entries.find(resourceName);
            if (entry==_entries.end())
            {
                _entries.insert
                (
                    std::make_pair
                    (
                        resourceName,
                        RenderGraphDictionaryEntry
                        {
                            .resourceType = RenderGraphResourceType::TEXTURE,
                            .entry = RenderGraphTextureDictionaryEntry
                                {
                                    .texture = texture,
                                    .currentLayout = currentLayout,
                                    .currentAccess = currentAccess,
                                    .currentPipelineStages = currentPipelineStage
                                },
                            .persistent = persistent
                        }
                    )
                );
            }
            else
            {
                throw std::runtime_error("Resource already exists");
            }
        }

        void RenderGraphResourceDictionary::addResource(const std::string& resourceName, slag::Buffer* buffer, slag::BarrierAccess currentAccess, slag::PipelineStages currentPipelineStage, bool persistent)
        {
            auto entry = _entries.find(resourceName);
            if (entry==_entries.end())
            {
                RenderGraphBufferDictionaryEntry bufferEntry
                {
                    .buffer = buffer,
                    .currentAccess = currentAccess,
                    .currentPipelineStage = currentPipelineStage,
                };
                RenderGraphEntry rge{.buffer = bufferEntry};
                RenderGraphDictionaryEntry dictionaryEntry{RenderGraphResourceType::BUFFER,rge,persistent};

                _entries.insert(std::make_pair(resourceName,dictionaryEntry));
            }
            else
            {
                throw std::runtime_error("Resource already exists");
            }
        }

        void RenderGraphResourceDictionary::removeResource(const std::string& resourceName)
        {
            _entries.erase(resourceName);
        }

        void RenderGraphResourceDictionary::transition(const std::vector<RenderGraphTransitionRequest>& transitions, slag::CommandBuffer* commandBuffer)
        {
            std::vector<slag::ImageBarrier> imageBarriers;
            std::vector<slag::BufferBarrier> bufferBarriers;
            for (auto& transition : transitions)
            {
                auto entry = _entries.find(transition.resourceName);
                if (entry==_entries.end())
                {
                    throw std::runtime_error("Resource not found for transition");
                }
                if (transition.resourceType==RenderGraphResourceType::TEXTURE)
                {
                    if (entry->second.resourceType!=RenderGraphResourceType::TEXTURE)
                    {
                        throw std::runtime_error("Attempted to apply buffer transition to texture resource");
                    }

                    auto texture = entry->second.entry.texture.texture;

                    imageBarriers.emplace_back
                    (
                        texture,
                        0,
                        texture->layers(),
                        0,
                        texture->mipLevels(),
                        entry->second.entry.texture.currentLayout,
                        transition.transition.textureTransition.newLayout,
                        entry->second.entry.texture.currentAccess,
                        transition.transition.bufferTransition.newAccess,
                        entry->second.entry.texture.currentPipelineStages,
                        transition.transition.textureTransition.newPipelineStages
                    );
                    entry->second.entry.texture.currentLayout = transition.transition.textureTransition.newLayout;
                    entry->second.entry.texture.currentAccess = transition.transition.bufferTransition.newAccess;
                    entry->second.entry.texture.currentPipelineStages = transition.transition.textureTransition.newPipelineStages;
                }
                else if (transition.resourceType==RenderGraphResourceType::BUFFER)
                {
                    if (entry->second.resourceType!=RenderGraphResourceType::BUFFER)
                    {
                        throw std::runtime_error("Attempted to apply texture transition to buffer resource");
                    }

                    auto buffer = entry->second.entry.buffer.buffer;

                    bufferBarriers.emplace_back
                    (
                       buffer,
                       0,
                       buffer->size(),
                       entry->second.entry.buffer.currentAccess,
                       transition.transition.bufferTransition.newAccess,
                       entry->second.entry.buffer.currentPipelineStage,
                       transition.transition.bufferTransition.newPipelineStages
                    );
                    entry->second.entry.buffer.currentAccess = transition.transition.bufferTransition.newAccess;
                    entry->second.entry.buffer.currentPipelineStage = transition.transition.bufferTransition.newPipelineStages;
                }

                commandBuffer->insertBarriers(imageBarriers.data(),imageBarriers.size(),bufferBarriers.data(),bufferBarriers.size(),nullptr,0);
            }
        }
    } // core
} // crucible
