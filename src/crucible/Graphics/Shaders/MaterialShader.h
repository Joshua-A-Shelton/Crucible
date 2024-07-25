#ifndef CRUCIBLEEDITOR_MATERIALSHADER_H
#define CRUCIBLEEDITOR_MATERIALSHADER_H
#include "EngineShader.h"

namespace crucible
{

    class MaterialShader: public EngineShader
    {
    public:
        MaterialShader(const std::string& vertex, const std::string& fragment);
        MaterialShader(const MaterialShader&)=delete;
        MaterialShader& operator=(const MaterialShader&)=delete;
        MaterialShader(MaterialShader&& from);
        MaterialShader& operator=(MaterialShader&& from);
    private:
        void move(MaterialShader&& from);
        static inline slag::FramebufferDescription _framebufferDescription =
                slag::FramebufferDescription()
                .addColorTarget(slag::Pixels::PixelFormat::B8G8R8A8_UNORM)//color
                .setDepthTarget(slag::Pixels::PixelFormat::D32_SFLOAT);//depth
        static inline slag::VertexDescription _vertexDescription =
                slag::VertexDescriptionBuilder()
                .add(slag::Pixels::PixelFormat::R32G32B32_SFLOAT)//vec3 position
                .add(slag::Pixels::PixelFormat::R32G32B32_SFLOAT)//vec 3 normal
                .add(slag::Pixels::PixelFormat::R32G32_SFLOAT).build();//vec2 uv
    };

} // crucible

#endif //CRUCIBLEEDITOR_MATERIALSHADER_H
