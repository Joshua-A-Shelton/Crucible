using System.Runtime.InteropServices;
using System.Text;

namespace Crucible.Core;
/// <summary>
/// Universally Unique Identifier
/// </summary>
public unsafe partial struct UUID: IEquatable<UUID>
{
    private fixed byte _bytes[16];

    public static bool operator ==(UUID u1, UUID u2)
    {
        bool same = true;
        for (int i = 0; i < 16; i++)
        {
            if (u1._bytes[i] != u2._bytes[i])
            {
                same = false;
                break;
            }
        }
        return same;
    }
    
    public static bool operator !=(UUID u1, UUID u2)
    {
        return !(u1 == u2);
    }

    public bool Equals(UUID other)
    {
        return this == other;
    }

    public override string ToString()
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < 16; i++)
        {
            sb.Append((char)_bytes[i]);
        }
        return sb.ToString();
    }
    
    public override bool Equals(object? obj)
    {
        if (obj is UUID against)
        {
            return this == against;
        }
        return false;
    }
    
    [LibraryImport("Crucible")]
    private static partial int CRUCIBLE_NATIVE_UUIDHash(ref UUID uuid);

    public override int GetHashCode()
    {
        return CRUCIBLE_NATIVE_UUIDHash(ref this);
    }
}