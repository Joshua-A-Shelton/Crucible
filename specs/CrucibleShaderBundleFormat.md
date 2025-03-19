# Crucible Shader Bundle Format (.csbf) #
Here in lies the specification for shader files for the crucible engine

## File Header ##
Every shader file starts with the ASCII string "csbf" followed by a newline "\n"

## Framebuffer Description ##
Every shader file must define its target framebuffer by defining one or more targets in an ASCII string, defined thusly:

    Pound symbol(#)

    Color/Depth signifier

    Single space " "

    Pixel format

    Newline "\n"

For example:
    
    #color R8G8B8A8_UNORM
    #depth D32_FLOAT

would define a framebuffer with a color target with a pixel format that has byte sized Red, Green, Blue, and Alpha channels, and a depth target with a 32 bit float depth channel. The order the color targets is defined must match the order provided in the end shader. Up to 8 color targets may be defined. The depth target may be decalred in any order, but only the final declaration will be used. At least one target must be defined (color or depth) but any more definitions is optional (you do not have to define a depth or color target if the shader doesn't use it)

### Color Pixel Formats ###
The following are the acceptable formats for a color target

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

### Depth Pixel Formats ###
The following are the acceptable formats for a depth target

    D32_FLOAT_S8X24_UINT
    D32_FLOAT
    D24_UNORM_S8_UINT
    D16_UNORM

## Vertex Properties ##
Vertex Properties describe what the vertex format for the shader will be. It is stored as a little endian 16 bit unsigned integer, with the bit fields of the integer indicating if a property is required or not

The properties bits are defined as follows

    0x0000000000000001 = 3D Vertex Position data
    0x0000000000000010 = 2D Vertex Position data
    0x0000000000000100 = Vertex Normals
    0x0000000000001000 = UV Coordinates
    0x0000000000010000 = Color
    0x0000000000100000 = Bone Weights
    0x0000000001000000 = RESERVED FOR FUTURE USE
    0x0000000010000000 = RESERVED FOR FUTURE USE
    0x0000000100000000 = RESERVED FOR FUTURE USE
    0x0000001000000000 = RESERVED FOR FUTURE USE
    0x0000010000000000 = RESERVED FOR FUTURE USE
    0x0000100000000000 = RESERVED FOR FUTURE USE
    0x0001000000000000 = RESERVED FOR FUTURE USE
    0x0010000000000000 = RESERVED FOR FUTURE USE
    0x0100000000000000 = RESERVED FOR FUTURE USE
    0x1000000000000000 = RESERVED FOR FUTURE USE

For example, if the shader requires a vertex to have 3d position data, Normals, UV coordinates, and bone weights the float will be
    
    0x0000000001001101

Do note that this is stored in the file as little endian, meaning the lowest bits are stored first, so while reading or writing this to a file the above example would be stored as

    (hexadecimal) D4
    (binary) 01001101 00000000

## Shader Properties ##

These are not currently supported, but will be.

## SPIR-V Modules ##

The remainder of the file is used to store SPIR-V shader data as follows
    
    Stage name (ASCII)
    Semicolon (:)
    SPIR-V data size (little endian unsigned 32 bit integer)
    SPIR-V data (raw bytecode for a shader stage)

For example (<> used here as an escape sequence to denote what is described is in english, not binary)
    
    vertex:<SPIR-V data size><SPIR-V data>

### Stage Names ###

The following are the supported stage names

    vertex
    fragment

stages may be defined in any order, but must only appear once