#include "EngineShader.h"

namespace crucible
{
    EngineShader::~EngineShader()
    {
        if(_shader)
        {
            delete _shader;
        }
    }

    EngineShader::EngineShader(const std::string& vertex, const std::string& fragment, slag::VertexDescription& vertexDescription, slag::FramebufferDescription& framebufferDescription)
    {
        _shader = slag::Shader::create(vertex.c_str(),fragment.c_str(),vertexDescription,framebufferDescription);
    }

    EngineShader::EngineShader(EngineShader&& from)
    {
        move(std::move(from));
    }

    EngineShader& EngineShader::operator=(EngineShader&& from)
    {
        move(std::move(from));
        return *this;
    }

    void EngineShader::move(EngineShader&& from)
    {
        _shader = from._shader;
        from._shader = nullptr;
    }

    slag::Shader* EngineShader::handle() const
    {
        return _shader;
    }
} // crucible