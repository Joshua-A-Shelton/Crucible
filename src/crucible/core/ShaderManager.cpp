#include "ShaderManager.h"

#include <bitset>
#include <fstream>
#include <boost/endian/conversion.hpp>

#include "Mesh.h"
#include "slag/ShaderPipeline.h"

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
            if (_pipeline)
            {
                delete _pipeline;
            }
        }

        ShaderUnit::ShaderUnit(ShaderUnit&& other)
        {
            move(other);
        }

        ShaderUnit& ShaderUnit::operator=(ShaderUnit&& other)
        {
            move(other);
            return *this;
        }

        void ShaderUnit::move(ShaderUnit& other)
        {
            std::swap(_referenceCount,other._referenceCount);
            std::swap(_pipeline,other._pipeline);
            std::swap(_name,other._name);
            std::swap(_requiredAttributes,other._requiredAttributes);
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
                shader = _compiledShaders.insert({name,buildShader(shaderFile,name)}).first;
            }

            return ShaderReference(&shader->second);
        }

        ShaderUnit ShaderManager::buildShader(std::filesystem::path path, const std::string& name)
        {

            slag::ShaderPipeline* pipeline = nullptr;

            std::ifstream shaderFile(path,std::ios::in | std::ios::binary);
            if (!shaderFile.is_open())
            {
                throw std::runtime_error("Unable to open "+path.string());
            }

            shaderFile.seekg(0,std::ios::end);
            size_t size = shaderFile.tellg();
            shaderFile.seekg(0,std::ios::beg);
            std::vector<unsigned char> data(size);
            shaderFile.read(std::bit_cast<char*>(data.data()), size);
            shaderFile.close();

            unsigned char* bytePointer = &data[0];

            auto magicNumber = std::string(reinterpret_cast<char*>(bytePointer),5);
            bytePointer+=5;

            if (magicNumber!="csbf\n")
            {
                throw std::runtime_error(path.string()+" is not a valid shader bundle file");
            }
            try
            {
                //Read output buffer formats
                auto frameBufferDescription = getFrameBufferDescription(bytePointer,&*data.end());

                //Read vertex format
                auto vertexInfo = getVertexProperties(bytePointer,&*data.end());

                //Read properties
                auto properties = getShaderProperties(bytePointer,&*data.end());

                //Read modules
                auto shaderModules = getShaderModules(bytePointer,&*data.end());

                pipeline = slag::ShaderPipeline::newShaderPipeline(shaderModules.data(),shaderModules.size(),nullptr,0,properties,&vertexInfo.description,frameBufferDescription);
                return ShaderUnit(pipeline,name,vertexInfo.attributes);
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

        slag::FrameBufferDescription ShaderManager::getFrameBufferDescription(unsigned char*& pointer, const unsigned char* eof)
        {
            slag::FrameBufferDescription frameBufferDescription;
            std::string line;
            while (*std::bit_cast<char*>(pointer) == '#')
            {
                while (pointer < eof && *pointer != '\n')
                {
                    line+=*std::bit_cast<char*>(pointer);
                    pointer++;
                }
                pointer++;
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
                line = "";
            }
            if (frameBufferDescription.colorTargetCount()==0 && frameBufferDescription.depthFormat()==slag::Pixels::UNDEFINED)
            {
                throw std::runtime_error("Frame buffer description must be defined");
            }
            return frameBufferDescription;
        }

        ShaderManager::VertexInfo ShaderManager::getVertexProperties(unsigned char*& pointer, const unsigned char* eof)
        {
            if (!(pointer+sizeof(uint16_t)< eof))
            {
                throw std::runtime_error("Unexpected end of file");
            }
            uint16_t vertexRequirements = *reinterpret_cast<uint16_t*>(pointer);
            pointer+=sizeof(uint16_t);
            boost::endian::little_to_native_inplace(vertexRequirements);
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

        slag::ShaderProperties ShaderManager::getShaderProperties(unsigned char*& pointer, const unsigned char* eof)
        {
            //TODO: actually set properties
            return slag::ShaderProperties();
        }

        std::vector<slag::ShaderModule> ShaderManager::getShaderModules(unsigned char*& pointer, const unsigned char* eof)
        {
            std::vector<slag::ShaderModule> modules;

            std::string moduleType;
            slag::ShaderStages stage = slag::ShaderStageFlags::VERTEX;
            while (pointer < eof)
            {
                char nextChar=*reinterpret_cast<char*>(pointer);
                pointer+=sizeof(char);

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


                    if (!(pointer+sizeof(uint32_t)<eof))
                    {
                        throw std::runtime_error("Unexpected end of file");
                    }

                    uint32_t shaderLength = *reinterpret_cast<uint32_t*>(pointer);
                    pointer+=sizeof(uint32_t);
                    boost::endian::little_to_native_inplace(shaderLength);

                    if (!(pointer+shaderLength<=eof))
                    {
                        throw std::runtime_error("Unexpected end of file");
                    }
                    std::vector<char> shaderBytes(shaderLength);
                    memcpy(shaderBytes.data(), pointer, shaderLength);
                    pointer+=shaderLength;
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