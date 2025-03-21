#include "RenderStage.h"

namespace crucible
{
    namespace core
    {
        RenderStage::RenderStage(const std::string& name, const std::vector<RenderGraphResourceData>& inFormats, const std::vector<RenderGraphResourceData>& outFormats)
        {
            _stageName = name;
            _inputs = inFormats;
            _outputs = outFormats;
        }

        RenderStage::RenderStage(RenderStage&& from)
        {
            move(from);
        }

        RenderStage& RenderStage::operator=(RenderStage&& from)
        {
            move(from);
            return *this;
        }

        void RenderStage::execute(slag::CommandBuffer* commandBuffer,RenderGraphResourceDictionary& dictionary)
        {
            throw std::runtime_error("RenderStage::execute not implemented");
            for (auto& kvPair: _priorities)
            {
                kvPair.second.draw(commandBuffer);
            }
            _priorities.clear();
        }

        void RenderStage::registerRenderable(const ShaderReference& shader, Mesh* mesh, slag::DescriptorBundle&& bundle, uint8_t priority)
        {
            auto entry = _priorities.find(priority);
            if (entry == _priorities.end())
            {
                entry = _priorities.insert({priority,RenderPriority()}).first;
            }
            auto& bracket = entry->second;
            bracket.registerRenderable(shader,mesh,std::move(bundle));
        }

        void RenderStage::move(RenderStage& from)
        {
            _stageName.swap(from._stageName);
            _inputs.swap(from._inputs);
            _outputs.swap(from._outputs);
            _priorities.swap(from._priorities);
        }
    } // core
} // crucible
