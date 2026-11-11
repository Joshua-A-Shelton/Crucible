namespace Crucible.Log;

/// <summary>
/// The core's messages in C# (ADR 0087): <see cref="Pump"/> takes those
/// written since the last call and raises <see cref="Received"/> for each,
/// oldest first. One owner pumps (the editor, each frame); the core has
/// already printed them to the terminal.
/// </summary>
public static partial class Api
{
    public static event Action<LogLevel, string>? Received;

    public static void Pump()
    {
        uint count = TakeMessages();
        for (uint i = 0; i < count; ++i)
        {
            Received?.Invoke(TakenLevel(i), TakenText(i));
        }
    }
}
