namespace DotDbg.Cli;

/// <summary>
/// Thrown by the parser when global options or tokenization cannot be processed.
/// Command parse errors are returned through <see cref="ParseResult"/> instead.
/// </summary>
public sealed class CommandParseException : Exception
{
    public int ExitCode { get; }

    public CommandParseException(string message, int exitCode = 2)
        : base(message)
    {
        ExitCode = exitCode;
    }
}
