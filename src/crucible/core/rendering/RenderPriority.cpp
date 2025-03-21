#include "RenderPriority.h"

namespace crucible
{
    namespace core
    {
        RenderPriority::RenderPriority()
        {
        }

        RenderPriority::RenderPriority(RenderPriority&& from)
        {
            move(from);
        }

        RenderPriority& RenderPriority::operator=(RenderPriority&& from)
        {
            move(from);
            return *this;
        }

        void RenderPriority::registerRenderable(const ShaderReference& shader, Mesh* mesh, slag::DescriptorBundle&& bundle)
        {
            auto& list = _renderables[shader];
            list.emplace_back(mesh,shader.pipeline(),std::move(bundle));
        }

        void RenderPriority::draw(slag::CommandBuffer* commandBuffer)
        {
            for (auto& kvpair: _renderables)
            {
                auto& shader = kvpair.first;
                auto& renderables = kvpair.second;
                commandBuffer->bindGraphicsShader(shader.pipeline());
                for (auto& renderable: renderables)
                {
                    renderable.draw(commandBuffer,shader.requiredAttributes());
                }
            }
        }

        void RenderPriority::move(RenderPriority& from)
        {
            _renderables.swap(from._renderables);
        }
    } // core
} // crucible
