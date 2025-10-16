namespace Crucible.Core.Math;

public static class Common
{
    /// <summary>
    /// Epsilon for comparing similarity of floating point values
    /// </summary>
    public static readonly float FLOAT_EPSILON = .0001f;
    
    /// <summary>
    /// Compare floats to see if they are functionally equivalent, even if they are not exact floating point representation matches
    /// </summary>
    /// <param name="float1"></param>
    /// <param name="float2"></param>
    /// <returns></returns>
    public static bool Approximately(float float1, float float2)
    {
        return MathF.Abs(float1 - float2) <= FLOAT_EPSILON;
    }
}