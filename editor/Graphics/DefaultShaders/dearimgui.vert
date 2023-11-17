#version 450 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 aUV;
layout(location = 2) in uint aColor;
layout(push_constant) uniform uPushConstant { vec2 uScale; vec2 uTranslate; } pc;

out gl_PerVertex { vec4 gl_Position; };
layout(location = 0) out struct { vec4 Color; vec2 UV; } Out;

void main()
{
    uint rmask = 0xFF000000;
    uint gmask = 0x00FF0000;
    uint bmask = 0x0000FF00;
    uint amask = 0x000000FF;
    vec4 ocolor;
    ocolor.a = ((aColor & rmask) >> 24)/255.0f;
    ocolor.b = ((aColor & gmask) >> 16)/255.0f;
    ocolor.g = ((aColor & bmask) >> 8)/255.0f;
    ocolor.r = (aColor & amask)/255.0f;
    Out.Color = ocolor;
    Out.UV = aUV;
    gl_Position = vec4(aPos * pc.uScale + pc.uTranslate, 0, 1);
}