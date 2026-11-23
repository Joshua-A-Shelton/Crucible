using Crucible.Importers;
using ImportersApi = Crucible.Importers.Api;

namespace Crucible.Editor;

/// <summary>How a dropped image is imported (ADR 0115): the choices its prompt offers.</summary>
public enum ImageKind
{
    PixelArt, // TextureUsage.Sprite: the source's pixels exactly
    Texture,  // TextureUsage.Color: BC7, mipmapped
    Ui,       // TextureUsage.Ui: RGBA8, no mips
}

/// <summary>
/// The prompt's first guess for a dropped image (ADR 0115), from its pixels:
/// pixel art if it's at most 512x512, has at most 256 colors, and its alpha
/// is only fully clear or fully opaque; else a texture. Texture isn't offered
/// for a size block compression can't store (not a multiple of 4).
/// </summary>
public readonly record struct ImageGuess(ImageKind Kind, int Width, int Height)
{
    public const int MaxPixelArtSize = 512;
    public const int MaxPixelArtColors = 256;

    /// <summary>Whether Texture is offered: BC7 stores whole 4x4 blocks.</summary>
    public bool TextureAllowed => Width % 4 == 0 && Height % 4 == 0;

    /// <summary>The guess for an image file's bytes; null if it can't be read as an image.</summary>
    public static ImageGuess? Of(ReadOnlySpan<byte> source)
    {
        // Pixel art imports with the source's pixels exactly: RGBA8, one level.
        TextureSettings settings = new() { Usage = TextureUsage.Sprite };
        TextureImportHandle texture;
        try
        {
            texture = ImportersApi.ImportTexture(source, settings);
        }
        catch (CrucibleException)
        {
            return null;
        }
        try
        {
            Crucible.Assets.TextureInfo info = ImportersApi.TextureInfo(texture);
            int width = (int)info.Width, height = (int)info.Height;
            bool fits = width <= MaxPixelArtSize && height <= MaxPixelArtSize;
            bool pixelArt = fits && LooksDrawn(texture, width, height);
            ImageKind kind = pixelArt ? ImageKind.PixelArt : width % 4 == 0 && height % 4 == 0 ? ImageKind.Texture : ImageKind.Ui;
            return new ImageGuess(kind, width, height);
        }
        finally
        {
            ImportersApi.DestroyTextureImport(texture);
        }
    }

    // Few colors and hard-edged alpha: drawn pixel by pixel, not photographed or painted.
    private static bool LooksDrawn(TextureImportHandle texture, int width, int height)
    {
        byte[] rgba = new byte[ImportersApi.TextureMipSize(texture, 0)];
        ImportersApi.CopyTextureMip(texture, 0, rgba);
        HashSet<uint> colors = [];
        for (int i = 0; i + 3 < rgba.Length && i < width * height * 4; i += 4)
        {
            byte alpha = rgba[i + 3];
            if (alpha is not (0 or 255))
            {
                return false;
            }
            if (alpha == 0)
            {
                continue; // clear pixels' colors don't show
            }
            colors.Add(rgba[i] | ((uint)rgba[i + 1] << 8) | ((uint)rgba[i + 2] << 16));
            if (colors.Count > MaxPixelArtColors)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>The import settings a kind starts with, as a sidecar's Settings.</summary>
    public static System.Text.Json.Nodes.JsonObject SettingsFor(ImageKind kind) => kind switch
    {
        ImageKind.PixelArt => new() { ["Usage"] = nameof(TextureUsage.Sprite), ["Mipmaps"] = false, ["Wrap"] = false },
        ImageKind.Ui => new() { ["Usage"] = nameof(TextureUsage.Ui), ["Mipmaps"] = false, ["Wrap"] = false },
        _ => new() { ["Usage"] = nameof(TextureUsage.Color) },
    };
}
