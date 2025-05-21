#ifndef CRUICIBLE_SHADERMANAGER_H
#define CRUICIBLE_SHADERMANAGER_H
#include <filesystem>
#include <string>
#include <unordered_map>
#include <slag/SlagLib.h>

#include "Mesh.h"

namespace crucible
{
    namespace core
    {
        class ShaderManager;
        class ShaderUnit
        {
        public:
            ShaderUnit(slag::ShaderPipeline* pipeline, const std::string& name, const std::vector<Mesh::VertexAttribute>& attributes);
            ~ShaderUnit();
            ShaderUnit(const ShaderUnit& other)=delete;
            ShaderUnit& operator=(const ShaderUnit& other)=delete;
            ShaderUnit(ShaderUnit&& other);
            ShaderUnit& operator=(ShaderUnit&& other);

            size_t _referenceCount = 0;
            slag::ShaderPipeline* _pipeline=nullptr;
            std::string _name;
            std::vector<Mesh::VertexAttribute> _requiredAttributes;
            void move(ShaderUnit& other);
        };

        class ShaderReference
        {
        private:
            ShaderUnit* _unit = nullptr;
            ShaderReference(ShaderUnit* unit);
            void copy(const ShaderReference& other);
            void move(ShaderReference& other);
        public:
            ShaderReference()=default;
            ShaderReference(const ShaderReference& ref);
            ShaderReference& operator=(const ShaderReference& ref);
            ShaderReference(ShaderReference&& ref);
            ShaderReference& operator=(ShaderReference&& ref);
            ~ShaderReference();

            bool operator==(const ShaderReference&) const;

            slag::ShaderPipeline* pipeline()const;
            size_t requiredAttributesCount()const;
            Mesh::VertexAttribute attribute(size_t attributeIndex)const;
            const std::vector<Mesh::VertexAttribute>& requiredAttributes() const;
            bool hasAttribute(Mesh::VertexAttribute attribute)const;
            friend class ShaderManager;
            struct hashFunction
            {
                size_t operator()(const ShaderReference& ref)const;
            };
        };


        class ShaderManager
        {
        public:
            static ShaderReference getShaderReference(const std::string& name);
            friend class ShaderReference;
        private:
            struct VertexInfo
            {
                slag::VertexDescription description;
                std::vector<Mesh::VertexAttribute> attributes;
            };
            inline static std::unordered_map<std::string, ShaderUnit> _compiledShaders;
            inline static std::mutex _compiledShadersMutex;
            static ShaderUnit buildShader(std::filesystem::path path, const std::string& name);
            static void destroyShader(ShaderUnit* shader);

            static void getDescriptorGroups(unsigned char*& currentByte, const unsigned char* endByte,std::vector<slag::DescriptorGroup*>& descriptorGroups);
            static void getShaderModules(unsigned char* startingByte, const unsigned char *endByte, std::vector<slag::ShaderModule>& shaderModules,slag::ShaderProperties& properties,VertexInfo& vertexDesc, slag::FrameBufferDescription& frameBufferDesc);
            static void nextModule(unsigned char*& currentByte, const unsigned char* endByte, std::vector<slag::ShaderModule>& shaderModules,slag::ShaderProperties& properties,VertexInfo& vertexDesc, slag::FrameBufferDescription& frameBufferDesc);
            static slag::Pixels::Format getFormat(const std::string& fromText);
            static slag::ShaderStages getStage(const std::string& fromText);
            static Mesh::VertexAttribute getVertexAttribute(const std::string& fromText);

        };
    } // core
} // crucible

#endif //CRUICIBLE_SHADERMANAGER_H
