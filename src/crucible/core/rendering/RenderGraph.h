#ifndef CRUCIBLE_RENDERGRAPH_H
#define CRUCIBLE_RENDERGRAPH_H
#include "RenderStage.h"

namespace crucible
{
    namespace core
    {
        class RenderGraph
        {
        public:
            void addStage(RenderStage&& stage);
            void removeStage(const std::string& name);
            void addRenderable(const std::string& stage, uint8_t priority, ShaderReference& shader, Mesh* mesh, slag::DescriptorBundle&& bundle);
            void render(slag::CommandBuffer* commandBuffer);
        };
    } // core
} // crucible

#endif //CRUCIBLE_RENDERGRAPH_H
