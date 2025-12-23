using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Crucible.Core;

public unsafe partial class Material
{
    private enum ShaderStage: UInt16
    {
        Vertex = 0b0000000000000001,
        Geometry = 0b0000000000000010,
        Fragment = 0b0000000000000100,
        Compute = 0b0000000000001000,
        RayGeneration = 0b0000000000010000,
        AnyHit = 0b0000000000100000,
        ClosestHit = 0b0000000001000000,
        Miss = 0b0000000010000000,
        Intersection = 0b0000000100000000,
        Callable = 0b0000001000000000,
        Mesh = 0b0000010000000000,
        Task = 0b0000100000000000
    }

    private enum ShaderCreateResult
    {
        Success = 0,
        FailedCompilation = 1
    }
    
    private IntPtr _shaderReference;
    
    private delegate void loadShaderFromFileDelegate(byte* filePath, byte* shaderName, delegate* unmanaged<byte*,Mesh.VertexAttributes, byte**, UInt32*, ShaderStage*,UInt32,ref ShaderProperties, ref FramebufferDescription,ShaderCreateResult> nativeBuildShader);
    private static loadShaderFromFileDelegate loadShaderFromFilePtr = loadShaderFromFile;
    [LibraryImport("Crucible", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr CRUCIBLE_NATIVE_ShaderPipelineGraphicsGetOrLoad(string name, loadShaderFromFileDelegate loadShaderFromFile);
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_ShaderReferenceDelete(IntPtr handle);
    
    private static void loadShaderFromFile(byte* shaderFilePath, byte* shaderName,delegate* unmanaged<byte*,Mesh.VertexAttributes, byte**, UInt32*,ShaderStage* ,UInt32,ref ShaderProperties, ref FramebufferDescription,ShaderCreateResult> nativeBuildShader)
    {
        string? path = System.Runtime.InteropServices.Marshal.PtrToStringUTF8((IntPtr)shaderFilePath);
        if (path == null)
        {
            throw new FileNotFoundException("No path for shader file was found");
        }
        BinaryReader reader = new BinaryReader(new MemoryStream(File.ReadAllBytes(path)));
        var magicNumber = reader.ReadChars(6);
        if (magicNumber[0] != 'c' && magicNumber[1] != 's' && magicNumber[2] != 'h' && magicNumber[3] != 'd' && magicNumber[4] != 'r' && magicNumber[5] != '\n')
        {
            throw new FormatException("Invalid shader format");
        }
        
        Mesh.VertexAttributes attributes = (Mesh.VertexAttributes)reader.ReadUInt16();
        var stageCount = reader.ReadByte();
        if (stageCount == 0)
        {
            throw new FormatException("Shader must have at least one stage");
        }
        List<byte[]> spirvData = new List<byte[]>(stageCount);
        UInt32[] spivLengths = new UInt32[stageCount];
        ShaderStage[] stages = new ShaderStage[stageCount];
        for (int i = 0; i < stageCount; i++)
        {
            var shaderStage = (ShaderStage)reader.ReadUInt16();
            var shaderLength = reader.ReadUInt32();
            var bytes = reader.ReadBytes((int)shaderLength);
            spirvData.Add(bytes);
            spivLengths[i] = shaderLength;
            stages[i] = shaderStage;
        }
        List<GCHandle> pinnedArrays = new List<GCHandle>(stageCount);
        try
        {
            byte*[] spirvs = new byte*[stageCount];
            for (int i = 0; i < stageCount; i++)
            {
                GCHandle handle = GCHandle.Alloc(spirvData[i], GCHandleType.Pinned);
                pinnedArrays.Add(handle);
                spirvs[i] = (byte*)handle.AddrOfPinnedObject();
                
            }

            ShaderProperties properties = new ShaderProperties();
            
            FramebufferDescription frameDescription = new FramebufferDescription();
            frameDescription.ColorAttachments[0] = Texture.PixelFormat.R8G8B8A8_UNorm;
            frameDescription.ColorAttachments[1] = Texture.PixelFormat.None;
            frameDescription.ColorAttachments[2] = Texture.PixelFormat.None;
            frameDescription.ColorAttachments[3] = Texture.PixelFormat.None;
            frameDescription.ColorAttachments[4] = Texture.PixelFormat.None;
            frameDescription.ColorAttachments[5] = Texture.PixelFormat.None;
            frameDescription.ColorAttachments[6] = Texture.PixelFormat.None;
            frameDescription.ColorAttachments[7] = Texture.PixelFormat.None;
            frameDescription.DepthStencilFormat = Texture.PixelFormat.D32_Float;
            
            fixed (byte** spirvPtr = spirvs)
            {
                fixed (UInt32* lenghtsPtr = spivLengths)
                {
                    fixed (ShaderStage* stagesPtr = stages)
                    {
                        if (nativeBuildShader(shaderName, attributes,spirvPtr,lenghtsPtr,stagesPtr,(uint)stageCount,ref properties,ref frameDescription) != ShaderCreateResult.Success)
                        {
                            throw new InvalidDataException($"Shader located at {path} is not valid");
                        }
                    }
                }
            }
            
        }
        catch (Exception e)
        {
            throw;
        }
        finally
        {
            foreach (var pinnedArray in pinnedArrays)
            {
                pinnedArray.Free();
            }
        }
        
    }


    public Material(string name)
    {
        _shaderReference = CRUCIBLE_NATIVE_ShaderPipelineGraphicsGetOrLoad(name, loadShaderFromFilePtr);
        if (_shaderReference == IntPtr.Zero)
        {
            throw new InvalidOperationException("Material unable to load backing shader");
        }
    }

    ~Material()
    {
        CRUCIBLE_NATIVE_ShaderReferenceDelete(_shaderReference);
    }
}
[StructLayout(LayoutKind.Sequential)]
internal struct RasterizationState
{
    ///How to fill the geometry with pixels
    public enum DrawMode: byte
    {
        ///Draw the entire triangle
        FACE,
        ///Draw just the edges of the triangle
        EDGE,
        ///Draw just the vertices of the triangle
        VERTEX
    };
    ///Which triangles to cull (not draw)
    public enum CullOptions: byte
    {
        NONE,
        FRONT_FACING,
        BACK_FACING
    };
    ///Which triangles are considered front facing for culling
    public enum FrontFacing: byte
    {
        ///Triangles that specify vertices in a clockwise manner
        CLOCKWISE,
        ///Triangles that specify vertices in a counter clockwise manner
        COUNTER_CLOCKWISE
    };
    ///Whether or not a fragment's depth is clamped to values in the depth test
    byte depthClampEnable = 0;
    ///Whether or not geometry is discarded before the rasterization stage
    byte rasterizerDicardEnable = 0;
    ///How to fill drawn geometry
    DrawMode drawMode = DrawMode.FACE;
    ///How thick to draw rasterized lines (DrawModes EDGE only)
    float lineThickness = 1.0f;
    ///Which faces of a mesh to not draw
    CullOptions culling = CullOptions.BACK_FACING;
    ///What winding order determines a face as front facing
    FrontFacing frontFacing = FrontFacing.CLOCKWISE;

    ///Enable depth biasing for drawn fragments
    byte depthBiasEnable = 0;
    ///Extra nudge to bias fragments in the depth buffer if depthBiasEnable is true
    int depthBiasConstantFactor = 0;
    ///Maximum (or minimum) depth bias of a fragment
    float depthBiasClamp = 0.0f;
    ///Scalar applied to a fragments slope in depth bias calculations
    float depthBiasSlopeFactor = 0.0f;

    public RasterizationState()
    {
    }
};

///Details about multisampling
internal struct MultiSampleState
{
    ///Number or rasterization samples (1/2/4/8/16)
    byte rasterizationSamples = 1;
    ///Enable sample shading (require multiple samples to generate a fragment)
    byte sampleShadingEnable = 0;
    ///Minimum number of samples (1/2/4/8/16) needed to generate a fragment is sampleShadingEnable is true
    byte minSampleShading = 1;
    ///Controls if an alpha component of a fragment's first color is replaced in multisampling
    byte alphaToOneEnable = 0;

    public MultiSampleState()
    {
    }
};

 ///Details about blending partially transparent pixels per Attachment
internal struct BlendAttachmentState
{
    ///Enable blending
    byte blendingEnabled = 1;
    ///Blend factor for RGB components of source texel
    Operations.BlendFactor srcColorBlendFactor = Operations.BlendFactor.BLEND_FACTOR_SRC_ALPHA;
    ///Blend factor from RGB components of incoming texel
    Operations.BlendFactor dstColorBlendFactor = Operations.BlendFactor.BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
    ///Operation for combining RGB components of mixing texels
    Operations.BlendOperation colorBlendOperation = Operations.BlendOperation.BLEND_OP_ADD;
    ///Blend factor for Alpha component of source texel
    Operations.BlendFactor srcAlphaBlendFactor = Operations.BlendFactor.BLEND_FACTOR_ONE;
    ///Blend factor for Alpha component of incoming texel
    Operations.BlendFactor dstAlphaBlendFactor = Operations.BlendFactor.BLEND_FACTOR_ZERO;
    ///Operation for combining Alpha components of mixing texels
    Operations.BlendOperation alphaBlendOperation = Operations.BlendOperation.BLEND_OP_ADD;
    ///Mask to only write certain components of the texels
    Color.ComponentFlags colorWriteMask = Color.ComponentFlags.RED_COMPONENT | Color.ComponentFlags.GREEN_COMPONENT | Color.ComponentFlags.BLUE_COMPONENT | Color.ComponentFlags.ALPHA_COMPONENT;

    public BlendAttachmentState()
    {
    }
};

[InlineArray(8)]
public struct BlendAttachmentStateArray
{
    private BoneWeight _element0;
}
///Details about blending partially transparent pixels
internal struct BlendState
{
    ///Whether to apply logical operation to determine if blending should occur
    byte logicOperationEnable = 0;
    ///What operation to perform to determine if blending should occur
    Operations.LogicalOperation logicalOperation = Operations.LogicalOperation.LOGIC_OP_COPY;
    ///Blend states for color attachments, up to 8, that correspond to FrameBufferDescription color attachments
    BlendAttachmentStateArray attachmentBlendStates = default;

    public BlendState()
    {
    }
};

///Parameter on stencil usage
struct StencilOpState
{
    ///Action performed on samples that fail the stencil test
    Operations.StencilOperation failOp = Operations.StencilOperation.STENCIL_OP_KEEP;
    ///Action performed on samples that pass the stencil test
    Operations.StencilOperation passOp = Operations.StencilOperation.STENCIL_OP_KEEP;
    ///Action performed on samples that pass the stencil test, but fail the depth test
    Operations.StencilOperation depthFailOp = Operations.StencilOperation.STENCIL_OP_KEEP;
    ///Comparison to use in the stencil test
    Operations.ComparisonFunction compareOp = Operations.ComparisonFunction.COMPARISION_NEVER;

    public StencilOpState()
    {
    }
};
///Details on the depth stencil usage
internal struct DepthStencilState
{
    ///Whether to perform a depth test or not
    byte depthTestEnable = 1;
    ///Whether to update the depth buffer if a fragment passes the depth test with that fragment's depth
    byte depthWriteEnable = 1;
    ///Operation to use to determine if a fragment passes the depth test
    Operations.ComparisonFunction depthCompareOperation = Operations.ComparisonFunction.COMPARISION_LESS;
    ///Whether or not to enable stencil buffer testing
    byte stencilTestEnable = 0;
    ///Which bits of the stencil part of the buffer are part of the stencil test
    byte stencilReadMask = 0xFF;
    ///Which bits of the stencil buffer are update by the stencil test
    byte stencilWriteMask = 0xFF;
    ///Parameter for stencil test on front facing polygons
    StencilOpState front = default;
    ///Parameter for stencil test on back facing polygons
    StencilOpState back = default;

    public DepthStencilState()
    {
    }
};
///Collection of properties that describe how a shader should behave
internal struct ShaderProperties
{
    ///Details about how to rasterize fragments
    RasterizationState rasterizationState=default;
    ///Details about how to handle multi sampling
    MultiSampleState multiSampleState=default;
    ///Details about blending transparent pixels
    BlendState blendState=default;
    ///Details about depth/stencil tests
    DepthStencilState depthStencilState=default;

    public ShaderProperties()
    {
    }
};

[InlineArray(8)]
public struct ColorAttachmentFormatArray
{
    private Texture.PixelFormat _element0;
}

internal struct FramebufferDescription
{
    public ColorAttachmentFormatArray ColorAttachments;
    public Texture.PixelFormat DepthStencilFormat;
}
