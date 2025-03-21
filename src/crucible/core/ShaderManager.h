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
            ShaderReference()=delete;
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

            static slag::FrameBufferDescription getFrameBufferDescription(unsigned char*& pointer, const unsigned char* lastInFile);
            static VertexInfo getVertexProperties(unsigned char*& pointer,const unsigned char* lastInFile);
            static slag::ShaderProperties getShaderProperties(unsigned char*& pointer, const unsigned char* lastInFile);
            static std::vector<slag::ShaderModule> getShaderModules(unsigned char*& pointer,const unsigned char* lastInFile);

            static slag::Pixels::Format getFormat(const std::string& fromText);

        };
    } // core
} // crucible

#endif //CRUICIBLE_SHADERMANAGER_H
