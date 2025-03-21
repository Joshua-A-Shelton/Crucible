#include "RenderData.h"

namespace crucible
{
    namespace core
    {
        RenderData::RenderData(Mesh* mesh, slag::ShaderPipeline* shader, slag::DescriptorBundle&& bundle):_instanceData(std::move(bundle))
        {
            _mesh = mesh;
            _shader = shader;
        }

        RenderData::RenderData(RenderData&& from):_instanceData(std::move(from._instanceData))
        {
            _mesh = from._mesh;
            _shader = from._shader;
        }

        RenderData& RenderData::operator=(RenderData&& from)
        {
            move(from);
            return *this;
        }

        void RenderData::draw(slag::CommandBuffer* commandBuffer, const std::vector<Mesh::VertexAttribute>& attributes)
        {
            commandBuffer->bindGraphicsDescriptorBundle(_shader,2,_instanceData);
            for (size_t i = 0; i < attributes.size(); ++i)
            {
                auto buffer = _mesh->getBuffer(attributes[i]);
                size_t offset = 0;
                size_t size = buffer->size();
                size_t stride = Mesh::attributeSize(attributes[i]);

                //TODO: not sure if should bind all at once, or iteratively, but I don't really want to allocate memory for every draw call, so iterative for now
                commandBuffer->bindVertexBuffers(i,&buffer,&offset,&size,&stride,1);
            }

            commandBuffer->bindIndexBuffer(_mesh->indexBuffer(),_mesh->indexSize(),0);
            commandBuffer->drawIndexed(_mesh->indexCount(),1,0,0,0);

        }

        void RenderData::move(RenderData& from)
        {
            _mesh = from._mesh;
            _shader = from._shader;
            _instanceData = std::move(from._instanceData);
        }
    } // core
} // crucible
