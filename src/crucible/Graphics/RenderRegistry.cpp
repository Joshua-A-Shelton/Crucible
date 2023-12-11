#include "RenderRegistry.h"

namespace crucible
{
    void RenderRegistry::registerDraw(slag::Shader *shader, Mesh *mesh)
    {
        auto& registry = _meshMap[shader];
        registry.push_back(mesh);
    }

    void RenderRegistry::draw(slag::CommandBuffer *commandBuffer)
    {
        for(auto& kvpair : _meshMap)
        {
            auto cbuffer = commandBuffer->createSubCommandBuffer();
            for(auto& mesh : kvpair.second)
            {
                cbuffer->bindVertexBuffer(mesh->verticies());
                cbuffer->bindIndexBuffer(mesh->indecies(),slag::GraphicsTypes::UINT16);
                cbuffer->drawIndexed(mesh->indeciesCount(),1,0,0,0);
            }
            commandBuffer->addSubCommandBuffer(cbuffer);
        }
        _meshMap.clear();
    }
} // crucible