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

            size_t _referenceCount = 0;
            slag::ShaderPipeline* _pipeline;
            std::string _name;
            std::vector<Mesh::VertexAttribute> _requiredAttributes;
        };

        class ShaderReference
        {
        private:
            ShaderUnit* _unit = nullptr;
            ShaderReference(ShaderUnit* unit);
            void copy(const ShaderReference& other);
            void move(ShaderReference& other);
        public:
            ShaderReference()=delete;
            ShaderReference(const ShaderReference& ref);
            ShaderReference& operator=(const ShaderReference& ref);
            ShaderReference(ShaderReference&& ref);
            ShaderReference& operator=(ShaderReference&& ref);
            ~ShaderReference();

            slag::ShaderPipeline* pipeline();
            size_t requiredAttributesCount();
            Mesh::VertexAttribute attribute(size_t attributeIndex);
            friend class ShaderManager;
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

            static slag::FrameBufferDescription getFrameBufferDescription(std::ifstream& shaderFile);
            static VertexInfo getVertexProperties(std::ifstream& shaderFile);
            static slag::ShaderProperties getShaderProperties(std::ifstream& shaderFile);
            static std::vector<slag::ShaderModule> getShaderModules(std::ifstream& shaderFile);

            static slag::Pixels::Format getFormat(const std::string& fromText);

        };
    } // core
} // crucible

#endif //CRUICIBLE_SHADERMANAGER_H
