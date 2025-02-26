using System.Text;

namespace Crucible.Core;

public unsafe struct UUID: IEquatable<UUID>
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
    
#pragma warning disable 0649
    private static delegate* unmanaged<ref UUID, int> _uuidHash_ptr;
#pragma warning restore 0649

    public override int GetHashCode()
    {
        return _uuidHash_ptr(ref this);
    }
}