using DotDbg.Util;

namespace DotDbg.Cli;

internal static class BreakpointCommandParser
{
    internal static ParseResult ParseBreak(IReadOnlyList<string> tail, string cwd)
    {
        if (tail.Count == 0)
            return ParseResult.Fail("break: missing location");

        var ilMode = false;
        var args = new List<string>(tail);
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i].Equals("--il", StringComparison.OrdinalIgnoreCase))
            {
                ilMode = true;
                args.RemoveAt(i);
                i--;
            }
        }

        if (args.Count == 0)
            return ParseResult.Fail("break: missing location");

        string? name = null;
        string? condition = null;
        string? moduleName = null;

        var location = args[0];
        var ifIndex = location.IndexOf(" if ", StringComparison.OrdinalIgnoreCase);
        if (ifIndex >= 0)
        {
            // Support quoted form: "break \"Program.cs:26 if n > 1\""
            condition = location.Substring(ifIndex + 4).Trim();
            location = location.Substring(0, ifIndex).Trim();
        }

        if (string.IsNullOrWhiteSpace(location))
            return ParseResult.Fail("break: missing location");

        if (ilMode)
        {
            if (
                !TryParseIlLocation(
                    location,
                    out moduleName,
                    out var method,
                    out var ilOffset,
                    out var error
                )
            )
                return ParseResult.Fail($"break: {error}");

            location = BuildIlLocation(method!, ilOffset);
        }
        else
        {
            if (!CommandParser.ValidateLocation(location, out var error))
                return ParseResult.Fail($"break: {error}");
        }

        var conditionParts = new List<string>();
        var inCondition = condition is not null;
        for (var i = 1; i < args.Count; )
        {
            var arg = args[i];
            if (arg.Equals("--name", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Count)
                    return ParseResult.Fail("break: --name requires a value");
                if (name is not null)
                    return ParseResult.Fail("break: --name may be specified only once");
                name = args[i + 1];
                i += 2;
                continue;
            }

            if (arg.Equals("if", StringComparison.OrdinalIgnoreCase) && !inCondition)
            {
                inCondition = true;
                i++;
                continue;
            }

            if (inCondition)
            {
                conditionParts.Add(arg);
                i++;
                continue;
            }
            return ParseResult.Fail($"break: unknown argument '{arg}'");
        }

        if (conditionParts.Count > 0)
            condition = string.Join(' ', conditionParts);
        if (inCondition && string.IsNullOrWhiteSpace(condition))
            return ParseResult.Fail("break: missing condition after if");

        return ParseResult.Ok(new BreakCommand(cwd, location, name, condition, moduleName, ilMode));
    }

    private static string BuildIlLocation(string method, int ilOffset) =>
        $"{method}:IL_{ilOffset:X4}";

    private static bool TryParseIlLocation(
        string location,
        out string? moduleName,
        out string? method,
        out int ilOffset,
        out string? error
    )
    {
        moduleName = null;
        method = null;
        ilOffset = 0;
        error = null;

        if (string.IsNullOrWhiteSpace(location))
        {
            error = "missing IL location";
            return false;
        }

        var rest = location;
        var bangIndex = rest.IndexOf('!');
        if (bangIndex >= 0)
        {
            moduleName = rest.Substring(0, bangIndex).Trim();
            rest = rest.Substring(bangIndex + 1).Trim();
        }

        var colonIndex = rest.LastIndexOf(':');
        if (colonIndex <= 0)
        {
            error = "invalid IL location. Use Type.Method:IL_0000 or Type.Method:0x1f";
            return false;
        }

        var methodSpec = rest.Substring(0, colonIndex).Trim();
        var offsetSpec = rest.Substring(colonIndex + 1).Trim();

        if (string.IsNullOrWhiteSpace(methodSpec) || string.IsNullOrWhiteSpace(offsetSpec))
        {
            error = "invalid IL location. Use Type.Method:IL_0000 or Type.Method:0x1f";
            return false;
        }

        if (!TryParseIlOffset(offsetSpec, out ilOffset, out error))
            return false;

        if (!methodSpec.Contains('.'))
        {
            error = "invalid IL location. Use Type.Method:IL_0000 or Type.Method:0x1f";
            return false;
        }

        method = methodSpec;
        return true;
    }

    private static bool TryParseIlOffset(string offsetSpec, out int ilOffset, out string? error)
    {
        ilOffset = 0;
        error = null;

        if (IlOffsetParser.TryParse(offsetSpec, out ilOffset))
            return true;

        error = "invalid IL offset";
        return false;
    }

    internal static ParseResult ParseTrace(IReadOnlyList<string> tail, string cwd)
    {
        if (tail.Count == 0)
            return ParseResult.Fail("trace: missing location");

        var location = tail[0];
        if (string.IsNullOrWhiteSpace(location))
            return ParseResult.Fail("trace: missing location");

        if (!CommandParser.ValidateLocation(location, out var error))
            return ParseResult.Fail($"trace: {error}");

        if (tail.Count < 2)
            return ParseResult.Fail("trace: missing expression");

        string? name = null;
        string expression;
        var nameIndex = -1;
        for (var i = 1; i < tail.Count; i++)
        {
            if (tail[i].Equals("--name", StringComparison.OrdinalIgnoreCase))
            {
                nameIndex = i;
                break;
            }
        }

        if (nameIndex >= 0)
        {
            if (nameIndex == 1)
                return ParseResult.Fail("trace: missing expression before --name");
            if (nameIndex + 1 >= tail.Count)
                return ParseResult.Fail("trace: --name requires a value");
            if (nameIndex + 2 < tail.Count)
                return ParseResult.Fail("trace: unexpected arguments after --name value");

            expression = string.Join(' ', tail.Skip(1).Take(nameIndex - 1));
            name = tail[nameIndex + 1];
        }
        else
        {
            expression = string.Join(' ', tail.Skip(1));
        }

        return ParseResult.Ok(new TraceCommand(cwd, location, expression, name));
    }

    internal static ParseResult ParseDelete(IReadOnlyList<string> tail, string cwd)
    {
        if (!CommandParser.TryParseIntArg(tail, out var id))
            return ParseResult.Fail("delete: missing or invalid breakpoint id");

        return ParseResult.Ok(new DeleteCommand(cwd, id));
    }

    internal static ParseResult ParseEnable(IReadOnlyList<string> tail, string cwd)
    {
        if (!CommandParser.TryParseIntArg(tail, out var id))
            return ParseResult.Fail("enable: missing or invalid breakpoint id");

        return ParseResult.Ok(new EnableCommand(cwd, id));
    }

    internal static ParseResult ParseDisable(IReadOnlyList<string> tail, string cwd)
    {
        if (!CommandParser.TryParseIntArg(tail, out var id))
            return ParseResult.Fail("disable: missing or invalid breakpoint id");

        return ParseResult.Ok(new DisableCommand(cwd, id));
    }

    internal static ParseResult ParseCondition(IReadOnlyList<string> tail, string cwd)
    {
        if (tail.Count == 0)
            return ParseResult.Fail("condition: missing breakpoint id");

        if (!int.TryParse(tail[0], out var id) || id < 1)
            return ParseResult.Fail($"condition: invalid breakpoint id '{tail[0]}'");

        var condition = tail.Count > 1 ? string.Join(' ', tail.Skip(1)).Trim() : null;
        return ParseResult.Ok(new ConditionCommand(cwd, id, condition));
    }

    internal static ParseResult ParseIgnore(IReadOnlyList<string> tail, string cwd)
    {
        if (tail.Count < 1 || !int.TryParse(tail[0], out var id) || id < 1)
            return ParseResult.Fail("ignore: missing or invalid breakpoint id");

        if (tail.Count < 2 || !int.TryParse(tail[1], out var count) || count < 0)
            return ParseResult.Fail("ignore: missing or invalid count");

        return ParseResult.Ok(new IgnoreCommand(cwd, id, count));
    }
}
