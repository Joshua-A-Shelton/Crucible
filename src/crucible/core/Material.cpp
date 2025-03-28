#include "Material.h"

namespace crucible
{
    namespace core
    {
        Material::Material(const std::string& shaderName)
        {
            _shader = ShaderManager::getShaderReference(shaderName);
            auto layout = _shader.pipeline()->uniformBufferLayout(2,0);

            if (layout == nullptr)
            {
                throw std::runtime_error("Shader is not a material shader");
            }
            _data = slag::Buffer::newBuffer(layout->size(),slag::Buffer::CPU_AND_GPU,slag::Buffer::UNIFORM_BUFFER);
            _uniformBufferNames.resize(layout->childrenCount());
            for (uint32_t i = 0; i < layout->childrenCount(); i++)
            {
                auto& child = layout[i];
                _uniformBuffers.insert({child.name(),&child});
                _uniformBufferNames[i] = child.name();
            }
            auto group = _shader.pipeline()->descriptorGroup(2);
            for (uint32_t i=0; i< group->descriptorCount(); i++)
            {
                auto& desc = group->descriptor(i);
                if (desc.shape().type == slag::Descriptor::SAMPLER_AND_TEXTURE)
                {
                    _textures.emplace(std::make_pair(desc.name(),TextureMaterialIndex{.texture = nullptr, .index = i}));
                    _textureNames.push_back(desc.name());
                }
            }
        }

        Material::~Material()
        {
            if (_data != nullptr)
            {
                delete _data;
            }
        }

        Material::Material(Material&& from)
        {
            move(from);
        }

        Material& Material::operator=(Material&& from)
        {
            move(from);
            return *this;
        }

        void Material::setData(size_t offset, void* data, uint32_t length)
        {
            if (offset + length > _data->size())
            {
                throw std::runtime_error("Tried to write data out of range");
            }
            _data->update(offset,data,length);
        }

        void Material::setTexture(const std::string& name, slag::Texture* texture)
        {
            auto it = _textures.find(name);
            if (it == _textures.end())
            {
                throw std::runtime_error("No texture slot with name \""+name+"\" exists in this material");
            }
            it->second.texture = texture;
        }

        size_t Material::uniformCount() const
        {
            return _uniformBufferNames.size();
        }

        size_t Material::textureCount() const
        {
            return _textureNames.size();
        }

        size_t Material::offsetOfBuffer(const std::string& name)
        {
            auto it = _uniformBuffers.find(name);
            if (it == _uniformBuffers.end())
            {
                throw std::runtime_error("No uniform buffer \""+name+"\" exists in this material");
            }
            return it->second->absoluteOffset();
        }

        std::string Material::uniformBufferName(size_t index)
        {
            return _uniformBufferNames[index];
        }

        std::string Material::textureName(size_t index)
        {
            return _textureNames[index];
        }

        bool Material::hasUniform(const std::string& name)
        {
            return _uniformBuffers.find(name) != _uniformBuffers.end();
        }

        bool Material::hasTexture(const std::string& name)
        {
            return  _textures.find(name) != _textures.end();
        }

        ShaderReference& Material::shaderReference()
        {
            return _shader;
        }

        slag::DescriptorBundle Material::makeBundle(slag::DescriptorPool* pool)
        {
            auto bundle = pool->makeBundle(_shader.pipeline()->descriptorGroup(2));
            bundle.setUniformBuffer(0,0,_data,0,_data->size());
            for (auto& textureData: _textures)
            {
                bundle.setSamplerAndTexture(textureData.second.index,0,textureData.second.texture,slag::Texture::SHADER_RESOURCE,_defaultSampler);
            }
            return bundle;
        }

        void Material::initialize()
        {
            slag::SamplerBuilder sb;
            _defaultSampler = sb.newSampler();
        }

        void Material::cleanup()
        {
            delete _defaultSampler;
        }

        void Material::move(Material& from)
        {
            _shader = std::move(from._shader);
            std::swap(_data,from._data);
            _uniformBuffers.swap(from._uniformBuffers);
            _uniformBufferNames.swap(from._uniformBufferNames);
            _textures.swap(from._textures);
            _textureNames.swap(from._textureNames);
        }
    } // core
} // crucible
