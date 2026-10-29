using System.Runtime.InteropServices;
using Crucible.Assets;

namespace Crucible.Render;

/// <summary>
/// GPU assets (ADR 0049): imported models and textures loaded into GPU memory
/// for the renderer. <see cref="AcquireMesh"/> and <see cref="AcquireTexture"/>
/// load an asset on its first use and count uses; the matching release
/// unloads it after the last, once the GPU has finished every frame that
/// could still use it. The bytes come from the reader set with
/// <see cref="SetReader"/>: the asset database's imported files in the
/// editor. Main thread only, like the renderer.
/// </summary>
public static unsafe class RenderAssets
{
    private static Func<AssetId, byte[]?>? _reader;
    private static int _registered;

    /// <summary>
    /// Where acquired assets' bytes come from: the asset's imported file
    /// (.crmesh, .crtexture), or null when there is no such asset. Engine and
    /// editor code sets it; a reader from a game assembly would keep the game
    /// loaded across a hot reload (ADR 0011).
    /// </summary>
    public static void SetReader(Func<AssetId, byte[]?>? reader)
    {
        _reader = reader;
        if (Interlocked.Exchange(ref _registered, 1) == 0)
        {
            Api.SetAssetReader((ulong)(delegate* unmanaged<AssetId*, nint, delegate* unmanaged<nint, byte*, ulong, void>, int>)&Read);
        }
    }

    public static MeshHandle AcquireMesh(AssetId model) => Api.AcquireMesh(model);

    public static void ReleaseMesh(MeshHandle mesh) => Api.ReleaseMesh(mesh);

    public static TextureHandle AcquireTexture(AssetId texture) => Api.AcquireTexture(texture);

    public static void ReleaseTexture(TextureHandle texture) => Api.ReleaseTexture(texture);

    /// <summary>The texture's slot in the resource heap, for shaders; 0 for a stale handle.</summary>
    public static uint TextureDescriptor(TextureHandle texture) => Api.TextureDescriptor(texture);

    /// <summary>The sampler's slot in the sampler heap.</summary>
    public static uint SamplerDescriptor(Sampler sampler) => Api.SamplerDescriptor(sampler);

    // Called by native code while it acquires an asset: the bytes are handed
    // over during the call, pinned. An exception can't cross into native
    // code, so a failing reader counts as no such asset.
    [UnmanagedCallersOnly]
    private static int Read(AssetId* id, nint context, delegate* unmanaged<nint, byte*, ulong, void> deliver)
    {
        byte[]? bytes;
        try
        {
            bytes = _reader?.Invoke(*id);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"crucible: the asset reader failed for {*id}: {exception.Message}");
            return -1;
        }
        if (bytes is null)
        {
            return -1;
        }
        fixed (byte* data = bytes)
        {
            deliver(context, data, (ulong)bytes.Length);
        }
        return 0;
    }
}
