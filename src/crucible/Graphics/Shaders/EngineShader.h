#ifndef CRUCIBLEEDITOR_ENGINESHADER_H
#define CRUCIBLEEDITOR_ENGINESHADER_H
#include <slag/Shader.h>
namespace crucible
{

    class EngineShader
    {
    public:
        virtual ~EngineShader();
        EngineShader(const EngineShader&)=delete;
        EngineShader& operator=(const EngineShader&)=delete;
        EngineShader(EngineShader&& from);
        EngineShader& operator=(EngineShader&& from);
        slag::Shader* handle()const;
    protected:
        EngineShader(const std::string& vertex, const std::string& fragment, slag::VertexDescription& vertexDescription, slag::FramebufferDescription& framebufferDescription);
        slag::Shader* _shader = nullptr;
        void move(EngineShader&& from);
    };
} // crucible

#endif //CRUCIBLEEDITOR_ENGINESHADER_H
