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
}