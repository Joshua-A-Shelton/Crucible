using System.Diagnostics;
using System.Text;
using Crucible.Log;
using LogApi = Crucible.Log.Api;

namespace Crucible.Diagnostics;

/// <summary>
/// C#'s messages (ADR 0103), written into the core's log (ADR 0087): one
/// stream with C++'s, printed (information to standard output, warnings and
/// errors to standard error), written to a package's log file, and shown in
/// the editor's Log. A Shipping build compiles <see cref="Info"/> calls out,
/// arguments and all: they need the symbol <c>CRUCIBLE_LOG_INFO</c>, which
/// every build but Shipping defines (the engine's own, and the editor's
/// builds of a game).
/// </summary>
public static class Log
{
    [Conditional("CRUCIBLE_LOG_INFO")]
    public static void Info(string message) => LogApi.Write(LogLevel.Info, message);

    public static void Warning(string message) => LogApi.Write(LogLevel.Warning, message);

    public static void Error(string message) => LogApi.Write(LogLevel.Error, message);

    /// <summary>
    /// Sends <see cref="Console"/>'s output into the log from now on, a line
    /// at a time: <see cref="Console.Out"/> as information (dropped in a
    /// Shipping build) and <see cref="Console.Error"/> as errors. The host
    /// does it for games, so code that writes to the console, the game's or a
    /// library's, still reaches the log file; the editor keeps its console.
    /// </summary>
    public static void CatchConsole()
    {
        Console.SetOut(new LineWriter(LogLevel.Info));
        Console.SetError(new LineWriter(LogLevel.Error));
    }

    // Collects what's written until a line ends, then writes the line.
    private sealed class LineWriter(LogLevel level) : TextWriter
    {
        private readonly StringBuilder _line = new();

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            lock (_line)
            {
                if (value == '\n')
                {
                    Emit(_line);
                }
                else if (value != '\r')
                {
                    _line.Append(value);
                }
            }
        }

        public override void Write(string? value)
        {
            if (value is null)
            {
                return;
            }
            lock (_line)
            {
                foreach (char c in value)
                {
                    Write(c);
                }
            }
        }

        public override void WriteLine(string? value)
        {
            lock (_line)
            {
                _line.Append(value);
                Emit(_line);
            }
        }

        public override void Flush()
        {
            lock (_line)
            {
                if (_line.Length > 0)
                {
                    Emit(_line);
                }
            }
        }

        private void Emit(StringBuilder line)
        {
            LogApi.Write(level, line.ToString());
            line.Clear();
        }
    }
}
