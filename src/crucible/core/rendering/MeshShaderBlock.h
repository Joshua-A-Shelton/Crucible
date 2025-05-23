#ifndef CRUCIBLE_SHADERBLOCK_H
#define CRUCIBLE_SHADERBLOCK_H
#include "crucible/core/ShaderManager.h"

namespace crucible
{
    namespace core
    {
        class MeshData
        {
        public:
            MeshData(Mesh* mesh, slag::DescriptorBundle&& materialBundle, slag::DescriptorBundle&& instanceBundle);
            ~MeshData()=default;
            MeshData(const MeshData&)=delete;
            MeshData& operator=(const MeshData&)=delete;
            MeshData(MeshData&& other);
            MeshData& operator=(MeshData&& from);
            Mesh* mesh()const;
            slag::DescriptorBundle& materialBundle();
            slag::DescriptorBundle& instanceBundle();
        private:
            Mesh* _mesh=nullptr;
            slag::DescriptorBundle _materialBundle;
            slag::DescriptorBundle _instanceBundle;
        };

        class MeshShaderBlock
        {
        public:
            explicit MeshShaderBlock(const ShaderReference& shader);
            ~MeshShaderBlock()=default;
            MeshShaderBlock(const MeshShaderBlock&)=delete;
            MeshShaderBlock& operator=(const MeshShaderBlock&)=delete;
            MeshShaderBlock(MeshShaderBlock&& from);
            MeshShaderBlock& operator=(MeshShaderBlock&& from);
            void registerMeshData(Mesh* mesh,slag::DescriptorBundle&& materialBundle, slag::DescriptorBundle&& instanceBundle);
            void draw(slag::CommandBuffer* commandBuffer);
        private:
            void move(MeshShaderBlock& from);
            ShaderReference _shader;
            std::vector<MeshData> _drawData;
        };
    } // core
} // crucible

#endif //${$CUSTOM_INCLUDE_GUARD}
