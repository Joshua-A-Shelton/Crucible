#include "ShaderManager.h"

#include <bitset>
#include <fstream>
#include <crucible/serialization/Serializer.h>

#include "Mesh.h"
#include "slag/ShaderPipeline.h"

using namespace crucible::serialization;

namespace crucible
{
    namespace core
    {
        ShaderUnit::ShaderUnit(slag::ShaderPipeline* pipeline,const std::string& name, const std::vector<Mesh::VertexAttribute>& attributes)
        {
            _pipeline = pipeline;
            _referenceCount = 0;
            _name = name;
            _requiredAttributes = attributes;
        }

        ShaderUnit::~ShaderUnit()
        {
            delete _pipeline;
        }

        ShaderReference::ShaderReference(ShaderUnit* unit)
        {
            _unit = unit;
            unit->_referenceCount++;
        }

        void ShaderReference::copy(const ShaderReference& other)
        {
            _unit = other._unit;
            _unit->_referenceCount++;
        }

        void ShaderReference::move(ShaderReference& other)
        {
            std::swap(_unit, other._unit);
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

        ShaderReference::ShaderReference(ShaderReference&& ref)
        {
            move(ref);
        }

        ShaderReference& ShaderReference::operator=(ShaderReference&& ref)
        {
            move(ref);
            return *this;
        }

        ShaderReference::~ShaderReference()
        {
            if (_unit != nullptr)
            {
                _unit->_referenceCount--;
                if (_unit->_referenceCount == 0)
                {
                    ShaderManager::destroyShader(_unit);
                }
            }
        }

        slag::ShaderPipeline* ShaderReference::pipeline()
        {
            return _unit->_pipeline;
        }

        size_t ShaderReference::requiredAttributesCount()
        {
            return _unit->_requiredAttributes.size();
        }

        Mesh::VertexAttribute ShaderReference::attribute(const size_t attributeIndex)
        {
            return _unit->_requiredAttributes[attributeIndex];
        }

        ShaderReference ShaderManager::getShaderReference(const std::string& name)
        {
            std::lock_guard<std::mutex> lock(_compiledShadersMutex);
            auto shader = _compiledShaders.find(name);
            if (shader==_compiledShaders.end())
            {
                std::filesystem::path shaderFolder = "shaders";
                auto shaderFile = shaderFolder/(name + ".csbf");
                auto unit = buildShader(shaderFile,name);
                shader = _compiledShaders.insert({name,unit}).first;
            }

            return ShaderReference(&shader->second);
        }

        ShaderUnit ShaderManager::buildShader(std::filesystem::path path, const std::string& name)
        {

            slag::ShaderPipeline* pipeline = nullptr;

            std::ifstream shaderFile(path);
            if (!shaderFile.is_open())
            {
                throw std::runtime_error("Unable to open "+path.string());
            }
            auto magicNumber = Serializer::read(shaderFile,5);

            if (magicNumber!="csbf\n")
            {
                throw std::runtime_error(path.string()+" is not a valid shader bundle file");
            }
            try
            {
                //Read output buffer formats
                auto frameBufferDescription = getFrameBufferDescription(shaderFile);

                //Read vertex format
                auto vertexInfo = getVertexProperties(shaderFile);

                //Read properties
                auto properties = getShaderProperties(shaderFile);

                //Read modules
                auto shaderModules = getShaderModules(shaderFile);

                pipeline = slag::ShaderPipeline::newShaderPipeline(shaderModules.data(),shaderModules.size(),nullptr,0,properties,&vertexInfo.description,frameBufferDescription);
                ShaderUnit shaderUnit(pipeline,name,vertexInfo.attributes);
                return shaderUnit;
            }
            catch (const std::exception& e)
            {
                throw std::runtime_error("An error occurred trying to load shader: "+path.string()+"\n"+e.what());
            }
        }

        void ShaderManager::destroyShader(ShaderUnit* shader)
        {
            std::lock_guard<std::mutex> lock(_compiledShadersMutex);
            _compiledShaders.erase(shader->_name);
        }

        slag::FrameBufferDescription ShaderManager::getFrameBufferDescription(std::ifstream& shaderFile)
        {
            slag::FrameBufferDescription frameBufferDescription;
            std::string line;
            while (static_cast<char>(shaderFile.peek())=='#')
            {
                std::getline(shaderFile,line);
                if (line.length()<9)
                {
                    throw std::runtime_error("Shader file is corrupt, invalid description of render targets");
                }
                auto substr = line.substr(0,7);
                auto value = line.substr(7,line.length()-7);
                auto format = getFormat(value);
                if (format == slag::Pixels::UNDEFINED)
                {
                    throw std::runtime_error("Undefined format is not valid for render target");
                }
                if (substr=="#color ")
                {
                    frameBufferDescription.addColorTarget(format);
                }
                else if (line.substr(0,8)=="#depth ")
                {
                    frameBufferDescription.setDepthTarget(format);
                }
            }
            if (frameBufferDescription.colorTargetCount()==0 && frameBufferDescription.depthFormat()==slag::Pixels::UNDEFINED)
            {
                throw std::runtime_error("Frame buffer description must be defined");
            }
            return frameBufferDescription;
        }

        ShaderManager::VertexInfo ShaderManager::getVertexProperties(std::ifstream& shaderFile)
        {
            auto vertexRequirements = Serializer::readUInt16(shaderFile);
            auto channels = std::popcount(vertexRequirements);
            size_t currentChannel = 0;
            slag::VertexDescription vertexDescription(channels);
            std::vector<Mesh::VertexAttribute> attributes;

            if (vertexRequirements & Mesh::attributeSerializeIndex(Mesh::POSITION_3D))
            {
                vertexDescription.add(slag::VertexAttribute(slag::GraphicsTypes::VECTOR3,0),currentChannel);
                attributes.push_back(Mesh::POSITION_3D);
                currentChannel++;
            }
            if (vertexRequirements & Mesh::attributeSerializeIndex(Mesh::POSITION_2D))
            {
                vertexDescription.add(slag::VertexAttribute(slag::GraphicsTypes::VECTOR2,0),currentChannel);
                attributes.push_back(Mesh::POSITION_2D);
                currentChannel++;
            }
            if (vertexRequirements & Mesh::attributeSerializeIndex(Mesh::NORMAL))
            {
                vertexDescription.add(slag::VertexAttribute(slag::GraphicsTypes::VECTOR3,0),currentChannel);
                attributes.push_back(Mesh::NORMAL);
                currentChannel++;
            }
            if (vertexRequirements & Mesh::attributeSerializeIndex(Mesh::UV))
            {
                vertexDescription.add(slag::VertexAttribute(slag::GraphicsTypes::VECTOR2,0),currentChannel);
                attributes.push_back(Mesh::UV);
                currentChannel++;
            }
            if (vertexRequirements & Mesh::attributeSerializeIndex(Mesh::COLOR))
            {
                vertexDescription.add(slag::VertexAttribute(slag::GraphicsTypes::GraphicsType::BOOLEAN_VECTOR4,0),currentChannel);
                attributes.push_back(Mesh::COLOR);
                currentChannel++;
            }
            return ShaderManager::VertexInfo{vertexDescription,attributes};
        }

        slag::ShaderProperties ShaderManager::getShaderProperties(std::ifstream& shaderFile)
        {
            //TODO: actually set properties
            return slag::ShaderProperties();
        }

        std::vector<slag::ShaderModule> ShaderManager::getShaderModules(std::ifstream& shaderFile)
        {
            std::vector<slag::ShaderModule> modules;

            std::string moduleType;
            slag::ShaderStages stage = slag::ShaderStageFlags::VERTEX;
            while (true)
            {
                char nextChar=0;
                shaderFile.read(&nextChar,1);
                if (shaderFile.eof())
                {
                    break;
                }
                if (iswspace(nextChar))
                {
                    continue;
                }

                if (nextChar!=':')
                {
                    moduleType+=nextChar;
                }
                else
                {

                    if (moduleType == "vertex")
                    {
                        stage = slag::ShaderStageFlags::VERTEX;
                    }
                    else if (moduleType == "fragment")
                    {
                        stage = slag::ShaderStageFlags::FRAGMENT;
                    }
                    else
                    {
                        throw std::runtime_error("Unrecognized shader stage: "+moduleType);
                    }

                    auto shaderLength = Serializer::readUInt32(shaderFile);
                    shaderFile.read(&nextChar,1); //\n
                    auto shaderBytes = Serializer::readBytes(shaderFile,shaderLength);
                    modules.emplace_back(stage,shaderBytes.data(),shaderBytes.size());
                    moduleType = "";
                }
            }
            return modules;
        }

        slag::Pixels::Format ShaderManager::getFormat(const std::string& fromText)
        {
#define DEFINITION(SlagName, DxName, VulkanName, VkImageAspectFlags, VkComponentSwizzle_r, VkComponentSwizzle_g, VkComponentSwizzle_b, VkComponentSwizzle_a, totalBits) if(fromText==#SlagName){return slag::Pixels::SlagName;}
            TEXTURE_FORMAT_DEFINTITIONS(DEFINITION)
#undef DEFINITION
            throw std::runtime_error("Unrecognized format: "+fromText);
        }
    } // core
} // crucible