#ifndef CRUCIBLE_MESHPASS_H
#define CRUCIBLE_MESHPASS_H
#include <map>
#include "MeshShaderBlock.h"

namespace crucible
{
    namespace core
    {
        class MeshShaderPriorityGroup
        {
        public:
            MeshShaderPriorityGroup();
            ~MeshShaderPriorityGroup()=default;
            MeshShaderPriorityGroup(const MeshShaderPriorityGroup&)=delete;
            MeshShaderPriorityGroup& operator=(const MeshShaderPriorityGroup&)=delete;
            MeshShaderPriorityGroup(MeshShaderPriorityGroup&&from);
            MeshShaderPriorityGroup& operator=(MeshShaderPriorityGroup&&from);
            void registerRenderable(const ShaderReference& shader, Mesh* mesh, slag::DescriptorBundle&& descriptorBundle);
            void drawMeshes(slag::CommandBuffer* commandBuffer);
        private:
            void move(MeshShaderPriorityGroup& from);
            std::unordered_map<slag::ShaderPipeline*,MeshShaderBlock> _shaderBlocks;
        };

        class MeshPass
        {
        public:
            MeshPass();
            ~MeshPass()=default;
            MeshPass(const MeshPass&)=delete;
            MeshPass& operator=(const MeshPass&)=delete;
            MeshPass(MeshPass&& from);
            MeshPass& operator=(MeshPass&& from);
            void registerMeshData(uint8_t priority, const ShaderReference& shader, Mesh* mesh, slag::DescriptorBundle&& descriptorBundle);
            void drawMeshes(slag::CommandBuffer* commandBuffer);
        private:
            void move(MeshPass& from);
            std::map<uint8_t,MeshShaderPriorityGroup> _shaderPriorityGroups;
        };
    } // core
} // crucible

#endif //CRUCIBLE_MESHPASS_H
