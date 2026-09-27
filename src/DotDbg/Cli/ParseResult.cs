namespace DotDbg.Cli;

/// <summary>
/// Result of parsing a single command. On success <see cref="Request"/> is set;
/// on failure <see cref="Error"/> is set and the caller should exit with <see cref="ExitCode"/>.
/// </summary>
public sealed record ParseResult(
    bool Success,
    CommandRequest? Request,
    string? Error,
    int ExitCode = 2
)
{
    public static ParseResult Ok(CommandRequest request) => new(true, request, null, 0);

    public static ParseResult Fail(string error, int exitCode = 2) =>
        new(false, null, error, exitCode);
}
