#include "MeshShaderBlock.h"

namespace crucible
{
    namespace core
    {
        MeshData::MeshData(Mesh* mesh, slag::DescriptorBundle&& materialBundle, slag::DescriptorBundle&& instanceBundle): _materialBundle(std::move(materialBundle)), _instanceBundle(std::move(instanceBundle))
        {
            _mesh=mesh;
        }

        MeshData::MeshData(MeshData&& other): _materialBundle(std::move(other._materialBundle)), _instanceBundle(std::move(other._instanceBundle))
        {
            std::swap(_mesh,other._mesh);
        }

        MeshData& MeshData::operator=(MeshData&& from)
        {
            std::swap(_mesh,from._mesh);
            _instanceBundle = std::move(from._instanceBundle);
            _materialBundle = std::move(from._materialBundle);
            return *this;
        }

        Mesh* MeshData::mesh() const
        {
            return _mesh;
        }

        slag::DescriptorBundle& MeshData::materialBundle()
        {
            return _materialBundle;
        }

        slag::DescriptorBundle& MeshData::instanceBundle()
        {
            return _instanceBundle;
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

        void MeshShaderBlock::registerMeshData(Mesh* mesh,slag::DescriptorBundle&& materialBundle, slag::DescriptorBundle&& instanceBundle)
        {
            _drawData.emplace_back(mesh, std::move(materialBundle), std::move(instanceBundle));
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
                    commandBuffer->bindGraphicsDescriptorBundle(_shader.pipeline(),2,meshData.materialBundle());
                    commandBuffer->bindGraphicsDescriptorBundle(_shader.pipeline(),3,meshData.instanceBundle());
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
