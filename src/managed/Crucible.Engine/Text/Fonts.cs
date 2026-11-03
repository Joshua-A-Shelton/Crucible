using Crucible.Assets;

namespace Crucible.Text;

/// <summary>
/// Fonts (ADR 0057): imported <c>.crfont</c> files, loaded for shaping and
/// glyph rasterization. <see cref="Acquire"/> loads a font on its first use
/// and counts uses; <see cref="Release"/> unloads it after the last, though
/// layouts made with it keep what they need. The bytes come from the reader
/// set with <see cref="Render.RenderAssets.SetReader"/>. Main thread only.
/// </summary>
public static class Fonts
{
    public static FontHandle Acquire(AssetId font) => Api.AcquireFont(font);

    public static void Release(FontHandle font) => Api.ReleaseFont(font);

    /// <summary>From a <c>.crfont</c>'s bytes, outside the asset system.</summary>
    public static FontHandle Load(ReadOnlySpan<byte> bytes) => Api.LoadFont(bytes);

    public static void Unload(FontHandle font) => Api.UnloadFont(font);
}
