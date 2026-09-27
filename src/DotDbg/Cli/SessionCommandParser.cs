namespace DotDbg.Cli;

internal static class SessionCommandParser
{
    internal static ParseResult ParseCatch(IReadOnlyList<string> tail, string cwd)
    {
        if (tail.Count == 0)
            return ParseResult.Ok(new CatchCommand(cwd, null));
        if (tail.Count == 1 && tail[0] is "all" or "user" or "unhandled" or "none")
            return ParseResult.Ok(new CatchCommand(cwd, tail[0]));
        return ParseResult.Fail(
            "catch: use 'catch', 'catch user', 'catch all', 'catch unhandled', or 'catch none'"
        );
    }

    internal static ParseResult ParseStep(StepKind kind, IReadOnlyList<string> tail, string cwd)
    {
        if (tail.Count > 0 && !(tail.Count == 1 && tail[0] == "--wait"))
            return ParseResult.Fail(
                $"{kind.ToString().ToLowerInvariant()}: step commands already wait; use no arguments or --wait"
            );

        return ParseResult.Ok(new StepCommand(cwd, kind));
    }

    internal static ParseResult ParseUntil(IReadOnlyList<string> tail, string cwd)
    {
        var location = tail.Count > 0 ? string.Join(' ', tail) : null;

        if (location is not null && !CommandParser.ValidateLocation(location, out var error))
            return ParseResult.Fail($"until: {error}");

        return ParseResult.Ok(new UntilCommand(cwd, location));
    }

    internal static ParseResult ParseBacktrace(IReadOnlyList<string> tail, string cwd)
    {
        var all = false;
        int? threadId = null;

        for (var i = 0; i < tail.Count; i++)
        {
            var a = tail[i];
            if (a.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                all = true;
                continue;
            }

            if (a.Equals("--thread", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= tail.Count)
                    return ParseResult.Fail("backtrace: --thread requires a thread id");

                if (!int.TryParse(tail[i + 1], out var tid) || tid < 1)
                    return ParseResult.Fail($"backtrace: invalid thread id '{tail[i + 1]}'");

                threadId = tid;
                i++;
                continue;
            }

            return ParseResult.Fail($"backtrace: unknown argument '{a}'");
        }

        if (all && threadId is not null)
            return ParseResult.Fail("backtrace: 'all' and '--thread' cannot be combined");

        return ParseResult.Ok(new BacktraceCommand(cwd, all, threadId));
    }

    internal static ParseResult ParseFrame(IReadOnlyList<string> tail, string cwd)
    {
        if (tail.Count == 0)
            return ParseResult.Ok(new FrameCommand(cwd, null));

        if (tail.Count > 1)
            return ParseResult.Fail("frame: takes at most one index");

        if (!CommandParser.TryParseIntArg(tail, out var index, 0))
            return ParseResult.Fail($"frame: invalid frame index '{tail[0]}'");

        return ParseResult.Ok(new FrameCommand(cwd, index));
    }

    internal static ParseResult ParseThread(IReadOnlyList<string> tail, string cwd)
    {
        if (tail.Count == 0)
            return ParseResult.Ok(new ThreadCommand(cwd, null));

        if (tail.Count > 1)
            return ParseResult.Fail("thread: takes at most one id");

        if (!CommandParser.TryParseIntArg(tail, out var id))
            return ParseResult.Fail($"thread: invalid thread id '{tail[0]}'");

        return ParseResult.Ok(new ThreadCommand(cwd, id));
    }

    internal static ParseResult ParseList(IReadOnlyList<string> tail, string cwd)
    {
        var locationTokens = new List<string>();
        var lineCount = 5;
        var hasLineCount = false;
        for (var i = 0; i < tail.Count; i++)
        {
            if (tail[i] == "--lines")
            {
                if (hasLineCount)
                    return ParseResult.Fail("list: --lines may be specified only once");
                if (
                    ++i >= tail.Count
                    || !int.TryParse(tail[i], out lineCount)
                    || lineCount is < 1 or > 100
                )
                    return ParseResult.Fail("list: --lines requires a number from 1 to 100");
                hasLineCount = true;
                continue;
            }

            if (tail[i].StartsWith('-'))
                return ParseResult.Fail($"list: unknown option '{tail[i]}'");
            locationTokens.Add(tail[i]);
        }

        var location = locationTokens.Count > 0 ? string.Join(' ', locationTokens) : null;

        if (location is not null && !CommandParser.ValidateLocation(location, out var error))
            return ParseResult.Fail($"list: {error}");

        return ParseResult.Ok(new ListCommand(cwd, location, lineCount));
    }

    internal static ParseResult ParsePrint(IReadOnlyList<string> tail, string cwd)
    {
        if (tail.Count == 0)
            return ParseResult.Fail("print: missing expression");

        return ParseResult.Ok(new PrintCommand(cwd, string.Join(' ', tail)));
    }

    internal static ParseResult ParseWatch(IReadOnlyList<string> tail, string cwd)
    {
        if (tail.Count == 0)
            return ParseResult.Fail("watch: missing expression");

        return ParseResult.Ok(new WatchCommand(cwd, string.Join(' ', tail)));
    }

    internal static ParseResult ParseUnwatch(IReadOnlyList<string> tail, string cwd)
    {
        if (tail.Count == 0)
            return ParseResult.Fail("unwatch: missing watch id");

        if (tail.Count > 1)
            return ParseResult.Fail("unwatch: takes a single id");

        if (!CommandParser.TryParseIntArg(tail, out var id))
            return ParseResult.Fail($"unwatch: invalid watch id '{tail[0]}'");

        return ParseResult.Ok(new UnwatchCommand(cwd, id));
    }

    internal static ParseResult ParseTraceLog(IReadOnlyList<string> tail, string cwd)
    {
        string? filePath = null;
        var clear = false;

        foreach (var a in tail)
        {
            if (a.Equals("--clear", StringComparison.OrdinalIgnoreCase))
            {
                clear = true;
                continue;
            }

            if (filePath is not null)
                return ParseResult.Fail("trace-log: multiple file paths specified");

            filePath = a;
        }

        return ParseResult.Ok(new TraceLogCommand(cwd, filePath, clear));
    }

    internal static ParseResult ParseHelp(IReadOnlyList<string> tail, string cwd)
    {
        if (tail.Count == 0)
            return ParseResult.Ok(new HelpCommand(cwd, null));

        var raw = string.Join(' ', tail);
        var subject = CommandParser.CanonicalizeCommand(raw);
        return ParseResult.Ok(new HelpCommand(cwd, subject));
    }

    internal static ParseResult ParseInfo(IReadOnlyList<string> tail, string cwd)
    {
        if (tail.Count == 0)
            return ParseResult.Fail("info: missing subject");

        var traceSubject = CommandParser.TryGetCanonicalInfoSubject(tail[0]);
        if (traceSubject == "trace")
        {
            long? since = null;
            var limit = 100;
            for (var i = 1; i < tail.Count; i++)
            {
                if (tail[i] == "--since")
                {
                    if (++i >= tail.Count || !long.TryParse(tail[i], out var value) || value < 0)
                        return ParseResult.Fail(
                            "info trace: --since requires a nonnegative line index"
                        );
                    since = value;
                }
                else if (tail[i] == "--limit")
                {
                    if (
                        ++i >= tail.Count
                        || !int.TryParse(tail[i], out limit)
                        || limit is < 1 or > 200
                    )
                        return ParseResult.Fail("info trace: --limit must be 1-200");
                }
                else
                    return ParseResult.Fail($"info trace: unknown argument '{tail[i]}'");
            }
            return ParseResult.Ok(new InfoCommand(cwd, "trace", since, limit));
        }

        var raw = string.Join(' ', tail);
        var subject = CommandParser.TryGetCanonicalInfoSubject(raw) ?? raw;
        return ParseResult.Ok(new InfoCommand(cwd, subject));
    }

    internal static ParseResult ParseSource(IReadOnlyList<string> tail, string cwd)
    {
        if (tail.Count == 0)
            return ParseResult.Fail("source: missing file or inline commands");

        var lastOnly = tail[0].Equals("--last", StringComparison.OrdinalIgnoreCase);
        var tailCount = 0;
        var start = lastOnly ? 1 : 0;
        if (tail[0].Equals("--tail", StringComparison.OrdinalIgnoreCase))
        {
            if (tail.Count < 2 || !int.TryParse(tail[1], out tailCount) || tailCount is < 1 or > 20)
                return ParseResult.Fail("source: --tail requires a count from 1 to 20");
            start = 2;
        }
        if (start >= tail.Count)
            return ParseResult.Fail("source: missing file or inline commands");
        if (
            tail[start].Equals("--last", StringComparison.OrdinalIgnoreCase)
            || tail[start].Equals("--tail", StringComparison.OrdinalIgnoreCase)
        )
            return ParseResult.Fail("source: use either --last or --tail N");

        if (
            tail[start].Equals("-c", StringComparison.OrdinalIgnoreCase)
            || tail[start].Equals("--commands", StringComparison.OrdinalIgnoreCase)
        )
        {
            var script = string.Join(' ', tail.Skip(start + 1));
            if (string.IsNullOrWhiteSpace(script))
                return ParseResult.Fail("source: missing inline commands");

            return ParseResult.Ok(new SourceCommand(cwd, true, script, lastOnly, tailCount));
        }

        var path = string.Join(' ', tail.Skip(start));
        return ParseResult.Ok(new SourceCommand(cwd, false, path, lastOnly, tailCount));
    }

    internal static ParseResult ParseWait(IReadOnlyList<string> tail, string cwd)
    {
        if (tail.Count == 0)
            return ParseResult.Ok(new WaitCommand(cwd));

        if (
            tail.Count != 2
            || tail[0] != "--timeout"
            || !int.TryParse(tail[1], out var seconds)
            || seconds is < 1 or > 3600
        )
            return ParseResult.Fail("wait: use --timeout <seconds> (1-3600)");

        return ParseResult.Ok(new WaitCommand(cwd, seconds));
    }

    internal static ParseResult ParseContinue(IReadOnlyList<string> tail, string cwd)
    {
        var wait = false;
        int? timeout = null;
        int? targetBreakpointId = null;
        for (var i = 0; i < tail.Count; i++)
        {
            switch (tail[i])
            {
                case "--wait":
                    wait = true;
                    break;
                case "--to" when targetBreakpointId is null:
                    if (++i >= tail.Count || !int.TryParse(tail[i], out var id) || id < 1)
                        return ParseResult.Fail("continue: --to requires a breakpoint id");
                    targetBreakpointId = id;
                    wait = true;
                    break;
                case "--timeout" when timeout is null:
                    if (
                        ++i >= tail.Count
                        || !int.TryParse(tail[i], out var seconds)
                        || seconds is < 1 or > 3600
                    )
                        return ParseResult.Fail("continue: --timeout requires 1-3600 seconds");
                    timeout = seconds;
                    break;
                default:
                    return ParseResult.Fail(
                        "continue: use [--wait] [--to <breakpoint-id>] [--timeout <seconds>]"
                    );
            }
        }

        if (timeout is not null && !wait)
            return ParseResult.Fail("continue: --timeout requires --wait or --to");
        return ParseResult.Ok(new ContinueCommand(cwd, wait, timeout, targetBreakpointId));
    }

    internal static ParseResult ParseEventQuery(
        IReadOnlyList<string> tail,
        string cwd,
        bool outputOnly
    )
    {
        var command = outputOnly ? "output" : "events";
        long? since = null;
        var limit = 50;
        string? channel = null;
        string? kind = null;
        for (var i = 0; i < tail.Count; i++)
        {
            var option = tail[i];
            if (option == "--since")
            {
                if (++i >= tail.Count || !long.TryParse(tail[i], out var value) || value < 0)
                    return ParseResult.Fail(
                        $"{command}: --since requires a nonnegative sequence number"
                    );
                since = value;
            }
            else if (option == "--limit")
            {
                if (++i >= tail.Count || !int.TryParse(tail[i], out limit) || limit is < 1 or > 200)
                    return ParseResult.Fail($"{command}: --limit must be 1-200");
            }
            else if (outputOnly && option == "--channel")
            {
                if (++i >= tail.Count || tail[i] is not ("stdout" or "stderr" or "debug"))
                    return ParseResult.Fail("output: --channel must be stdout, stderr, or debug");
                channel = tail[i];
            }
            else if (!outputOnly && option == "--kind")
            {
                if (
                    ++i >= tail.Count
                    || tail[i] is not ("stop" or "trace" or "output" or "diagnostic")
                )
                    return ParseResult.Fail(
                        "events: --kind must be stop, trace, output, or diagnostic"
                    );
                kind = tail[i];
            }
            else
            {
                return ParseResult.Fail($"{command}: unknown argument '{option}'");
            }
        }

        return outputOnly
            ? ParseResult.Ok(new OutputCommand(cwd, since, limit, channel))
            : ParseResult.Ok(new EventsCommand(cwd, since, limit, kind));
    }

    internal static ParseResult ParseDecompile(IReadOnlyList<string> tail, string cwd)
    {
        var ilMode = false;
        var remaining = new List<string>();
        foreach (var a in tail)
        {
            if (a.Equals("--il", StringComparison.OrdinalIgnoreCase))
            {
                ilMode = true;
                continue;
            }
            remaining.Add(a);
        }

        if (remaining.Count > 0)
            return ParseResult.Fail("decompile: only --il is supported");

        return ParseResult.Ok(new DecompileCommand(cwd, null, ilMode));
    }
}
