#include "ShaderManager.h"

#include <bitset>
#include <fstream>
#include <iostream>
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

        bool ShaderReference::operator==(const ShaderReference& other) const
        {
            return _unit->_pipeline == other._unit->_pipeline;
        }

        slag::ShaderPipeline* ShaderReference::pipeline()const
        {
            return _unit->_pipeline;
        }

        size_t ShaderReference::requiredAttributesCount()const
        {
            return _unit->_requiredAttributes.size();
        }

        Mesh::VertexAttribute ShaderReference::attribute(const size_t attributeIndex)const
        {
            return _unit->_requiredAttributes[attributeIndex];
        }

        const std::vector<Mesh::VertexAttribute>& ShaderReference::requiredAttributes() const
        {
            return _unit->_requiredAttributes;
        }

        bool ShaderReference::hasAttribute(Mesh::VertexAttribute attribute) const
        {
            return std::find(_unit->_requiredAttributes.begin(),_unit->_requiredAttributes.end(),attribute)!=_unit->_requiredAttributes.end();
        }

        size_t ShaderReference::hashFunction::operator()(const ShaderReference& ref) const
        {
            return std::hash<std::string>()(ref._unit->_name);
        }

        ShaderReference ShaderManager::getShaderReference(const std::string& name)
        {
            std::lock_guard<std::mutex> lock(_compiledShadersMutex);
            auto shader = _compiledShaders.find(name);
            if (shader==_compiledShaders.end())
            {
                std::filesystem::path shaderFolder = "shaders";
                auto shaderFile = shaderFolder/(name + ".cshdr");
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

            auto magicNumber = std::string(reinterpret_cast<char*>(bytePointer),6);
            bytePointer+=6;

            if (magicNumber!="cshdr\n")
            {
                throw std::runtime_error(path.string()+" is not a valid crucible shader file");
            }
            try
            {
                auto last = &*(data.end()-1);

                std::vector<slag::DescriptorGroup*> descriptorGroups;
                getDescriptorGroups(bytePointer,last,descriptorGroups);

                slag::ShaderProperties properties{};
                VertexInfo vertexInfo(slag::VertexDescription(0),std::vector<Mesh::VertexAttribute>({}));
                slag::FrameBufferDescription frameBufferDescription;
                std::vector<slag::ShaderModule> shaderModules;
                getShaderModules(bytePointer,last,shaderModules,properties,vertexInfo,frameBufferDescription);

                properties.rasterizationState.culling = slag::RasterizationState::NONE;

                //only pass in maximum of 2 descriptor groups (global/view), we can get the others from reflection, and it will have more specific information than what we can read from the file defined groups
                pipeline = slag::ShaderPipeline::newShaderPipeline(shaderModules.data(),std::min(shaderModules.size(),(size_t)2),descriptorGroups.data(),descriptorGroups.size(),properties,&vertexInfo.description,frameBufferDescription);
                for (auto i=0; i<descriptorGroups.size(); i++)
                {
                    delete descriptorGroups[i];
                }
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

        void ShaderManager::getDescriptorGroups(unsigned char*& currentByte, const unsigned char* endByte, std::vector<slag::DescriptorGroup*>& descriptorGroups)
        {
            uint8_t descriptorGroupCount = *currentByte;
            currentByte++;
            for(size_t i=0; i<descriptorGroupCount; i++)
            {
                uint8_t descriptorCount = *currentByte;
                currentByte++;
                std::vector<slag::Descriptor> descriptors(descriptorCount);
                for (auto i=0; i< descriptorCount; i++)
                {
                    std::string name = (char*)currentByte;
                    currentByte+=name.size()+1;
                    if (currentByte>=endByte)
                    {
                        throw std::runtime_error("Descriptor name is malformed");
                    }
                    uint32_t index = *((uint32_t*)currentByte);
                    currentByte+=sizeof(uint32_t);
                    if constexpr (std::endian::native==std::endian::big)
                    {
                        boost::endian::little_to_native_inplace(index);
                    }
                    uint8_t typeValue = *currentByte;
                    currentByte++;
                    slag::Descriptor::DescriptorType type = static_cast<slag::Descriptor::DescriptorType>(typeValue);
                    uint32_t count = *((uint32_t*)currentByte);
                    currentByte+=sizeof(uint32_t);
                    if constexpr (std::endian::native==std::endian::big)
                    {
                        boost::endian::little_to_native_inplace(count);
                    }
                    slag::Descriptor descriptor(name,type,count,index,slag::ShaderStageFlags::VERTEX|slag::ShaderStageFlags::FRAGMENT);
                    descriptors[i] = descriptor;

                }

                descriptorGroups.push_back(slag::DescriptorGroup::newDescriptorGroup(descriptors.data(),descriptors.size()));
            }
        }


        void ShaderManager::getShaderModules(unsigned char* startingByte, const unsigned char *endByte, std::vector<slag::ShaderModule>& shaderModules,slag::ShaderProperties& properties,VertexInfo& vertexDesc, slag::FrameBufferDescription& frameBufferDesc)
        {
            unsigned char* currentByte = startingByte;
            while (currentByte<endByte)
            {
                nextModule(currentByte,endByte,shaderModules,properties,vertexDesc,frameBufferDesc);

                uintptr_t cb = (uintptr_t)currentByte;
                uintptr_t eb = (uintptr_t)endByte;
                if (currentByte>endByte)
                {
                    throw std::runtime_error("Exceeded end of shader data");
                }
                currentByte++;
            }
        }

        void ShaderManager::nextModule(unsigned char*& currentByte, const unsigned char* endByte,std::vector<slag::ShaderModule>& shaderModules,slag::ShaderProperties& properties,VertexInfo& vertexDesc, slag::FrameBufferDescription& frameBufferDesc)
        {
            auto startByte = currentByte;
            size_t length = 0;
            while (*currentByte!=':')
            {
                currentByte++;
                length++;
                if (currentByte>=endByte)
                {
                    throw std::runtime_error("Exceeded end of shader data");
                }
            }
            std::string stageName((char*)startByte,length);
            auto stage = getStage(stageName);
            currentByte++;
            while (isspace(*currentByte))
            {
                currentByte++;
            }
            if (*currentByte!='<')
            {
                throw std::runtime_error("Shader stage parameters malformed");
            }
            currentByte++;
            startByte = currentByte;
            length = 0;
            while (*currentByte!='>')
            {
                currentByte++;
                length++;
                if (currentByte>=endByte)
                {
                    throw std::runtime_error("Exceeded end of shader data");
                }
            }
            currentByte++;
            std::string paramString((char*)startByte,length);
            const char* delimiter = ",";
            char* token = strtok(paramString.data(),delimiter);
            std::vector<std::string> parameters;
            while (token!=nullptr)
            {
                std::string param(token);
                parameters.push_back(param);
                token = strtok(nullptr,delimiter);
            }
            uint32_t shaderSize = *((uint32_t*)currentByte);
            if constexpr (std::endian::native==std::endian::big)
            {
                boost::endian::little_to_native_inplace(shaderSize);
            }
            currentByte+=sizeof(uint32_t);
            slag::ShaderModule module(stage,currentByte,shaderSize);
            currentByte+=shaderSize-1;
            shaderModules.push_back(std::move(module));
            if (stage == slag::ShaderStageFlags::VERTEX)
            {
                slag::VertexDescription description(parameters.size());
                std::vector<Mesh::VertexAttribute> attributes(parameters.size());
                for (auto i=0; i< parameters.size(); i++)
                {
                    auto attribute = getVertexAttribute(parameters[i]);
                    attributes[i]= attribute;
                    description.add(Mesh::underlyingAttribute(attribute),i);
                }
                vertexDesc.attributes = std::move(attributes);
                vertexDesc.description = std::move(description);
            }
            else if (stage == slag::ShaderStageFlags::FRAGMENT)
            {
                if (parameters.size()==0)
                {
                    throw std::runtime_error("Shader fragment stage has no targets");
                }
                for (auto i=0; i< parameters.size(); i++)
                {
                    auto format = getFormat(parameters[i]);
                    if (slag::Pixels::isColorFormat(format))
                    {
                        frameBufferDesc.addColorTarget(format);
                    }
                    else if (slag::Pixels::isDepthFormat(format))
                    {
                        frameBufferDesc.setDepthTarget(format);
                    }
                    else
                    {
                        throw std::runtime_error("Shader fragment format not supported");
                    }
                }
            }
        }

        slag::Pixels::Format ShaderManager::getFormat(const std::string& fromText)
        {
#define DEFINITION(SlagName, DxName, VulkanName, VkImageAspectFlags, VkComponentSwizzle_r, VkComponentSwizzle_g, VkComponentSwizzle_b, VkComponentSwizzle_a, totalBits) if(fromText==#SlagName){return slag::Pixels::SlagName;}
            TEXTURE_FORMAT_DEFINTITIONS(DEFINITION)
#undef DEFINITION
            throw std::runtime_error("Unrecognized format: "+fromText);
        }

        slag::ShaderStages ShaderManager::getStage(const std::string& fromText)
        {
            if (fromText == "vertex")
            {
                return slag::ShaderStageFlags::VERTEX;
            }
            if (fromText == "hull")
            {
                throw std::runtime_error("Hull Shaders not currently supported");
            }
            if (fromText == "domain")
            {
                throw std::runtime_error("Domain Shaders not currently supported");
            }
            if (fromText == "geometry")
            {
                return slag::ShaderStageFlags::GEOMETRY;
            }
            if (fromText == "fragment")
            {
                return slag::ShaderStageFlags::FRAGMENT;
            }
            if (fromText == "compute")
            {
                return slag::ShaderStageFlags::COMPUTE;
            }
            if (fromText == "rayGeneration")
            {
                return slag::ShaderStageFlags::RAY_GENERATION;
            }
            if (fromText == "intersection")
            {
                return slag::ShaderStageFlags::INTERSECTION;
            }
            if (fromText == "anyHit")
            {
                return slag::ShaderStageFlags::ANY_HIT;
            }
            if (fromText == "closestHit")
            {
                return slag::ShaderStageFlags::CLOSEST_HIT;
            }
            if (fromText == "miss")
            {
                return slag::ShaderStageFlags::MISS;
            }
            if (fromText == "callable")
            {
                return slag::ShaderStageFlags::CALLABLE;
            }
            if (fromText == "mesh")
            {
                return slag::ShaderStageFlags::MESH;
            }
            if (fromText == "task")
            {
                return slag::ShaderStageFlags::TASK;
            }
            throw std::runtime_error("Unrecognized shader type: "+fromText);

        }

        Mesh::VertexAttribute ShaderManager::getVertexAttribute(const std::string& fromText)
        {
            if (fromText == "Vertex3D")
            {
                return Mesh::POSITION_3D;
            }
            else if (fromText == "Vertex2D")
            {
                return Mesh::POSITION_2D;
            }
            else if (fromText == "Normal")
            {
                return Mesh::NORMAL;
            }
            else if (fromText == "Tangent")
            {
                return Mesh::TANGENT;
            }
            else if (fromText == "UVCoordinates")
            {
                return Mesh::UV;
            }
            else if (fromText == "VertexColor")
            {
                return Mesh::COLOR;
            }
            else if (fromText == "BoneWeights")
            {
                return Mesh::BONE_WEIGHT;
            }
            throw std::runtime_error("Unrecognized vertex attribute: "+fromText);
        }
    } // core
} // crucible