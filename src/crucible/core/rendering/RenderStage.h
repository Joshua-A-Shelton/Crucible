#ifndef CRUCIBLE_RENDERSTAGE_H
#define CRUCIBLE_RENDERSTAGE_H
#include <map>
#include <string>
#include <vector>

#include "RenderGraphResourceData.h"
#include "RenderGraphResourceDictionary.h"
#include "RenderPriority.h"

namespace crucible
{
    namespace core
    {
        class RenderStage
        {
        public:
            RenderStage(const std::string& name,const std::vector<RenderGraphResourceData>& inFormats, const std::vector<RenderGraphResourceData>& outFormats);
            ~RenderStage()=default;
            RenderStage(const RenderStage&) = delete;
            RenderStage& operator=(const RenderStage&) = delete;
            RenderStage(RenderStage&& from);
            RenderStage& operator=(RenderStage&& from);

            void execute(slag::CommandBuffer* commandBuffer,RenderGraphResourceDictionary& dictionary);
            void registerRenderable(const ShaderReference& shader, Mesh* mesh, slag::DescriptorBundle&& bundle, uint8_t priority);
        private:
            void move(RenderStage& from);
            std::string _stageName;
            std::vector<RenderGraphResourceData> _inputs;
            std::vector<RenderGraphResourceData> _outputs;
            std::map<uint8_t,RenderPriority> _priorities;
        };
    } // core
} // crucible

#endif //CRUCIBLE_RENDERSTAGE_H
