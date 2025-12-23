using System.Runtime.InteropServices;

namespace Crucible.Core;
/// <summary>
/// Struct that contains the RGBA values of a color (0-255)
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Color
{
    /// <summary>
    /// Total red in the color (0-255)
    /// </summary>
    public byte R;
    /// <summary>
    /// Total green in the color (0-255)
    /// </summary>
    public byte G;
    /// <summary>
    /// Total blue in the color (0-255)
    /// </summary>
    public byte B;
    /// <summary>
    /// Total alpha (transparency) in the color (0-255), 0 fully transparent, 255 fully opaque
    /// </summary>
    public byte A;

    public Color(byte r, byte g, byte b, byte a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public static bool operator ==(Color c1, Color c2)
    {
        if (c1.R == c2.R && c1.G == c2.G && c1.B == c2.B && c1.A == c2.A)
        {
            return true;
        }
        return false;
    }

    public static bool operator !=(Color c1, Color c2)
    {
        return !(c1 == c2);
    }

    [Flags]
    internal enum ComponentFlags
    {
        RED_COMPONENT = 0b00000001,
        GREEN_COMPONENT = 0b00000010,
        BLUE_COMPONENT = 0b00000100,
        ALPHA_COMPONENT = 0b00001000
    }
}