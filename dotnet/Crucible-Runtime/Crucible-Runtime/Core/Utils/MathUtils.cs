namespace Crucible.Core.Utils;

public static class MathUtils
{
    public static readonly float FLOAT_EPSILON = .0001f;

    public static bool Approximately(float float1, float float2)
    {
        return MathF.Abs(float1 - float2) <= FLOAT_EPSILON;
    }
    
}