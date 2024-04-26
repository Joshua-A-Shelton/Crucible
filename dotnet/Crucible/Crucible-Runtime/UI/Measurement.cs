namespace Crucible.UI;

public class Measurement
{
    public enum MeasurementUnit
    {
        pixels,
        percent
    }
    public float Amount;
    public MeasurementUnit Unit;
}