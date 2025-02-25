using System.Text;

namespace Crucible.Core;

public unsafe struct UUID
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

    public override string ToString()
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < 16; i++)
        {
            sb.Append((char)_bytes[i]);
        }
        return sb.ToString();
    }
}