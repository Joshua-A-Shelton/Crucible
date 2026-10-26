using System.Runtime.InteropServices;

namespace Crucible.Interop;

/// <summary>
/// The raw layer: one declaration per exported C function, mirroring the C ABI
/// exactly (ADR 0017). Hand-written in M1; the binding generator replaces it
/// in M1 step 7. Game code uses the idiomatic layer instead.
/// </summary>
internal static partial class Native
{
    /// <summary>The core library's name; .NET adds the platform's prefix and extension.</summary>
    internal const string CoreLibrary = "crucible_core";

    [LibraryImport(CoreLibrary, EntryPoint = "crucible_hasRenderBackend")]
    internal static partial byte HasRenderBackend();
}
