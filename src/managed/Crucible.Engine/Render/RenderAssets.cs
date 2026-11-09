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

    /// <summary>
    /// An authored material (.crmat, ADR 0052), loaded with its shader and
    /// textures on first use and counted.
    /// </summary>
    public static MaterialHandle AcquireMaterial(AssetId material) => Api.AcquireMaterial(material);

    public static void ReleaseMaterial(MaterialHandle material) => Api.ReleaseMaterial(material);

    /// <summary>
    /// Replaces a material's parameters with <paramref name="parameters"/>,
    /// the struct the Shader importer generated for its type. Frames already
    /// submitted keep the old values. Throws if the material is of another
    /// type (or stale).
    /// </summary>
    public static void SetMaterial<T>(MaterialHandle material, in T parameters) where T : unmanaged, IMaterialData
    {
        AssetId shader = Api.MaterialShader(material);
        string type = Api.MaterialTypeName(material);
        if (shader != T.ShaderId || type != T.TypeName)
        {
            throw new ArgumentException(
                $"The material is a {(type.Length == 0 ? "stale handle" : type)}, not a {T.TypeName}", nameof(parameters));
        }
        fixed (T* data = &parameters)
        {
            Api.SetMaterialParameters(material, new ReadOnlySpan<byte>(data, sizeof(T)));
        }
    }

    /// <summary>
    /// Loads an asset again from the reader after it's reimported (ADR 0072):
    /// a loaded model, texture, material, or game shader is replaced in
    /// place, after waiting for the GPU, so handles to it stay valid. False
    /// if it isn't loaded (always, before the renderer starts). Throws if
    /// the new version doesn't load; the old one stays.
    /// </summary>
    public static bool Reload(AssetId asset) => Api.ReloadRenderAsset(asset);

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
