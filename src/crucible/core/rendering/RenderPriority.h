#ifndef CRUCIBLE_RENDERPRIORITY_H
#define CRUCIBLE_RENDERPRIORITY_H
#include <cstdint>
#include <vector>

#include "RenderData.h"
#include "crucible/core/ShaderManager.h"

namespace crucible
{
    namespace core
    {
        class RenderPriority
        {
        public:
            RenderPriority();
            ~RenderPriority()=default;
            RenderPriority(const RenderPriority&)=delete;
            RenderPriority& operator=(const RenderPriority&)=delete;
            RenderPriority(RenderPriority&& from);
            RenderPriority& operator=(RenderPriority&& from);
            uint8_t priority()const;
            void registerRenderable(const ShaderReference& shader, Mesh* mesh, slag::DescriptorBundle&& bundle);
            void draw(slag::CommandBuffer* commandBuffer);
        private:
            void move(RenderPriority& from);
            std::unordered_map<ShaderReference,std::vector<RenderData>,ShaderReference::hashFunction> _renderables;
        };
    } // core
} // crucible

#endif //CRUCIBLE_RENDERPRIORITY_H
