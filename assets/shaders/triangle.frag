#version 450

layout (location = 0) in vec3 inColor;

layout (location = 0) out vec4 outFragColor;

layout (set=0, binding =0) uniform fog
{
    vec4 fogColor;
};

void main()
{
    outFragColor = vec4(inColor,1.0f)*fogColor;
}