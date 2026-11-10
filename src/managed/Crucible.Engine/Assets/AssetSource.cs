using System.Runtime.InteropServices;

namespace Crucible.Assets;

/// <summary>
/// The runtime asset contract from C# (ADR 0076): the one source of every
/// asset's bytes by ID, which the renderer, prefabs, and anything else at
/// runtime read through. The editor installs its asset database, a game run
/// from a project its imported files (<see cref="AssetCache"/>); a native
/// module may install its own (pack files, streaming) instead. Main thread.
/// </summary>
public static unsafe class AssetSource
{
    private static Func<AssetId, byte[]?>? _reader;

    /// <summary>
    /// Installs a C# source: an asset's bytes, or null when there's no such
    /// asset; null uninstalls. Engine and editor code installs it: a source
    /// from a game assembly would keep the game loaded across a hot reload
    /// (ADR 0011).
    /// </summary>
    public static void Install(Func<AssetId, byte[]?>? reader)
    {
        _reader = reader;
        Api.SetAssetReader(reader is null
            ? 0
            : (ulong)(delegate* unmanaged<AssetId*, nint, delegate* unmanaged<nint, byte*, ulong, void>, int>)&Deliver);
    }

    /// <summary>
    /// Installs it unless a source is installed already (a native module's):
    /// a default, such as a game's imported files. False if one was.
    /// </summary>
    public static bool InstallDefault(Func<AssetId, byte[]?> reader)
    {
        if (Api.AssetSourceInstalled())
        {
            return false;
        }
        Install(reader);
        return true;
    }

    /// <summary>Whether a source is installed.</summary>
    public static bool IsInstalled => Api.AssetSourceInstalled();

    /// <summary>An asset's bytes from the installed source, whichever language it's in; null if it has none.</summary>
    public static byte[]? Read(AssetId id)
    {
        ulong size = Api.AssetSize(id);
        if (size == ulong.MaxValue)
        {
            return null;
        }
        byte[] bytes = new byte[size];
        Api.CopyAsset(id, bytes);
        return bytes;
    }

    // Called by native code while it reads an asset: the bytes are handed
    // over during the call, pinned. An exception can't cross into native
    // code, so a failing source counts as no such asset.
    [UnmanagedCallersOnly]
    private static int Deliver(AssetId* id, nint context, delegate* unmanaged<nint, byte*, ulong, void> deliver)
    {
        byte[]? bytes;
        try
        {
            bytes = _reader?.Invoke(*id);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"crucible: the asset source failed for {*id}: {exception.Message}");
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
