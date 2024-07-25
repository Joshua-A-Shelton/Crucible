#include "MaterialShader.h"

namespace crucible
{
    MaterialShader::MaterialShader(const std::string& vertex, const std::string& fragment): EngineShader(vertex,fragment,_vertexDescription,_framebufferDescription)
    {

    }

    MaterialShader::MaterialShader(MaterialShader&& from): EngineShader(std::move(from))
    {
        move(std::move(from));
    }

    MaterialShader& MaterialShader::operator=(MaterialShader&& from)
    {
        EngineShader::move(std::move(from));
        move(std::move(from));
        return *this;
    }

    void MaterialShader::move(MaterialShader&& from)
    {

    }


} // crucible