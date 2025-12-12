using System.Runtime.InteropServices;
using Crucible.Core.Serialization;

namespace Crucible.Core;

public unsafe partial class Texture2D: Texture
{
    public struct Offset
    {
        public Int32 X;
        public Int32 Y;

        public Offset(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    public struct Extent
    {
        public UInt32 Width;
        public UInt32 Height;

        public Extent(UInt32 width, UInt32 height)
        {
            Width = width;
            Height = height;
        }
    }

    public struct Region
    {
        public Offset Offset;
        public Extent Extent;
        public UInt32 Mip;

        public Region(Offset offset, Extent extent, UInt32 mip)
        {
            Offset = offset;
            Extent = extent;
            Mip = mip;
        }
    }
    
    public struct UpdateRegion
    {
        public byte[] PixelData;
        public Region Region;

        public UpdateRegion(byte[] pixelData, Region region)
        {
            PixelData = pixelData;
            Region = region;
        }
    }
    
    public uint Width
    {
        get { return CRUCIBLE_NATIVE_TextureGetWidth(_handle); }
    }

    public uint MipWidth(UInt32 mip)
    {
        if (mip < MipCount)
        {
            return CRUCIBLE_NATIVE_TextureGetMipWidth(_handle, mip);
        }
        throw new IndexOutOfRangeException("Selected mip is out of range");
    }
    public uint Height
    {
        get { return CRUCIBLE_NATIVE_TextureGetHeight(_handle); }
    }

    public uint MipHeight(UInt32 mip)
    {
        if (mip < MipCount)
        {
            return CRUCIBLE_NATIVE_TextureGetMipHeight(_handle, mip);
        }
        throw new IndexOutOfRangeException("Selected mip is out of range");
    }
    public Texture2D(PixelFormat format,uint width, uint height, uint mips, MultiSampleCount multiSampleCount = MultiSampleCount.One)
    {
        _handle = CRUCIBLE_NATIVE_TextureCreate2D(format, width, height, mips, multiSampleCount);
    }

    private Texture2D(IntPtr handle)
    {
        _handle = handle;
    }
    
    
    [LibraryImport("Crucible")]
    private static partial IntPtr CRUCIBLE_NATIVE_TextureCreate2D(PixelFormat format, UInt32 width, UInt32 height, UInt32 mips, MultiSampleCount multiSampleCount);

    public override byte[] Serialize()
    {
        var aspects = Texture.AspectsOf(Format);
        if (aspects.HasFlag(Texture.PixelAspects.Stencil))
        {
            throw new InvalidOperationException("Textures with a stencil aspect cannot be serialized");
        }
        
        List<Region> regions = new List<Region>((int)MipCount);
        List<ulong> mipSizes = new List<ulong>((int)MipCount);
        for (uint i = 0; i < MipCount; i++)
        {
            regions.Add(new Region(new Offset(0,0), new Extent(MipWidth(i),MipHeight(i)),i));
            mipSizes.Add(CRUCIBLE_NATIVE_TextureGetMipByteSize(_handle,i));
        }

        var pixels = GetPixels(regions, aspects);
        List<LZ4CompressionData> compressedData = new List<LZ4CompressionData>((int)MipCount);
        int offset = 0;
        int arraySize =
            6 + //magic number
            sizeof(PixelFormat)+ //pixel type
            sizeof(UInt32)+ //Width
            sizeof(UInt32)+ //Height
            sizeof(Int32) //MipCount
            ;
        for (int i = 0; i < mipSizes.Count; i++)
        {
            var compressed = new LZ4CompressionData(pixels.AsSpan(offset, (int)mipSizes[i]));
            compressedData.Add(compressed);
            offset += (int)mipSizes[i];
            arraySize += (sizeof(int) * 2);
            arraySize += compressed.CompressedDataSize;
        }
        byte[] data = new byte[arraySize];
        data[0] = (byte)'c';
        data[1] = (byte)'t';
        data[2] = (byte)'x';
        data[3] = (byte)'t';
        data[4] = (byte)'r';
        data[5] = (byte)'\n';
        int index = 6;
        index += data.Insert(Format,index);
        index += data.Insert(Width, index);
        index += data.Insert(Height, index);
        index += data.Insert(MipCount, index);
        for (int i = 0; i < compressedData.Count; i++)
        {
            //start with the smallest mip, so that loading can load the lower quality mips first
            var compressed = compressedData[compressedData.Count-1-i];
            index += data.Insert(compressed.CompressedDataSize, index);
            index += data.Insert(compressed.UncompressedDataSize, index);
            System.Buffer.BlockCopy(compressed.CompressedData,0,data,index,compressed.CompressedDataSize);
            index += compressed.CompressedDataSize;
        }

        return data;
    }
    /// <summary>
    /// Queue a job to set pixels in this texture
    /// </summary>
    /// <param name="pixelUpdates">Data about the pixels to update</param>
    /// <param name="aspect">Pixel aspect to update (only one can be done at a time)</param>
    /// <param name="batchInitQueue">Queue to assign the update to</param>
    /// <exception cref="ArgumentException"></exception>
    public void SetPixels(IEnumerable<UpdateRegion> pixelUpdates, Texture.PixelAspects aspect,GPUBatchInitQueue batchInitQueue,Action<Region>? callback = null)
    {
        if (System.Numerics.BitOperations.PopCount(((byte)aspect)) != 1)
        {
            throw new ArgumentException("One and only one aspect may be set per pixel update operation",nameof(aspect));
        }
        var pixelSize = Texture.PixelSize(Format, aspect);
        if (pixelSize == 0)
        {
            throw new ArgumentException("Invalid aspect for this texture", nameof(aspect));
        }
        int bufferSize = 0;
        
        foreach (var update in pixelUpdates)
        {
            bufferSize += update.PixelData.Length;
        }
        
        byte[] buffer = new byte[bufferSize];
        int bufferOffset = 0;
        TextureBufferMapping[] mappings = new TextureBufferMapping[pixelUpdates.Count()];
        for (int i = 0; i < pixelUpdates.Count(); i++)
        {
            var update = pixelUpdates.ElementAt(i);
            if (update.Region.Mip >= MipCount)
            {
                throw new ArgumentException($"Update Region '{i}' is trying to access mip beyond texture's mip count ({MipCount})",nameof(pixelUpdates));
            }
            var expectedLength = pixelSize * update.Region.Extent.Width *  update.Region.Extent.Height;
            if (update.PixelData.Length != expectedLength)
            {
                throw new ArgumentException($"Update Region '{i}' is not the appropriate size for the section it is trying to update ({expectedLength})",nameof(pixelUpdates));
            }
            System.Buffer.BlockCopy(update.PixelData, 0, buffer, bufferOffset, update.PixelData.Length);

            Extent3D extent = new Extent3D();
            extent.Width = update.Region.Extent.Width;
            extent.Height = update.Region.Extent.Height;
            extent.Depth = 1;
            
            Offset3D offset = new Offset3D();
            offset.X = update.Region.Offset.X;
            offset.Y = update.Region.Offset.Y;
            offset.Z = 0;
            
            
            TextureBufferMapping mapping = new TextureBufferMapping();
            mapping.BufferOffset =  (ulong)bufferOffset;
            mapping.Subresource.AspectFlags = aspect;
            mapping.Subresource.MipLevel = update.Region.Mip;
            mapping.Subresource.BaseArrayLayer = 0;
            mapping.Subresource.LayerCount = 1;
            mapping.TextureExtent = extent;
            mapping.TextureOffset = offset;
            mappings[i] = mapping;
            bufferOffset += update.PixelData.Length;
            
        }

        fixed (byte* bufferPtr = buffer)
        {
            fixed (TextureBufferMapping* mappingPtr = mappings)
            {
                if (callback != null)
                {
                    GCHandle handle = GCHandle.Alloc(callback);
                    CRUCIBLE_NATIVE_TextureSetPixelsDeferred(_handle,bufferPtr,(ulong)buffer.Length,mappingPtr,(uint)mappings.Length,batchInitQueue.DeferredJobQueueHandle,GCHandle.ToIntPtr(handle));
                }
                else
                {
                    CRUCIBLE_NATIVE_TextureSetPixelsDeferred(_handle,bufferPtr,(ulong)buffer.Length,mappingPtr,(uint)mappings.Length,batchInitQueue.DeferredJobQueueHandle,IntPtr.Zero);

                }
            }
        }
    }
    /// <summary>
    /// Queue a job to retrieve pixels from this texture
    /// </summary>
    /// <param name="regions">Areas of the texture to get pixels from</param>
    /// <param name="aspect">Pixel aspect to retrieve (only one can be done at a time)</param>
    /// <param name="batchInitQueue">Queue to assign the retrieval to</param>
    /// <param name="deferredInit">Action to take when the pixels are retrieved</param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public byte[] GetPixels(IEnumerable<Region> regions, Texture.PixelAspects aspect)
    {
        if (System.Numerics.BitOperations.PopCount(((byte)aspect)) != 1)
        {
            throw new ArgumentException("One and only one aspect may be set per pixel retrieval operation",nameof(aspect));
        }
        var pixelSize = Texture.PixelSize(Format, aspect);
        if (pixelSize == 0)
        {
            throw new ArgumentException("Invalid aspect for this texture", nameof(aspect));
        }
        int bufferSize = 0;
        TextureBufferMapping[] mappings = new TextureBufferMapping[regions.Count()];
        for (int i = 0; i < regions.Count(); i++)
        {
            var region = regions.ElementAt(i);
            if (region.Mip >= MipCount)
            {
                throw new ArgumentException($"Retrieve Region '{i}' is trying to access mip beyond texture's mip count ({MipCount})",nameof(regions));
            }
            var mipWidth = MipWidth(region.Mip);
            var mipHeight = MipHeight(region.Mip);
            if (region.Offset.X < 0 || region.Offset.Y < 0 ||
                region.Extent.Width + region.Offset.X > mipWidth ||
                region.Extent.Height + region.Offset.Y > mipHeight)
            {
                throw new ArgumentException($"Retrieve Region '{i}' is trying to access pixels beyond texture's extent",nameof(regions));
            }
            
            
            var regionSize = region.Extent.Width *  region.Extent.Height * pixelSize;
            
            Extent3D extent = new Extent3D();
            extent.Width = region.Extent.Width;
            extent.Height = region.Extent.Height;
            extent.Depth = 1;
            
            Offset3D offset = new Offset3D();
            offset.X = region.Offset.X;
            offset.Y = region.Offset.Y;
            offset.Z = 0;
            
            TextureBufferMapping mapping = new TextureBufferMapping();
            mapping.BufferOffset = (ulong)bufferSize;
            mapping.Subresource.AspectFlags = aspect;
            mapping.Subresource.MipLevel = region.Mip;
            mapping.Subresource.BaseArrayLayer = 0;
            mapping.Subresource.LayerCount = 1;
            mapping.TextureExtent = extent;
            mapping.TextureOffset = offset;
            mappings[i] = mapping;
            bufferSize += (int)regionSize;
        }
        byte[] pixels = new byte[bufferSize];
        fixed (byte* bufferPtr = pixels)
        {
            fixed (TextureBufferMapping* mappingPtr = mappings)
            {
                CRUCIBLE_NATIVE_TextureGetPixels(_handle, bufferPtr, (ulong)bufferSize, mappingPtr, (uint)mappings.Length);
            }
        }

        return pixels;
    }

    public static Texture2D LoadExchange(string filePath, uint mips = 1, bool autofill = true)
    {
        throw new NotImplementedException();
    }

    public static Texture2D ReadFromStream(BinaryReader reader)
    {
        var magicNumber = reader.ReadChars(6);
        if (magicNumber[0] != 'c' && magicNumber[1] != 't' && magicNumber[2] != 'x' && magicNumber[3] != 't' && magicNumber[4] != 'r' && magicNumber[5] != '\n')
        {
            throw new FormatException("Invalid mesh format");
        }

        var pixelFormat = (PixelFormat)reader.ReadInt32();
        var aspects = Texture.AspectsOf(pixelFormat);
        if (aspects != PixelAspects.Color && aspects != PixelAspects.Depth)
        {
            throw new InvalidDataException("Image must be either a color or depth texture");
        }
        var width = reader.ReadUInt32();
        var height = reader.ReadUInt32();
        var mips = reader.ReadUInt32();
        Texture2D texture2D = new Texture2D(pixelFormat, width, height, mips);
        List<UpdateRegion> regions = new List<UpdateRegion>((int)mips);
        for (int i = 0; i < mips; i++)
        {
            var mipIndex = mips - 1 - i;
            var compressedSize = reader.ReadInt32();
            var uncompressedSize = reader.ReadInt32();
            var compressedData = reader.ReadBytes(compressedSize);
            var decompressedData = LZ4.Decompress(compressedData, uncompressedSize);
            regions.Add(new UpdateRegion(decompressedData,new Region(new Offset(0,0),new Extent(texture2D.MipWidth((uint)mipIndex),texture2D.MipHeight((uint)mipIndex)),(uint)mipIndex)));
        }
        GPUBatchInitQueue initQueue = new GPUBatchInitQueue();
        texture2D.SetPixels(regions,aspects,initQueue,null);
        initQueue.Process();
        return texture2D;
    }
    
}