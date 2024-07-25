#include <stdexcept>
#include "ShaderLibrary.h"

namespace crucible
{
    MaterialShader* ShaderLibrary::getMaterialShader(const std::string& name)
    {
        if(_materialShaders.contains(name))
        {
            return &_materialShaders.at(name);
        }
        return nullptr;
    }

    MaterialShader* ShaderLibrary::loadMaterialShader(const std::string& path, const std::string& name)
    {
        return nullptr;
    }

    MaterialShader* ShaderLibrary::loadMaterialShader(const std::string& vert, const std::string& frag, const std::string& name)
    {
        if(_materialShaders.contains(name))
        {
            throw std::runtime_error("Shader with given name already exists!");
        }
        _materialShaders.insert(std::pair<std::string,MaterialShader>(name,MaterialShader(vert,frag)));
        return &_materialShaders.at(name);
    }

    void ShaderLibrary::unloadShaders()
    {
        _materialShaders.clear();
    }
} // crucible