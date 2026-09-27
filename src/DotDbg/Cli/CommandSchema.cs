using System.Text.Json.Nodes;

namespace DotDbg.Cli;

/// <summary>
/// Provides a machine-readable schema of the dotdbg command surface.
/// </summary>
public static class CommandSchema
{
    private sealed record ArgumentSchema(
        string Name,
        string Type,
        bool Required,
        string Description
    );

    private sealed record CommandSchemaInfo(
        string Name,
        string Description,
        string Usage,
        IReadOnlyList<ArgumentSchema> Arguments
    );

    private static readonly IReadOnlyDictionary<string, CommandSchemaInfo> Commands =
        new Dictionary<string, CommandSchemaInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["file"] = new(
                "file",
                "Load executable, DLL, .csproj, or file-based app (.cs) to debug",
                "file <path> [-c Configuration] [-p Name=Value]...",
                new[]
                {
                    new ArgumentSchema(
                        "path",
                        "string",
                        true,
                        "Path to a .dll, .exe, Unix executable, .csproj, or .cs file-based app"
                    ),
                    new ArgumentSchema(
                        "configuration",
                        "string",
                        false,
                        "Build configuration for .csproj/.cs (e.g. Debug, Release)"
                    ),
                    new ArgumentSchema(
                        "properties",
                        "array",
                        false,
                        "MSBuild properties Name=Value for .csproj/.cs builds"
                    ),
                }
            ),
            ["run"] = new(
                "run",
                "Start the debugged program",
                "run [--no-jmc] [--env NAME=VALUE]... [--cwd PATH] [--wait [--timeout SECONDS]] [--] [args...]",
                new[]
                {
                    new ArgumentSchema("no-jmc", "boolean", false, "Disable Just My Code"),
                    new ArgumentSchema("env", "object", false, "Target environment overrides"),
                    new ArgumentSchema("cwd", "string", false, "Target working directory"),
                    new ArgumentSchema(
                        "wait",
                        "boolean",
                        false,
                        "Return the first stop or exit (default timeout: 30 seconds)"
                    ),
                    new ArgumentSchema(
                        "timeout",
                        "integer",
                        false,
                        "Wait timeout in seconds (requires --wait)"
                    ),
                    new ArgumentSchema("args", "array", false, "Arguments passed to the target"),
                }
            ),
            ["attach"] = new(
                "attach",
                "Attach to a running process",
                "attach [--no-jmc] <pid|name>",
                new[]
                {
                    new ArgumentSchema("no-jmc", "boolean", false, "Disable Just My Code"),
                    new ArgumentSchema("target", "string", true, "Process id or process name"),
                }
            ),
            ["detach"] = new(
                "detach",
                "Detach from the debugged process",
                "detach",
                Array.Empty<ArgumentSchema>()
            ),
            ["break"] = new(
                "break",
                "Set a breakpoint",
                "break [--il] <location> [--name <name>] [if <condition>]",
                new[]
                {
                    new ArgumentSchema(
                        "location",
                        "string",
                        true,
                        "file:line or Type.Method:IL_0000"
                    ),
                    new ArgumentSchema("il", "boolean", false, "Set an IL-level breakpoint"),
                    new ArgumentSchema("name", "string", false, "Optional breakpoint label"),
                    new ArgumentSchema(
                        "condition",
                        "string",
                        false,
                        "Optional condition; stop only when true"
                    ),
                }
            ),
            ["trace"] = new(
                "trace",
                "Log an expression when execution reaches a location",
                "trace <location> <expression> [--name <name>]",
                new[]
                {
                    new ArgumentSchema("location", "string", true, "file:line or file:line:column"),
                    new ArgumentSchema("expression", "string", true, "Expression to log"),
                    new ArgumentSchema("name", "string", false, "Optional tracepoint label"),
                }
            ),
            ["delete"] = new(
                "delete",
                "Delete a breakpoint",
                "delete <id>",
                new[] { new ArgumentSchema("breakpointId", "integer", true, "Breakpoint id") }
            ),
            ["enable"] = new(
                "enable",
                "Enable a breakpoint",
                "enable <id>",
                new[] { new ArgumentSchema("breakpointId", "integer", true, "Breakpoint id") }
            ),
            ["disable"] = new(
                "disable",
                "Disable a breakpoint",
                "disable <id>",
                new[] { new ArgumentSchema("breakpointId", "integer", true, "Breakpoint id") }
            ),
            ["condition"] = new(
                "condition",
                "Set or clear a breakpoint condition",
                "condition <id> [expression]",
                new[]
                {
                    new ArgumentSchema("breakpointId", "integer", true, "Breakpoint id"),
                    new ArgumentSchema(
                        "condition",
                        "string",
                        false,
                        "Optional condition; omit to clear"
                    ),
                }
            ),
            ["ignore"] = new(
                "ignore",
                "Ignore the first N hits of a breakpoint",
                "ignore <id> <count>",
                new[]
                {
                    new ArgumentSchema("breakpointId", "integer", true, "Breakpoint id"),
                    new ArgumentSchema("count", "integer", true, "Number of hits to ignore"),
                }
            ),
            ["continue"] = new(
                "continue",
                "Continue execution",
                "continue [--wait] [--to BREAKPOINT_ID] [--timeout SECONDS]",
                new[]
                {
                    new ArgumentSchema(
                        "wait",
                        "boolean",
                        false,
                        "Return the next stop, trace, or exit"
                    ),
                    new ArgumentSchema(
                        "timeout",
                        "integer",
                        false,
                        "Wait timeout in seconds (requires --wait or --to)"
                    ),
                    new ArgumentSchema(
                        "targetBreakpointId",
                        "integer",
                        false,
                        "Skip other stops until this breakpoint or process exit"
                    ),
                }
            ),
            ["interrupt"] = new(
                "interrupt",
                "Interrupt or pause execution",
                "interrupt",
                Array.Empty<ArgumentSchema>()
            ),
            ["breakpoints"] = new(
                "breakpoints",
                "List all breakpoints and tracepoints",
                "breakpoints",
                Array.Empty<ArgumentSchema>()
            ),
            ["next"] = new(
                "next",
                "Step over and wait for the stop",
                "next [--wait]",
                new[]
                {
                    new ArgumentSchema(
                        "wait",
                        "boolean",
                        false,
                        "Accepted for consistency; stepping already waits"
                    ),
                }
            ),
            ["step"] = new(
                "step",
                "Step into and wait for the stop",
                "step [--wait]",
                new[]
                {
                    new ArgumentSchema(
                        "wait",
                        "boolean",
                        false,
                        "Accepted for consistency; stepping already waits"
                    ),
                }
            ),
            ["finish"] = new(
                "finish",
                "Step out and wait for the stop",
                "finish [--wait]",
                new[]
                {
                    new ArgumentSchema(
                        "wait",
                        "boolean",
                        false,
                        "Accepted for consistency; stepping already waits"
                    ),
                }
            ),
            ["until"] = new(
                "until",
                "Run until current line or a location is reached",
                "until [location]",
                new[]
                {
                    new ArgumentSchema(
                        "location",
                        "string",
                        false,
                        "Optional file:line or file:line:column"
                    ),
                }
            ),
            ["backtrace"] = new(
                "backtrace",
                "Print the stack trace",
                "backtrace [all] [--thread <id>]",
                new[]
                {
                    new ArgumentSchema("all", "boolean", false, "Show all threads"),
                    new ArgumentSchema("threadId", "integer", false, "Thread to inspect"),
                }
            ),
            ["frame"] = new(
                "frame",
                "Select or display the current stack frame",
                "frame [index]",
                new[] { new ArgumentSchema("index", "integer", false, "Frame index") }
            ),
            ["up"] = new(
                "up",
                "Move to the next older stack frame",
                "up",
                Array.Empty<ArgumentSchema>()
            ),
            ["down"] = new(
                "down",
                "Move to the next newer stack frame",
                "down",
                Array.Empty<ArgumentSchema>()
            ),
            ["thread"] = new(
                "thread",
                "Select or display the current thread",
                "thread [id]",
                new[] { new ArgumentSchema("threadId", "integer", false, "Thread id") }
            ),
            ["list"] = new(
                "list",
                "List source lines",
                "list [file:line] [--lines N]",
                new[]
                {
                    new ArgumentSchema(
                        "location",
                        "string",
                        false,
                        "Optional file:line or file:line:column"
                    ),
                    new ArgumentSchema(
                        "lineCount",
                        "integer",
                        false,
                        "Source lines (1-100; default 5)"
                    ),
                }
            ),
            ["decompile"] = new(
                "decompile",
                "Decompile the current frame to C# or IL",
                "decompile [--il]",
                new[] { new ArgumentSchema("il", "boolean", false, "Output IL instead of C#") }
            ),
            ["print"] = new(
                "print",
                "Evaluate an expression",
                "print <expression>",
                new[] { new ArgumentSchema("expression", "string", true, "Expression to evaluate") }
            ),
            ["watch"] = new(
                "watch",
                "Add a watch expression",
                "watch <expression>",
                new[] { new ArgumentSchema("expression", "string", true, "Expression to watch") }
            ),
            ["unwatch"] = new(
                "unwatch",
                "Remove a watch expression",
                "unwatch <id>",
                new[] { new ArgumentSchema("watchId", "integer", true, "Watch id") }
            ),
            ["watches"] = new(
                "watches",
                "List watch expressions",
                "watches",
                Array.Empty<ArgumentSchema>()
            ),
            ["trace-log"] = new(
                "trace-log",
                "Set or clear the trace log file",
                "trace-log [file] [--clear]",
                new[]
                {
                    new ArgumentSchema("file", "string", false, "File path; omit to disable"),
                    new ArgumentSchema("clear", "boolean", false, "Clear the log file"),
                }
            ),
            ["catch"] = new(
                "catch",
                "Query or configure stops on thrown exceptions",
                "catch [user|all|unhandled|none]",
                new[]
                {
                    new ArgumentSchema(
                        "mode",
                        "string",
                        false,
                        "user stops at user-code throws (default); all stops on every throw; unhandled stops only before termination; none lets exceptions run"
                    ),
                }
            ),
            ["info"] = new(
                "info",
                "Show information about a subject",
                "info <subject> [--since LINE] [--limit N]",
                new[]
                {
                    new ArgumentSchema("subject", "string", true, "Subject to query"),
                    new ArgumentSchema(
                        "since",
                        "integer",
                        false,
                        "Zero-based trace line index; trace only"
                    ),
                    new ArgumentSchema(
                        "limit",
                        "integer",
                        false,
                        "Trace page size (1-200; default 100)"
                    ),
                }
            ),
            ["context"] = new(
                "context",
                "Show a compact session and stopped-frame snapshot",
                "context",
                Array.Empty<ArgumentSchema>()
            ),
            ["events"] = new(
                "events",
                "Read recent stops, tracepoint results, target output, and debugger diagnostics",
                "events [--since SEQ] [--limit N] [--kind stop|trace|output|diagnostic]",
                new[]
                {
                    new ArgumentSchema(
                        "since",
                        "integer",
                        false,
                        "Only events after this sequence"
                    ),
                    new ArgumentSchema(
                        "limit",
                        "integer",
                        false,
                        "Maximum events (1-200; default 50)"
                    ),
                    new ArgumentSchema(
                        "kind",
                        "string",
                        false,
                        "stop, trace, output, or diagnostic"
                    ),
                }
            ),
            ["output"] = new(
                "output",
                "Read target stdout, stderr, and Debug.WriteLine messages",
                "output [--since SEQ] [--limit N] [--channel stdout|stderr|debug]",
                new[]
                {
                    new ArgumentSchema(
                        "since",
                        "integer",
                        false,
                        "Only events after this sequence"
                    ),
                    new ArgumentSchema(
                        "limit",
                        "integer",
                        false,
                        "Maximum events (1-200; default 50)"
                    ),
                    new ArgumentSchema("channel", "string", false, "stdout, stderr, or debug"),
                }
            ),
            ["wait"] = new(
                "wait",
                "Wait until the debuggee stops",
                "wait [--timeout <seconds>]",
                new[]
                {
                    new ArgumentSchema(
                        "timeoutSeconds",
                        "integer",
                        false,
                        "Maximum wait time in seconds (1-3600); omitted means no timeout"
                    ),
                }
            ),
            ["source"] = new(
                "source",
                "Read or execute a command script",
                "source [--last|--tail N] <file> | source [--last|--tail N] -c <commands>",
                new[]
                {
                    new ArgumentSchema("file", "string", false, "Script file path"),
                    new ArgumentSchema("command", "string", false, "Inline script commands"),
                    new ArgumentSchema(
                        "lastOnly",
                        "boolean",
                        false,
                        "Return the final response with up to 50 prints; failure history is bounded"
                    ),
                    new ArgumentSchema(
                        "tailCount",
                        "integer",
                        false,
                        "Return the last 1-20 command responses in data.responses (bounded by size)"
                    ),
                }
            ),
            ["help"] = new(
                "help",
                "Show help for a command",
                "help [command]",
                new[] { new ArgumentSchema("subject", "string", false, "Command to describe") }
            ),
            ["quit"] = new("quit", "Quit the debugger", "quit", Array.Empty<ArgumentSchema>()),
            ["kill"] = new(
                "kill",
                "Kill the target process",
                "kill",
                Array.Empty<ArgumentSchema>()
            ),
            ["schema"] = new(
                "schema",
                "Print the machine-readable command schema",
                "schema",
                Array.Empty<ArgumentSchema>()
            ),
        };

    private static readonly IReadOnlyDictionary<string, string> InfoSubjects = new Dictionary<
        string,
        string
    >(StringComparer.OrdinalIgnoreCase)
    {
        ["breakpoints"] = "List breakpoints",
        ["files"] = "Show source files in the target",
        ["modules"] = "Show loaded modules",
        ["exception"] = "Show exception information",
        ["status"] = "Show debugger status",
        ["locals"] = "Show local variables",
        ["args"] = "Show method arguments",
        ["threads"] = "Show threads",
        ["trace"] = "Show trace output",
    };

    public static JsonObject GetSchema()
    {
        var commands = new JsonArray();
        foreach (var (name, info) in Commands)
        {
            var aliases = new JsonArray { name };
            foreach (
                var alias in CommandParser
                    .CommandAliases.Where(kvp =>
                        string.Equals(kvp.Value, name, StringComparison.OrdinalIgnoreCase)
                    )
                    .Select(kvp => kvp.Key)
                    .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
            )
            {
                aliases.Add(alias);
            }

            var args = new JsonArray();
            foreach (var arg in info.Arguments)
            {
                args.Add(
                    new JsonObject
                    {
                        ["name"] = arg.Name,
                        ["type"] = arg.Type,
                        ["required"] = arg.Required,
                        ["description"] = arg.Description,
                    }
                );
            }

            commands.Add(
                new JsonObject
                {
                    ["name"] = info.Name,
                    ["aliases"] = aliases,
                    ["description"] = info.Description,
                    ["usage"] = info.Usage,
                    ["arguments"] = args,
                }
            );
        }

        var infoSubjects = new JsonArray();
        foreach (var (name, description) in InfoSubjects)
        {
            var aliases = new JsonArray { name };
            foreach (
                var alias in CommandParser
                    .InfoSubjectAliases.Where(kvp =>
                        string.Equals(kvp.Value, name, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase)
                    )
                    .Select(kvp => kvp.Key)
                    .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
            )
            {
                aliases.Add(alias);
            }

            infoSubjects.Add(
                new JsonObject
                {
                    ["name"] = name,
                    ["aliases"] = aliases,
                    ["description"] = description,
                }
            );
        }

        return new JsonObject
        {
            ["version"] = "1.0",
            ["commands"] = commands,
            ["infoSubjects"] = infoSubjects,
        };
    }
}
