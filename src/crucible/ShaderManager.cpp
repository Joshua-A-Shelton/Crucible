#include "ShaderManager.h"

#include <fstream>

namespace crucible
{
    std::unordered_map<std::string, ShaderUnit> SHADER_MANAGER_COMPILED_SHADERS;
    std::mutex SHADER_MANAGER_COMPILED_SHADERS_MUTEX;

    ShaderUnit::ShaderUnit(slag::ShaderPipeline* pipeline, const std::string& name,Mesh::VertexAttributeFlags attributes)
    {
        _pipeline = pipeline;
        _referenceCount = 0;
        _name = name;
        _attributes = Mesh::toList(attributes);
    }

    ShaderUnit::~ShaderUnit()
    {
        if (_pipeline)
        {
            delete _pipeline;
        }
    }


    ShaderUnit::ShaderUnit(ShaderUnit&& from)
    {
        move(from);
    }

    ShaderUnit& ShaderUnit::operator=(ShaderUnit&& from)
    {
        move(from);
        return *this;
    }

    void ShaderUnit::move(ShaderUnit& from)
    {
        std::swap(_referenceCount,from._referenceCount);
        std::swap(_pipeline,from._pipeline);
        std::swap(_name,from._name);
        std::swap(_attributes,from._attributes);
    }

    ShaderReference::ShaderReference(ShaderUnit* unit)
    {
        _unit = unit;
        unit->_referenceCount++;
    }

    void ShaderReference::copy(const ShaderReference& from)
    {
        _unit = from._unit;
        _unit->_referenceCount++;
    }

    void ShaderReference::move(ShaderReference& from)
    {
        std::swap(_unit, from._unit);
    }

    ShaderReference::ShaderReference(const ShaderReference& ref)
    {
        copy(ref);
    }

    ShaderReference& ShaderReference::operator=(const ShaderReference& ref)
    {
        copy(ref);
        return *this;
    }

    ShaderReference::ShaderReference(ShaderReference&& from)
    {
        move(from);
    }

    ShaderReference& ShaderReference::operator=(ShaderReference&& from)
    {
        move(from);
        return *this;
    }

    ShaderReference::~ShaderReference()
    {
        if (_unit != nullptr)
        {
            std::lock_guard<std::mutex> lock(SHADER_MANAGER_COMPILED_SHADERS_MUTEX);
            _unit->_referenceCount--;
            if (_unit->_referenceCount == 0)
            {
                SHADER_MANAGER_COMPILED_SHADERS.erase(_unit->_name);
            }
        }
    }

    ShaderReference ShaderManager::getShader(const char* name, void(*createShader)(const char* shaderPath,const char* shaderName,ShaderCreateResult(*nativeAddShader)(const char* name,Mesh::VertexAttributeFlags attributes, unsigned char** shaderCodeArray, uint32_t* shaderCodeLengthsArray, slag::ShaderStageFlags* stages, uint32_t shaderCount,slag::ShaderProperties* properties, slag::FrameBufferDescription* framebufferDescription)))
    {
        std::lock_guard<std::mutex> lock(SHADER_MANAGER_COMPILED_SHADERS_MUTEX);
        auto shader = SHADER_MANAGER_COMPILED_SHADERS.find(name);
        if (shader==SHADER_MANAGER_COMPILED_SHADERS.end())
        {
            std::filesystem::path shaderFolder = "shaders";
            std::string fileName = name;
            auto shaderFile = shaderFolder/(fileName + ".cshdr");
            createShader(shaderFile.string().c_str(),name,addShader);
            shader = SHADER_MANAGER_COMPILED_SHADERS.find(name);
            if (shader==SHADER_MANAGER_COMPILED_SHADERS.end())
            {
                throw std::runtime_error("Failed to load shader");
            }

        }
        return ShaderReference(&shader->second);
    }

    ShaderManager::ShaderCreateResult ShaderManager::addShader(const char* name, Mesh::VertexAttributeFlags attributes,
        unsigned char** shaderCodeArray, uint32_t* shaderCodeLengthsArray, slag::ShaderStageFlags* stages,
        uint32_t shaderCount, slag::ShaderProperties* shaderProperties, slag::FrameBufferDescription* framebufferDescription)
    {
        std::vector<slag::ShaderCode> code;
        std::vector<slag::ShaderCode*> codePtrs;
        for (uint32_t i = 0; i < shaderCount; i++)
        {
            code.emplace_back(stages[i],slag::ShaderCode::CodeLanguage::SPIRV,shaderCodeArray[i],shaderCodeLengthsArray[i]);
            codePtrs.emplace_back(&(code[i]));
        }
        slag::VertexDescription vertexDescription = Mesh::createVertexDescription(attributes);

        auto pipeline = slag::ShaderPipeline::newShaderPipeline(codePtrs.data(),shaderCount,*shaderProperties,vertexDescription,*framebufferDescription);
        try
        {
            SHADER_MANAGER_COMPILED_SHADERS.emplace(name,ShaderUnit(pipeline,name,attributes));
        }
        catch (...)
        {
            delete pipeline;
            return ShaderCreateResult::FAILED_COMPILATION;
        }
        return ShaderCreateResult::SUCCESS;
    }
} // crucible
