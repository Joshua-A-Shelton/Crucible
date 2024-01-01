#include "RenderRegistry.h"
#include <glm/gtc/matrix_transform.hpp>

namespace crucible
{
    void RenderRegistry::registerDraw(slag::Shader *shader, Mesh *mesh)
    {
        auto& registry = _meshMap[shader];
        registry.push_back(mesh);
    }

    void RenderRegistry::draw(glm::mat4 projection, slag::Frame* frame)
    {
        auto commandBuffer = frame->getCommandBuffer();
        auto allocator = frame->getUniformSetDataAllocator();
        auto uBuffer = frame->getUniformBuffer();
        for(auto& kvpair : _meshMap)
        {
            commandBuffer->bindShader(kvpair.first);
            //TODO: move outside of this loop, should happen once per draw
            slag::UniformSetData globals(kvpair.first->getUniformSet(0),allocator);
            auto bufferWrite = uBuffer->write(&projection,sizeof projection);
            globals.setUniform(0,bufferWrite);
            commandBuffer->bindUniformSetData(kvpair.first,globals);
            for(auto& mesh : kvpair.second)
            {
                commandBuffer->bindVertexBuffer(mesh->verticies());
                commandBuffer->bindIndexBuffer(mesh->indecies(),slag::GraphicsTypes::UINT16);
                commandBuffer->drawIndexed(mesh->indeciesCount(),1,0,0,0);
            }
        }
        _meshMap.clear();
    }
} // crucible