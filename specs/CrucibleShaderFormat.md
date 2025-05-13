# Crucible Shader Format #
Here in lies the specification for crucible shader files

## File Header ##
Every shader file starts with the ASCII string "cshdr" followed by a newline "\n"

## File Contents
The remainder of the file is broken up into two sections which will be described below

    #1) Descriptor Groups
    #2) Shader Stages

## Descriptor Groups ##
The Descriptor Group section is layed out thusly

    #1) Descriptor Group Count          (8 bit unsigned integer)
    #2) Descriptor Group Definitions    (Collection of #1(Descriptor Group Count) number of Descriptor Group Definitions)*1

The Descriptor Group count indicates how many descriptor groups are defined in the file. Can be zero if there are no groups to describe

*1 Descriptor Group Definitions are layed out thusly
    
    #1) Descriptor Count            (8 bit unsigned integer, indicating the number of descriptors in this group)
    #2) Descriptor Descriptions     (collection of #1(Descriptor Count) number of Descriptor Descriptions)*2

*2 Descriptor Descriptions are layed out thusly

    #1) Descriptor Name             (Null terminated string)
    #2) Descriptor Group Index      (32 bit, little endian unsigned integer, represents the index of this descriptor)
    #3) Descriptor Type             (8 bit number to corresponds to a given slag descriptor type)*3
    #4) Array Count                 (32 bit, little endian unsigned integer, represents the depth of the array of this descriptor, or 1 for a single element)

*3 the slag descriptor types are mapped thusly

    SAMPLER                     0 (0x00000000)
    SAMPLED_TEXTURE             1 (0x00000001)
    SAMPLER_AND_TEXTURE         2 (0x00000010)
    STORAGE_TEXTURE             3 (0x00000011)
    UNIFORM_TEXEL_BUFFER        4 (0x00000100)
    STORAGE_TEXEL_BUFFER        5 (0x00000101)
    UNIFORM_BUFFER              6 (0x00000110)
    STORAGE_BUFFER              7 (0x00000111)
    INPUT_ATTACHMENT            8 (0x00001000)
    ACCELERATION_STRUCTURE      9 (0x00001001)
    
## Shader Stages ##
The shader stages section contains one or more shader stages, each described thusly

    #1) $SHADER_STAGE_NAME          (one of several non-null terminated strings, as defined below)*4
    #2) :                           (semicolon, indicating the end of the string)
    #3) <                           (less than bracket, indicating the start of stage parameters)
    #4) Parameter List              (List of parameters relevant to the stage, can be empty)*5
    #5) >                           (greater than bracket, indicating end of stage parameters
    #6) Shader Length               (unsigned 32 bit little endian integer)
    #7) Shader Data                 (SPIR-V code, of length #6(Shader Length))

*4 the possible shader stage are as follows

    vertex
    hull
    domain
    geometry
    fragment
    compute
    rayGeneration
    intersection
    anyHit
    anyHit
    closestHit
    miss
    callable
    mesh
    task

*5 Parameter lists are comma seperated values (with no white space). Provided are a few examples with the parameter list in bold

---
*Fragment stage with a color target of R8G8B8A8_UNORM, and a depth target of D32_FLOAT*

fragment:<**R8G8B8A8_UNORM,D32_FLOAT**>

*Fragment stage with a color target of R8G8B8A8_UINT*

fragment:<**R8G8B8A8_UINT**>

*Vertex stage that requires Vertex Data, Normal Data, UV Coordinates, and Bone Weights from a mesh*

vertex:<**Vertex3D,Normal,UVCoordinates,BoneWeights**>

---

The following are valid parameters for each stage

### Vertex Shader ###

Vertex Shader parameters define what kind of data is required from the Mesh in order to draw. Defining any of the following will attempt to pull the data from the mesh at draw time

    Vertex3D
    Vertex2D
    Normal
    Tangent
    UVCoordinates
    VertexColor
    BoneWeights

### Fragment Shader ###

Fragment Shader parameters define what targets the fragment shader draws to. The order of color targets defined determines the order of the targets in the shader

    R32G32B32A32_FLOAT
    R32G32B32A32_UINT
    R32G32B32A32_SINT
    R32G32B32_FLOAT
    R32G32B32_UINT
    R32G32B32_SINT
    R16G16B16A16_FLOAT
    R16G16B16A16_UNORM
    R16G16B16A16_UINT
    R16G16B16A16_SNORM
    R16G16B16A16_SINT
    R32G32_FLOAT
    R32G32_UINT
    R32G32_SINT
    R10G10B10A2_UNORM
    R10G10B10A2_UINT
    R11G11B10_FLOAT
    R8G8B8A8_UNORM
    R8G8B8A8_UNORM_SRGB
    R8G8B8A8_UINT
    R8G8B8A8_SNORM
    R8G8B8A8_SINT
    R16G16_FLOAT
    R16G16_UNORM
    R16G16_UINT
    R16G16_SNORM
    R16G16_SINT
    R32_FLOAT
    R32_UINT
    R32_SINT
    R8G8_UNORM
    R8G8_UINT
    R8G8_SNORM
    R8G8_SINT
    R16_FLOAT
    R16_UNORM
    R16_UINT
    R16_SNORM
    R16_SINT
    R8_UNORM
    R8_UINT
    R8_SNORM
    R8_SINT
    A8_UNORM
    R9G9B9E5_SHAREDEXP
    R8G8_B8G8_UNORM
    G8R8_G8B8_UNORM
    BC1_UNORM
    BC1_UNORM_SRGB
    BC2_UNORM
    BC2_UNORM_SRGB
    BC3_UNORM
    BC3_UNORM_SRGB
    BC4_UNORM
    BC4_SNORM
    BC5_UNORM
    BC5_SNORM
    B5G6R5_UNORM
    B5G5R5A1_UNORM
    B8G8R8A8_UNORM
    B8G8R8X8_UNORM
    B8G8R8A8_UNORM_SRGB
    B8G8R8X8_UNORM_SRGB
    BC6H_UF16
    BC6H_SF16
    BC7_UNORM
    BC7_UNORM_SRGB
    AYUV
    NV12
    OPAQUE_420
    YUY2
    B4G4R4A4_UNORM

A single depth target may also be defined, at any point within the parameter list

    D32_FLOAT_S8X24_UINT
    D32_FLOAT
    D24_UNORM_S8_UINT
    D16_UNORM