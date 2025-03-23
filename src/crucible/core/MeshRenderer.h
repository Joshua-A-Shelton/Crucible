#ifndef CRUCIBLE_MESHRENDERER_H
#define CRUCIBLE_MESHRENDERER_H
#include "ShaderManager.h"

namespace crucible
{
    namespace core
    {
        struct DrawDescriptorData
        {
            slag::Buffer* buffer;
            size_t offset;
            size_t length;
        };
        class MeshRenderer
        {
        public:
            MeshRenderer(unsigned char* meshData, uint64_t meshDataLength, const ShaderReference& shader, uint8_t priority);
            ~MeshRenderer();
            MeshRenderer(const MeshRenderer&)=delete;
            MeshRenderer& operator=(const MeshRenderer&)=delete;
            MeshRenderer(MeshRenderer&& from);
            MeshRenderer& operator=(MeshRenderer&& from);
            uint8_t priority();
            Mesh* mesh() const;
            ShaderReference& shader();
            void setData(uint32_t offset, void* data, uint32_t length);
            DrawDescriptorData getDrawDescriptorDataAndFlipBufferSides();
        private:
            uint8_t _priority;
            bool _firstHalf=false;
            Mesh* _mesh=nullptr;
            ShaderReference _shader;
            slag::Buffer* _dataBuffer=nullptr;
            char* _rawData = nullptr;
            uint32_t _rawDataLength=0;
        };
    } // core
} // crucible

#endif //${$CUSTOM_INCLUDE_GUARD}
