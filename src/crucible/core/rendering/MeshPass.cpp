#include "MeshPass.h"

namespace crucible
{
    namespace core
    {
        MeshShaderPriorityGroup::MeshShaderPriorityGroup()
        {
        }

        MeshShaderPriorityGroup::MeshShaderPriorityGroup(MeshShaderPriorityGroup&& from)
        {
            move(from);
        }

        MeshShaderPriorityGroup& MeshShaderPriorityGroup::operator=(MeshShaderPriorityGroup&& from)
        {
            move(from);
            return *this;
        }

        void MeshShaderPriorityGroup::registerRenderable(const ShaderReference& shader, Mesh* mesh, slag::DescriptorBundle&& materialBundle, slag::DescriptorBundle&& instanceBundle)
        {
            auto it = _shaderBlocks.find(shader.pipeline());
            if (it==_shaderBlocks.end())
            {
                it = _shaderBlocks.emplace(shader.pipeline(),std::move(MeshShaderBlock(shader))).first;
            }
            it->second.registerMeshData(mesh,std::move(materialBundle),std::move(instanceBundle));
        }

        void MeshShaderPriorityGroup::drawMeshes(slag::CommandBuffer* commandBuffer)
        {
            for (auto& shaderBlock : _shaderBlocks)
            {
                shaderBlock.second.draw(commandBuffer);
            }
        }

        void MeshShaderPriorityGroup::move(MeshShaderPriorityGroup& from)
        {
            _shaderBlocks.swap(from._shaderBlocks);
        }

        MeshPass::MeshPass()
        {
        }

        MeshPass::MeshPass(MeshPass&& from)
        {
            move(from);
        }

        MeshPass& MeshPass::operator=(MeshPass&& from)
        {
            move(from);
            return *this;
        }

        void MeshPass::registerMeshData(uint8_t priority, const ShaderReference& shader, Mesh* mesh, slag::DescriptorBundle&& materialBundle, slag::DescriptorBundle&& instanceBundle)
        {
            auto it = _shaderPriorityGroups.find(priority);
            if (it==_shaderPriorityGroups.end())
            {
                it = _shaderPriorityGroups.emplace(priority,MeshShaderPriorityGroup()).first;
            }

            it->second.registerRenderable(shader,mesh,std::move(materialBundle),std::move(instanceBundle));
        }

        void MeshPass::drawMeshes(slag::CommandBuffer* commandBuffer)
        {
            for (auto& group : _shaderPriorityGroups)
            {
                group.second.drawMeshes(commandBuffer);
            }
        }

        void MeshPass::move(MeshPass& from)
        {
            _shaderPriorityGroups.swap(from._shaderPriorityGroups);
        }
    } // core
} // crucible
