namespace DotDbg.Cli;

/// <summary>
/// Global options parsed from the invocation (before the command itself).
/// </summary>
public sealed record GlobalOptions(
    string SessionId,
    bool Daemon,
    bool JsonOutput,
    bool Help,
    IReadOnlyList<string> CommandArgs,
    string? JsonInputFile = null
);
