#ifndef CRUCIBLE_MATERIAL_H
#define CRUCIBLE_MATERIAL_H
#include "ShaderManager.h"
#include "VirtualUniformBuffer.h"

namespace crucible
{
    namespace core
    {
        struct TextureMaterialIndex
        {
            slag::Texture* texture;
            uint32_t index;
        };

        class Material
        {
        public:
            Material(const std::string& shaderName);
            ~Material();
            Material(const Material&)=delete;
            Material& operator=(const Material&)=delete;
            Material(Material&& from);
            Material& operator=(Material&& from);
            void setData(size_t offset, void* data, uint32_t length);
            void setTexture(const std::string& name, slag::Texture* texture);
            size_t uniformCount()const;
            size_t textureCount() const;
            size_t offsetOfBuffer(const std::string& name);
            std::string uniformBufferName(size_t index);
            std::string textureName(size_t index);
            bool hasUniform(const std::string& name);
            bool hasTexture(const std::string& name);
            ShaderReference& shaderReference();
            slag::DescriptorBundle makeBundle(slag::DescriptorPool* pool, VirtualUniformBuffer* virtualUniformBuffer);
            static void initialize();
            static void cleanup();
        private:
            void move(Material& from);
            ShaderReference _shader;
            std::vector<unsigned char> _data;
            std::unordered_map<std::string,slag::UniformBufferDescriptorLayout*> _uniformBuffers;
            std::vector<std::string> _uniformBufferNames;
            std::unordered_map<std::string, TextureMaterialIndex> _textures;
            std::vector<std::string> _textureNames;
            inline static slag::Sampler* _defaultSampler = nullptr;
        };
    } // core
} // crucible

#endif //CRUCIBLE_MATERIAL_H
