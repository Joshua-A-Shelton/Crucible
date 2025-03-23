#include "MeshShaderBlock.h"

namespace crucible
{
    namespace core
    {
        MeshData::MeshData(Mesh* mesh, slag::DescriptorBundle&& bundle): _bundle(std::move(bundle))
        {
            _mesh=mesh;
        }

        MeshData::MeshData(MeshData&& other): _bundle(std::move(other._bundle))
        {
            std::swap(_mesh,other._mesh);
        }

        MeshData& MeshData::operator=(MeshData&& from)
        {
            std::swap(_mesh,from._mesh);
            _bundle = std::move(from._bundle);
            return *this;
        }

        Mesh* MeshData::mesh() const
        {
            return _mesh;
        }

        slag::DescriptorBundle& MeshData::descriptorBundle()
        {
            return _bundle;
        }

        MeshShaderBlock::MeshShaderBlock(const ShaderReference& shader)
        {
            _shader=shader;
        }

        MeshShaderBlock::MeshShaderBlock(MeshShaderBlock&& from)
        {
            move(from);
        }

        MeshShaderBlock& MeshShaderBlock::operator=(MeshShaderBlock&& from)
        {
            move(from);
            return *this;
        }

        void MeshShaderBlock::registerMeshData(Mesh* mesh, slag::DescriptorBundle&& descriptorData)
        {
            _drawData.emplace_back(mesh, std::move(descriptorData));
        }

        void MeshShaderBlock::draw(slag::CommandBuffer* commandBuffer)
        {
            if (!_drawData.empty())
            {
                commandBuffer->bindGraphicsShader(_shader.pipeline());
                std::vector<slag::Buffer*> vertexBuffers(_shader.requiredAttributesCount(),nullptr);
                std::vector<size_t> offsets(_shader.requiredAttributesCount(),0);
                std::vector<size_t> sizes(_shader.requiredAttributesCount(),0);
                std::vector<size_t> strides(_shader.requiredAttributesCount(),0);
                for (size_t i=0; i< _shader.requiredAttributesCount(); i++)
                {
                    strides[i] = Mesh::attributeSize(_shader.attribute(i));
                }
                for(auto& meshData : _drawData)
                {
                    auto mesh = meshData.mesh();

                    for (size_t i=0; i< _shader.requiredAttributesCount(); i++)
                    {
                        auto buffer = mesh->getBuffer(_shader.attribute(i));
                        vertexBuffers[i] = buffer;
                        sizes[i] = buffer->size();
                    }
                    commandBuffer->bindVertexBuffers(0,vertexBuffers.data(),offsets.data(),sizes.data(),strides.data(),_shader.requiredAttributesCount());
                    commandBuffer->bindIndexBuffer(mesh->indexBuffer(),mesh->indexSize(),0);
                    commandBuffer->bindGraphicsDescriptorBundle(_shader.pipeline(),2,meshData.descriptorBundle());
                    commandBuffer->drawIndexed(mesh->indexCount(),1,0,0,0);
                }
                _drawData.clear();
            }
        }

        void MeshShaderBlock::move(MeshShaderBlock& from)
        {
            _shader = std::move(from._shader);
            _drawData.swap(from._drawData);
        }
    } // core
} // crucible
