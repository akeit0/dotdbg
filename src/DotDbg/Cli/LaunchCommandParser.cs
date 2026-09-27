using DotDbg.Util;

namespace DotDbg.Cli;

internal static class LaunchCommandParser
{
    internal static ParseResult ParseFile(IReadOnlyList<string> tail, string cwd)
    {
        if (tail.Count == 0)
            return ParseResult.Fail("file: missing path");

        string? configuration = null;
        var properties = new List<string>();
        var pathTokens = new List<string>();
        var stoppedOptions = false;

        for (var i = 0; i < tail.Count; )
        {
            var arg = tail[i];

            if (!stoppedOptions && arg == "--")
            {
                stoppedOptions = true;
                i++;
                continue;
            }

            if (!stoppedOptions && (arg is "-c" or "--configuration"))
            {
                if (i + 1 >= tail.Count)
                    return ParseResult.Fail("file: -c/--configuration requires a value");
                configuration = tail[i + 1];
                if (string.IsNullOrWhiteSpace(configuration))
                    return ParseResult.Fail("file: -c/--configuration value cannot be empty");
                i += 2;
                continue;
            }

            if (!stoppedOptions && arg.StartsWith("--configuration=", StringComparison.Ordinal))
            {
                configuration = arg["--configuration=".Length..];
                if (string.IsNullOrWhiteSpace(configuration))
                    return ParseResult.Fail("file: --configuration= value cannot be empty");
                i++;
                continue;
            }

            if (!stoppedOptions && (arg is "-p" or "--property"))
            {
                if (i + 1 >= tail.Count)
                    return ParseResult.Fail("file: -p/--property requires Name=Value");
                var property = tail[i + 1];
                if (!TryValidateBuildProperty(property, out var propertyError))
                    return ParseResult.Fail($"file: {propertyError}");
                properties.Add(property);
                i += 2;
                continue;
            }

            if (
                !stoppedOptions
                && (
                    arg.StartsWith("-p:", StringComparison.Ordinal)
                    || arg.StartsWith("--property=", StringComparison.Ordinal)
                )
            )
            {
                var property = arg.StartsWith("-p:", StringComparison.Ordinal)
                    ? arg[3..]
                    : arg["--property=".Length..];
                if (!TryValidateBuildProperty(property, out var propertyError))
                    return ParseResult.Fail($"file: {propertyError}");
                properties.Add(property);
                i++;
                continue;
            }

            if (!stoppedOptions && arg.StartsWith('-'))
                return ParseResult.Fail($"file: unknown option '{arg}'");

            pathTokens.Add(arg);
            i++;
        }

        var path = string.Join(' ', pathTokens);
        if (string.IsNullOrWhiteSpace(path))
            return ParseResult.Fail("file: missing path");

        var isBuildable =
            path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
        if (!isBuildable && (configuration is not null || properties.Count > 0))
            return ParseResult.Fail(
                "file: -c/--configuration and -p/--property are only valid with a .csproj or .cs path"
            );

        return ParseResult.Ok(
            new FileCommand(cwd, path, configuration, properties.Count == 0 ? null : properties)
        );
    }

    private static bool TryValidateBuildProperty(string property, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(property))
        {
            error = "-p/--property value cannot be empty";
            return false;
        }

        var eq = property.IndexOf('=');
        if (eq <= 0 || eq == property.Length - 1)
        {
            error = "-p/--property requires Name=Value";
            return false;
        }

        return true;
    }

    internal static ParseResult ParseRun(IReadOnlyList<string> tail, string cwd)
    {
        var justMyCode = true;
        var args = new List<string>();
        var environment = new Dictionary<string, string>(EnvironmentNameComparer.Instance);
        string? targetCwd = null;
        var wait = false;
        int? waitTimeoutSeconds = null;
        var afterSeparator = false;

        for (var i = 0; i < tail.Count; i++)
        {
            var token = tail[i];
            if (!afterSeparator && token == "--")
            {
                afterSeparator = true;
                continue;
            }

            if (!afterSeparator && token is "--no-jmc" or "--no-just-my-code")
            {
                justMyCode = false;
                continue;
            }

            if (!afterSeparator && token is "--env" or "-e")
            {
                if (
                    ++i >= tail.Count
                    || !TryParseEnvironmentAssignment(tail[i], out var name, out var value)
                )
                    return ParseResult.Fail("run: --env requires NAME=VALUE");
                environment[name] = value;
                continue;
            }

            if (!afterSeparator && token.StartsWith("--env=", StringComparison.Ordinal))
            {
                if (!TryParseEnvironmentAssignment(token[6..], out var name, out var value))
                    return ParseResult.Fail("run: --env requires NAME=VALUE");
                environment[name] = value;
                continue;
            }

            if (!afterSeparator && token == "--cwd")
            {
                if (++i >= tail.Count || string.IsNullOrWhiteSpace(tail[i]))
                    return ParseResult.Fail("run: --cwd requires a directory");
                targetCwd = tail[i];
                continue;
            }

            if (!afterSeparator && token == "--wait")
            {
                wait = true;
                continue;
            }

            if (!afterSeparator && token == "--timeout")
            {
                if (
                    ++i >= tail.Count
                    || !int.TryParse(tail[i], out var seconds)
                    || seconds is < 1 or > 3600
                )
                    return ParseResult.Fail("run: --timeout requires 1-3600 seconds");
                waitTimeoutSeconds = seconds;
                continue;
            }

            args.Add(token);
        }

        if (waitTimeoutSeconds.HasValue && !wait)
            return ParseResult.Fail("run: --timeout requires --wait");
        return ParseResult.Ok(
            new RunCommand(cwd, justMyCode, args, environment, targetCwd, wait, waitTimeoutSeconds)
        );
    }

    private static bool TryParseEnvironmentAssignment(
        string assignment,
        out string name,
        out string value
    )
    {
        var equals = assignment.IndexOf('=');
        name = equals > 0 ? assignment[..equals] : string.Empty;
        value = equals >= 0 ? assignment[(equals + 1)..] : string.Empty;
        return !string.IsNullOrWhiteSpace(name) && equals > 0 && !name.Contains('\0');
    }

    internal static ParseResult ParseAttach(IReadOnlyList<string> tail, string cwd)
    {
        var (justMyCode, args) = ParseJustMyCodeAndRemainingArgs(tail);
        if (args.Count == 0)
            return ParseResult.Fail("attach: missing target pid or process name");

        if (args.Count > 1)
            return ParseResult.Fail("attach: takes exactly one target pid or process name");

        return ParseResult.Ok(new AttachCommand(cwd, justMyCode, args[0]));
    }

    private static (bool JustMyCode, List<string> Args) ParseJustMyCodeAndRemainingArgs(
        IReadOnlyList<string> tail
    )
    {
        var justMyCode = true;
        var args = new List<string>();
        var stoppedOptions = false;

        foreach (var a in tail)
        {
            if (!stoppedOptions && a == "--")
            {
                stoppedOptions = true;
                continue;
            }

            if (
                !stoppedOptions
                && (
                    a.Equals("--no-jmc", StringComparison.OrdinalIgnoreCase)
                    || a.Equals("--no-just-my-code", StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                justMyCode = false;
                continue;
            }

            args.Add(a);
        }

        return (justMyCode, args);
    }
}
