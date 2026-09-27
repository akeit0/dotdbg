using System.Text;
using System.Text.Json.Nodes;

namespace DotDbg.Cli;

/// <summary>
/// Parses dotdbg invocation options and individual commands.
/// Produces typed <see cref="CommandRequest"/> objects and detailed <see cref="ParseResult"/> errors.
/// </summary>
public static class CommandParser
{
    internal static readonly IReadOnlyDictionary<string, string> CommandAliases = new Dictionary<
        string,
        string
    >(StringComparer.OrdinalIgnoreCase)
    {
        ["h"] = "help",
        ["?"] = "help",
        ["r"] = "run",
        ["a"] = "attach",
        ["att"] = "attach",
        ["det"] = "detach",
        ["b"] = "break",
        ["bp"] = "break",
        ["del"] = "delete",
        ["d"] = "delete",
        ["cl"] = "delete",
        ["clear"] = "delete",
        ["en"] = "enable",
        ["dis"] = "disable",
        ["cond"] = "condition",
        ["ign"] = "ignore",
        ["c"] = "continue",
        ["cont"] = "continue",
        ["int"] = "interrupt",
        ["n"] = "next",
        ["s"] = "step",
        ["fin"] = "finish",
        ["bt"] = "backtrace",
        ["where"] = "backtrace",
        ["f"] = "frame",
        ["l"] = "list",
        ["p"] = "print",
        ["w"] = "watch",
        ["ws"] = "watches",
        ["tp"] = "trace",
        ["bps"] = "breakpoints",
        ["i"] = "info",
        ["t"] = "thread",
        ["th"] = "thread",
        ["u"] = "up",
        ["dn"] = "down",
        ["k"] = "kill",
        ["so"] = "source",
        ["cat"] = "catch",
        ["q"] = "quit",
        ["dec"] = "decompile",
        ["decomp"] = "decompile",
        ["ctx"] = "context",
    };

    internal static readonly IReadOnlyDictionary<string, string> InfoSubjectAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["b"] = "breakpoints",
            ["bp"] = "breakpoints",
            ["bps"] = "breakpoints",
            ["breakpoints"] = "breakpoints",
            ["files"] = "files",
            ["file"] = "files",
            ["target"] = "files",
            ["targets"] = "files",
            ["mods"] = "modules",
            ["mod"] = "modules",
            ["modules"] = "modules",
            ["ex"] = "exception",
            ["exc"] = "exception",
            ["exception"] = "exception",
            ["status"] = "status",
            ["s"] = "status",
            ["locals"] = "locals",
            ["loc"] = "locals",
            ["args"] = "args",
            ["arg"] = "args",
            ["arguments"] = "args",
            ["threads"] = "threads",
            ["thread"] = "threads",
            ["trace"] = "trace",
            ["tr"] = "trace",
            ["trace-log"] = "trace",
            ["trlog"] = "trace",
            ["tl"] = "trace",
        };

    public static string? TryGetCanonicalCommand(string raw) =>
        CommandAliases.TryGetValue(raw, out var canonical) ? canonical : null;

    public static string CanonicalizeCommand(string raw) =>
        TryGetCanonicalCommand(raw) ?? raw.ToLowerInvariant();

    public static string? TryGetCanonicalInfoSubject(string raw) =>
        InfoSubjectAliases.TryGetValue(raw, out var canonical) ? canonical : null;

    /// <summary>
    /// Parses invocation options from the process argv. Stops at the first non-option token
    /// or at <c>--</c>, so command args like <c>run -v</c> are not mistaken for global <c>-v</c>.
    /// </summary>
    public static GlobalOptions ParseInvocation(string[] args)
    {
        var sessionId = string.Empty;
        var daemon = false;
        var jsonOutput = false;
        var help = false;
        string? jsonInputFile = null;
        var commandArgs = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg == "--")
            {
                commandArgs.AddRange(args.Skip(i + 1));
                break;
            }

            if (arg is "-s" or "--session-id")
            {
                if (i + 1 >= args.Length)
                    throw new CommandParseException("Missing value for -s/--session-id");
                sessionId = args[++i];
                if (string.IsNullOrWhiteSpace(sessionId))
                    throw new CommandParseException("-s/--session-id value cannot be empty");
                continue;
            }

            if (arg.StartsWith("--session-id="))
            {
                sessionId = arg.Substring("--session-id=".Length);
                if (string.IsNullOrWhiteSpace(sessionId))
                    throw new CommandParseException("--session-id= value cannot be empty");
                continue;
            }

            if (arg is "--json-input")
            {
                if (i + 1 >= args.Length)
                    throw new CommandParseException("Missing value for --json-input");
                jsonInputFile = args[++i];
                if (string.IsNullOrWhiteSpace(jsonInputFile))
                    throw new CommandParseException("--json-input value cannot be empty");
                continue;
            }

            if (arg.StartsWith("--json-input="))
            {
                jsonInputFile = arg.Substring("--json-input=".Length);
                if (string.IsNullOrWhiteSpace(jsonInputFile))
                    throw new CommandParseException("--json-input= value cannot be empty");
                continue;
            }

            if (arg is "-d" or "--daemon")
            {
                daemon = true;
                continue;
            }

            if (arg is "--json" or "-v" or "--verbose")
            {
                jsonOutput = true;
                continue;
            }

            if (arg is "-h" or "--help")
            {
                help = true;
                continue;
            }

            if (arg.StartsWith("-"))
            {
                throw new CommandParseException($"Unknown global option: {arg}");
            }

            commandArgs.AddRange(args.Skip(i));
            break;
        }

        if (daemon && commandArgs.Count > 0)
            throw new CommandParseException("--daemon cannot be combined with a command");
        if (daemon && !string.IsNullOrWhiteSpace(jsonInputFile))
            throw new CommandParseException("--daemon cannot be combined with --json-input");
        if (!string.IsNullOrWhiteSpace(jsonInputFile) && commandArgs.Count > 0)
            throw new CommandParseException("--json-input cannot be combined with a command");

        if (string.IsNullOrEmpty(sessionId))
            sessionId = DefaultSessionIdFromWorkingDirectory();

        return new GlobalOptions(sessionId, daemon, jsonOutput, help, commandArgs, jsonInputFile);
    }

    /// <summary>
    /// Parses a script or REPL line. The line is tokenized first, then parsed as a command.
    /// </summary>
    public static ParseResult ParseCommandLine(string line, string cwd)
    {
        try
        {
            var args = CommandLineTokenizer.Tokenize(line);
            return ParseCommand(args, cwd);
        }
        catch (CommandParseException ex)
        {
            return ParseResult.Fail(ex.Message, ex.ExitCode);
        }
    }

    /// <summary>
    /// Parses a line inside source or JSON batch input. Expression-bearing commands
    /// retain C# string literals that the command-line tokenizer would otherwise remove.
    /// </summary>
    public static ParseResult ParseScriptCommandLine(string line, string cwd)
    {
        try
        {
            var args = CommandLineTokenizer.Tokenize(line);
            if (args.Count == 0)
                return ParseResult.Fail("No command specified");

            var command = CanonicalizeCommand(args[0]);
            if (command is "condition" or "print" or "watch")
            {
                var prefixCount = command == "condition" ? 2 : 1;
                var expression = RawTailAfterWords(line, prefixCount);
                if (!string.IsNullOrWhiteSpace(expression))
                {
                    var prefix = args.Take(prefixCount).ToList();
                    prefix.Add(NormalizeScriptExpression(expression));
                    return ParseCommand(prefix, cwd);
                }
            }

            if (command == "break" && FindUnquotedWord(line, "if") is var ifIndex && ifIndex >= 0)
            {
                var prefix = CommandLineTokenizer.Tokenize(line[..ifIndex]).ToList();
                prefix.Add("if");
                var conditionTail = line[(ifIndex + 2)..].Trim();
                var nameIndex = FindUnquotedWord(conditionTail, "--name");
                var condition = nameIndex < 0 ? conditionTail : conditionTail[..nameIndex].Trim();
                if (string.IsNullOrWhiteSpace(condition))
                    return ParseResult.Fail("break: missing condition after if");
                prefix.Add(NormalizeScriptExpression(condition));
                if (nameIndex >= 0)
                    prefix.AddRange(CommandLineTokenizer.Tokenize(conditionTail[nameIndex..]));
                return ParseCommand(prefix, cwd);
            }

            if (command == "trace" && args.Count >= 2)
            {
                var expressionTail = RawTailAfterWords(line, 2);
                if (!string.IsNullOrWhiteSpace(expressionTail))
                {
                    var nameIndex = FindUnquotedWord(expressionTail, "--name");
                    var expression =
                        nameIndex < 0 ? expressionTail : expressionTail[..nameIndex].Trim();
                    if (string.IsNullOrWhiteSpace(expression))
                        return ParseResult.Fail("trace: missing expression before --name");
                    var traceArgs = new List<string>
                    {
                        args[0],
                        args[1],
                        NormalizeScriptExpression(expression),
                    };
                    if (nameIndex >= 0)
                        traceArgs.AddRange(
                            CommandLineTokenizer.Tokenize(expressionTail[nameIndex..])
                        );
                    return ParseCommand(traceArgs, cwd);
                }
            }

            return ParseCommand(args, cwd);
        }
        catch (CommandParseException ex)
        {
            return ParseResult.Fail(ex.Message, ex.ExitCode);
        }
    }

    private static string? RawTailAfterWords(string line, int words)
    {
        var index = 0;
        for (var word = 0; word < words; word++)
        {
            while (index < line.Length && char.IsWhiteSpace(line[index]))
                index++;
            if (index == line.Length)
                return null;
            char? quote = null;
            while (index < line.Length)
            {
                var c = line[index];
                if (c == '\\' && index + 1 < line.Length && line[index + 1] is '\'' or '"' or '\\')
                {
                    index += 2;
                    continue;
                }
                if (c is '\'' or '"')
                    quote =
                        quote == c ? null
                        : quote is null ? c
                        : quote;
                else if (quote is null && char.IsWhiteSpace(c))
                    break;
                index++;
            }
        }

        return line[index..].Trim();
    }

    private static string NormalizeScriptExpression(string expression)
    {
        if (
            expression.Length >= 4
            && expression[0] == '\''
            && expression[^1] == '\''
            && expression[1..^1].Any(char.IsWhiteSpace)
        )
        {
            var tokens = CommandLineTokenizer.Tokenize(expression);
            if (tokens.Count == 1)
                return tokens[0];
        }

        if (
            expression.Contains("\\\"", StringComparison.Ordinal)
            && !ContainsUnescapedDoubleQuote(expression)
        )
            return expression.Replace("\\\"", "\"", StringComparison.Ordinal);

        return expression;
    }

    private static bool ContainsUnescapedDoubleQuote(string text)
    {
        var escaped = false;
        foreach (var c in text)
        {
            if (c == '"' && !escaped)
                return true;
            if (c == '\\')
                escaped = !escaped;
            else
                escaped = false;
        }
        return false;
    }

    private static int FindUnquotedWord(string line, string word)
    {
        char? quote = null;
        var escaped = false;
        for (var i = 0; i <= line.Length - word.Length; i++)
        {
            var c = line[i];
            if (c is '\'' or '"' && !escaped)
                quote =
                    quote == c ? null
                    : quote is null ? c
                    : quote;
            if (
                quote is null
                && line.AsSpan(i).StartsWith(word, StringComparison.OrdinalIgnoreCase)
                && (i == 0 || char.IsWhiteSpace(line[i - 1]))
                && (i + word.Length == line.Length || char.IsWhiteSpace(line[i + word.Length]))
            )
                return i;
            if (c == '\\')
                escaped = !escaped;
            else
                escaped = false;
        }
        return -1;
    }

    /// <summary>
    /// Parses already-tokenized command arguments (e.g. from process argv or a script line).
    /// </summary>
    public static ParseResult ParseCommand(IReadOnlyList<string> args, string cwd)
    {
        if (args.Count == 0)
            return ParseResult.Fail("No command specified");

        var command = CanonicalizeCommand(args[0]);
        var tail = args.Skip(1).ToList();

        try
        {
            return command switch
            {
                "file" => LaunchCommandParser.ParseFile(tail, cwd),
                "run" => LaunchCommandParser.ParseRun(tail, cwd),
                "attach" => LaunchCommandParser.ParseAttach(tail, cwd),
                "detach" => ParseNoArgs<DetachCommand>(tail, cwd),
                "break" => BreakpointCommandParser.ParseBreak(tail, cwd),
                "trace" => BreakpointCommandParser.ParseTrace(tail, cwd),
                "delete" => BreakpointCommandParser.ParseDelete(tail, cwd),
                "enable" => BreakpointCommandParser.ParseEnable(tail, cwd),
                "disable" => BreakpointCommandParser.ParseDisable(tail, cwd),
                "condition" => BreakpointCommandParser.ParseCondition(tail, cwd),
                "ignore" => BreakpointCommandParser.ParseIgnore(tail, cwd),
                "continue" => SessionCommandParser.ParseContinue(tail, cwd),
                "interrupt" => ParseNoArgs<InterruptCommand>(tail, cwd),
                "next" => SessionCommandParser.ParseStep(StepKind.Next, tail, cwd),
                "step" => SessionCommandParser.ParseStep(StepKind.In, tail, cwd),
                "finish" => SessionCommandParser.ParseStep(StepKind.Out, tail, cwd),
                "until" => SessionCommandParser.ParseUntil(tail, cwd),
                "backtrace" => SessionCommandParser.ParseBacktrace(tail, cwd),
                "frame" => SessionCommandParser.ParseFrame(tail, cwd),
                "up" => ParseNoArgs<UpCommand>(tail, cwd),
                "down" => ParseNoArgs<DownCommand>(tail, cwd),
                "thread" => SessionCommandParser.ParseThread(tail, cwd),
                "list" => SessionCommandParser.ParseList(tail, cwd),
                "print" => SessionCommandParser.ParsePrint(tail, cwd),
                "watch" => SessionCommandParser.ParseWatch(tail, cwd),
                "unwatch" => SessionCommandParser.ParseUnwatch(tail, cwd),
                "watches" => ParseNoArgs<WatchesCommand>(tail, cwd),
                "trace-log" => SessionCommandParser.ParseTraceLog(tail, cwd),
                "catch" => SessionCommandParser.ParseCatch(tail, cwd),
                "schema" => ParseNoArgs<SchemaCommand>(tail, cwd),
                "help" => SessionCommandParser.ParseHelp(tail, cwd),
                "info" => SessionCommandParser.ParseInfo(tail, cwd),
                "context" => ParseNoArgs<ContextCommand>(tail, cwd),
                "events" => SessionCommandParser.ParseEventQuery(tail, cwd, false),
                "output" => SessionCommandParser.ParseEventQuery(tail, cwd, true),
                "wait" => SessionCommandParser.ParseWait(tail, cwd),
                "source" => SessionCommandParser.ParseSource(tail, cwd),
                "decompile" => SessionCommandParser.ParseDecompile(tail, cwd),
                "quit" => ParseNoArgs<QuitCommand>(tail, cwd),
                "kill" => ParseNoArgs<KillCommand>(tail, cwd),
                "breakpoints" => ParseNoArgs<BreakpointsCommand>(tail, cwd),
                _ => ParseResult.Fail($"Unknown command: {args[0]}"),
            };
        }
        catch (CommandParseException ex)
        {
            return ParseResult.Fail(ex.Message, ex.ExitCode);
        }
    }

    public static string DefaultSessionIdFromWorkingDirectory()
    {
        var path = Path.GetFullPath(Environment.CurrentDirectory);
        path = Path.TrimEndingDirectorySeparator(path);

        if (OperatingSystem.IsWindows())
            path = path.ToUpperInvariant();

        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(path));
        var bytes = hash.Take(8).ToArray();
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static ParseResult ParseNoArgs<T>(IReadOnlyList<string> tail, string cwd)
        where T : CommandRequest
    {
        if (tail.Count > 0)
            return ParseResult.Fail(
                $"{typeof(T).Name.Replace("Command", "").ToLowerInvariant()}: takes no arguments"
            );

        return ParseResult.Ok((T)Activator.CreateInstance(typeof(T), cwd)!);
    }

    internal static bool TryParseIntArg(IReadOnlyList<string> tail, out int value, int minValue = 1)
    {
        value = 0;
        if (tail.Count != 1 || !int.TryParse(tail[0], out var v))
            return false;

        if (v < minValue)
            return false;

        value = v;
        return true;
    }

    internal static bool ValidateLocation(string location, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(location))
        {
            error = "missing location";
            return false;
        }

        // Strip optional module prefix for validation.
        var raw = location.Split('!', 2).Last().Trim();
        if (!LocationParser.TryParse(raw, out _, out _, out _))
        {
            error = "invalid location. Use file:line or file:line:column";
            return false;
        }

        return true;
    }
}
