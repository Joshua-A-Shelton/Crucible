#ifndef CRUCIBLE_SHADERMANAGER_H
#define CRUCIBLE_SHADERMANAGER_H
#include <mutex>
#include <slag/Slag.h>

#include "Mesh.h"

namespace crucible
{
    class ShaderManager;
    class ShaderUnit
    {
    public:
        ShaderUnit(slag::ShaderPipeline* pipeline, const std::string& name, Mesh::VertexAttributeFlags attributes);
        ~ShaderUnit();
        ShaderUnit(const ShaderUnit&)=delete;
        ShaderUnit& operator=(const ShaderUnit&)=delete;
        ShaderUnit(ShaderUnit&& from);
        ShaderUnit& operator=(ShaderUnit&& from);

        uint64_t _referenceCount = 0;
        slag::ShaderPipeline* _pipeline = nullptr;
        std::string _name;
        std::vector<Mesh::VertexAttribute> _attributes;
    private:

        void move(ShaderUnit& from);

    };

    class ShaderReference
    {
    private:
        ShaderUnit* _unit = nullptr;
        ShaderReference(ShaderUnit* unit);
        void copy(const ShaderReference& from);
        void move(ShaderReference& from);
    public:
        ShaderReference()=default;
        ShaderReference(const ShaderReference& ref);
        ShaderReference& operator=(const ShaderReference& ref);
        ShaderReference(ShaderReference&& from);
        ShaderReference& operator=(ShaderReference&& from);
        ~ShaderReference();

        bool operator==(const ShaderReference& other)const;

        slag::ShaderPipeline* pipeline()const;
        Mesh::VertexAttributeFlags attributes()const;
        friend class ShaderManager;
        struct hashFunction
        {
            size_t operator()(const ShaderReference& ref)const;
        };
    };
    class ShaderManager
    {
    public:
        enum class ShaderCreateResult
        {
            SUCCESS = 0,
            FAILED_COMPILATION = 1
        };
        static ShaderReference getShader(const char* name, void(*createShader)(const char* shaderPath,const char* shaderName,ShaderCreateResult(*nativeAddShader)(const char* name,Mesh::VertexAttributeFlags attributes, unsigned char** shaderCodeArray, uint32_t* shaderCodeLengthsArray, slag::ShaderStageFlags* stages, uint32_t shaderCount,slag::ShaderProperties* properties, slag::FrameBufferDescription* framebufferDescription)));
        friend class ShaderReference;
    private:
        static ShaderCreateResult addShader(const char* name, Mesh::VertexAttributeFlags attributes, unsigned char** shaderCodeArray, uint32_t *shaderCodeLengthsArray, slag::ShaderStageFlags* stages, uint32_t shaderCount, slag::ShaderProperties* properties, slag::FrameBufferDescription* framebufferDescription);


    };
} // crucible

#endif //CRUCIBLE_SHADERMANAGER_H
