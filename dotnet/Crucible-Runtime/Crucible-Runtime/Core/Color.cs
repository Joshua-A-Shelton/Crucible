using System.Runtime.InteropServices;

namespace Crucible.Core;
/// <summary>
/// Struct that contains the RGBA values of a color (0-255)
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Color
{
    public byte R;
    public byte G;
    public byte B;
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
}