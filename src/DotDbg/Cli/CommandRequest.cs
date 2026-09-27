using System.Text.Json.Nodes;

namespace DotDbg.Cli;

/// <summary>
/// A parsed, validated command. The engine still receives a JSON object as the IPC boundary,
/// but the parser no longer uses JsonObject as its internal data model.
/// </summary>
public abstract record CommandRequest(string Op, string Cwd)
{
    public abstract JsonObject ToJson();
}

public sealed record FileCommand(
    string Cwd,
    string Path,
    string? Configuration = null,
    IReadOnlyList<string>? Properties = null
) : CommandRequest("file", Cwd)
{
    public override JsonObject ToJson()
    {
        var json = new JsonObject
        {
            ["op"] = Op,
            ["cwd"] = Cwd,
            ["path"] = Path,
        };
        if (!string.IsNullOrWhiteSpace(Configuration))
            json["configuration"] = Configuration;
        if (Properties is { Count: > 0 })
            json["properties"] = new JsonArray(Properties.Select(p => (JsonNode?)p).ToArray());
        return json;
    }
}

public sealed record RunCommand(
    string Cwd,
    bool JustMyCode,
    IReadOnlyList<string> Args,
    IReadOnlyDictionary<string, string>? Environment = null,
    string? TargetWorkingDirectory = null,
    bool Wait = false,
    int? WaitTimeoutSeconds = null
) : CommandRequest("run", Cwd)
{
    public override JsonObject ToJson() =>
        new()
        {
            ["op"] = Op,
            ["cwd"] = Cwd,
            ["justMyCode"] = JustMyCode,
            ["targetArgs"] = new JsonArray(Args.Select(a => (JsonNode?)a).ToArray()),
            ["env"] = Environment is null
                ? null
                : new JsonObject(
                    Environment.Select(pair => new KeyValuePair<string, JsonNode?>(
                        pair.Key,
                        JsonValue.Create(pair.Value)
                    ))
                ),
            ["targetCwd"] = TargetWorkingDirectory,
            ["wait"] = Wait,
            ["timeoutSeconds"] = WaitTimeoutSeconds,
        };
}

public sealed record AttachCommand(string Cwd, bool JustMyCode, string Target)
    : CommandRequest("attach", Cwd)
{
    public override JsonObject ToJson() =>
        new()
        {
            ["op"] = Op,
            ["cwd"] = Cwd,
            ["justMyCode"] = JustMyCode,
            ["attachTarget"] = Target,
        };
}

public sealed record DetachCommand(string Cwd) : CommandRequest("detach", Cwd)
{
    public override JsonObject ToJson() => new() { ["op"] = Op, ["cwd"] = Cwd };
}

public sealed record BreakCommand(
    string Cwd,
    string Location,
    string? Name,
    string? Condition,
    string? ModuleName = null,
    bool IlMode = false
) : CommandRequest("break", Cwd)
{
    public override JsonObject ToJson() =>
        new()
        {
            ["op"] = Op,
            ["cwd"] = Cwd,
            ["location"] = Location,
            ["name"] = Name,
            ["condition"] = Condition,
            ["module"] = ModuleName,
            ["ilMode"] = IlMode,
        };
}

public sealed record TraceCommand(string Cwd, string Location, string Expression, string? Name)
    : CommandRequest("trace", Cwd)
{
    public override JsonObject ToJson() =>
        new()
        {
            ["op"] = Op,
            ["cwd"] = Cwd,
            ["location"] = Location,
            ["expression"] = Expression,
            ["name"] = Name,
        };
}

public sealed record DeleteCommand(string Cwd, int BreakpointId) : CommandRequest("delete", Cwd)
{
    public override JsonObject ToJson() =>
        new()
        {
            ["op"] = Op,
            ["cwd"] = Cwd,
            ["breakpointId"] = BreakpointId,
        };
}

public sealed record EnableCommand(string Cwd, int BreakpointId) : CommandRequest("enable", Cwd)
{
    public override JsonObject ToJson() =>
        new()
        {
            ["op"] = Op,
            ["cwd"] = Cwd,
            ["breakpointId"] = BreakpointId,
        };
}

public sealed record DisableCommand(string Cwd, int BreakpointId) : CommandRequest("disable", Cwd)
{
    public override JsonObject ToJson() =>
        new()
        {
            ["op"] = Op,
            ["cwd"] = Cwd,
            ["breakpointId"] = BreakpointId,
        };
}

public sealed record ConditionCommand(string Cwd, int BreakpointId, string? Condition)
    : CommandRequest("condition", Cwd)
{
    public override JsonObject ToJson() =>
        new()
        {
            ["op"] = Op,
            ["cwd"] = Cwd,
            ["breakpointId"] = BreakpointId,
            ["condition"] = Condition,
        };
}

public sealed record IgnoreCommand(string Cwd, int BreakpointId, int Count)
    : CommandRequest("ignore", Cwd)
{
    public override JsonObject ToJson() =>
        new()
        {
            ["op"] = Op,
            ["cwd"] = Cwd,
            ["breakpointId"] = BreakpointId,
            ["count"] = Count,
        };
}

public sealed record ContinueCommand(
    string Cwd,
    bool Wait = false,
    int? WaitTimeoutSeconds = null,
    int? TargetBreakpointId = null
) : CommandRequest("continue", Cwd)
{
    public override JsonObject ToJson() =>
        new()
        {
            ["op"] = Op,
            ["cwd"] = Cwd,
            ["wait"] = Wait,
            ["timeoutSeconds"] = WaitTimeoutSeconds,
            ["targetBreakpointId"] = TargetBreakpointId,
        };
}

public sealed record InterruptCommand(string Cwd) : CommandRequest("interrupt", Cwd)
{
    public override JsonObject ToJson() => new() { ["op"] = Op, ["cwd"] = Cwd };
}

public enum StepKind
{
    Next,
    In,
    Out,
}

public sealed record StepCommand(string Cwd, StepKind Kind)
    : CommandRequest(
        Kind switch
        {
            StepKind.Next => "next",
            StepKind.In => "step",
            StepKind.Out => "finish",
            _ => "noop",
        },
        Cwd
    )
{
    public override JsonObject ToJson() => new() { ["op"] = Op, ["cwd"] = Cwd };
}

public sealed record UntilCommand(string Cwd, string? Location) : CommandRequest("until", Cwd)
{
    public override JsonObject ToJson() =>
        new()
        {
            ["op"] = Op,
            ["cwd"] = Cwd,
            ["location"] = Location,
        };
}

public sealed record BacktraceCommand(string Cwd, bool All, int? ThreadId)
    : CommandRequest("backtrace", Cwd)
{
    public override JsonObject ToJson() =>
        new()
        {
            ["op"] = Op,
            ["cwd"] = Cwd,
            ["all"] = All,
            ["threadId"] = ThreadId,
        };
}

public sealed record FrameCommand(string Cwd, int? Index) : CommandRequest("frame", Cwd)
{
    public override JsonObject ToJson() =>
        new()
        {
            ["op"] = Op,
            ["cwd"] = Cwd,
            ["index"] = Index,
        };
}

public sealed record UpCommand(string Cwd) : CommandRequest("up", Cwd)
{
    public override JsonObject ToJson() => new() { ["op"] = Op, ["cwd"] = Cwd };
}

public sealed record DownCommand(string Cwd) : CommandRequest("down", Cwd)
{
    public override JsonObject ToJson() => new() { ["op"] = Op, ["cwd"] = Cwd };
}

public sealed record ThreadCommand(string Cwd, int? ThreadId) : CommandRequest("thread", Cwd)
{
    public override JsonObject ToJson() =>
        new()
        {
            ["op"] = Op,
            ["cwd"] = Cwd,
            ["threadId"] = ThreadId,
        };
}

public sealed record ListCommand(string Cwd, string? Location, int LineCount = 5)
    : CommandRequest("list", Cwd)
{
    public override JsonObject ToJson() =>
        new()
        {
            ["op"] = Op,
            ["cwd"] = Cwd,
            ["location"] = Location,
            ["lineCount"] = LineCount,
        };
}

public sealed record DecompileCommand(string Cwd, string? Target, bool IlMode = false)
    : CommandRequest("decompile", Cwd)
{
    public override JsonObject ToJson() =>
        new()
        {
            ["op"] = Op,
            ["cwd"] = Cwd,
            ["target"] = Target,
            ["ilMode"] = IlMode,
        };
}

public sealed record PrintCommand(string Cwd, string Expression) : CommandRequest("print", Cwd)
{
    public override JsonObject ToJson() =>
        new()
        {
            ["op"] = Op,
            ["cwd"] = Cwd,
            ["expression"] = Expression,
        };
}

public sealed record WatchCommand(string Cwd, string Expression) : CommandRequest("watch", Cwd)
{
    public override JsonObject ToJson() =>
        new()
        {
            ["op"] = Op,
            ["cwd"] = Cwd,
            ["expression"] = Expression,
        };
}

public sealed record UnwatchCommand(string Cwd, int WatchId) : CommandRequest("unwatch", Cwd)
{
    public override JsonObject ToJson() =>
        new()
        {
            ["op"] = Op,
            ["cwd"] = Cwd,
            ["watchId"] = WatchId,
        };
}

public sealed record WatchesCommand(string Cwd) : CommandRequest("watches", Cwd)
{
    public override JsonObject ToJson() => new() { ["op"] = Op, ["cwd"] = Cwd };
}

public sealed record TraceLogCommand(string Cwd, string? FilePath, bool Clear)
    : CommandRequest("trace-log", Cwd)
{
    public override JsonObject ToJson() =>
        new()
        {
            ["op"] = Op,
            ["cwd"] = Cwd,
            ["file"] = FilePath,
            ["clear"] = Clear,
        };
}

public sealed record CatchCommand(string Cwd, string? Mode) : CommandRequest("catch", Cwd)
{
    public override JsonObject ToJson() =>
        new()
        {
            ["op"] = Op,
            ["cwd"] = Cwd,
            ["mode"] = Mode,
        };
}

public sealed record SchemaCommand(string Cwd) : CommandRequest("schema", Cwd)
{
    public override JsonObject ToJson() => new() { ["op"] = Op, ["cwd"] = Cwd };
}

public sealed record HelpCommand(string Cwd, string? Subject) : CommandRequest("help", Cwd)
{
    public override JsonObject ToJson() =>
        new()
        {
            ["op"] = Op,
            ["cwd"] = Cwd,
            ["subject"] = Subject ?? string.Empty,
        };
}

public sealed record InfoCommand(string Cwd, string Subject, long? Since = null, int Limit = 100)
    : CommandRequest("info", Cwd)
{
    public override JsonObject ToJson() =>
        new()
        {
            ["op"] = Op,
            ["cwd"] = Cwd,
            ["subject"] = Subject,
            ["since"] = Since,
            ["limit"] = Limit,
        };
}

public sealed record ContextCommand(string Cwd) : CommandRequest("context", Cwd)
{
    public override JsonObject ToJson() => new() { ["op"] = Op, ["cwd"] = Cwd };
}

public sealed record EventsCommand(string Cwd, long? Since, int Limit, string? Kind)
    : CommandRequest("events", Cwd)
{
    public override JsonObject ToJson() =>
        new()
        {
            ["op"] = Op,
            ["cwd"] = Cwd,
            ["since"] = Since,
            ["limit"] = Limit,
            ["kind"] = Kind,
        };
}

public sealed record OutputCommand(string Cwd, long? Since, int Limit, string? Channel)
    : CommandRequest("output", Cwd)
{
    public override JsonObject ToJson() =>
        new()
        {
            ["op"] = Op,
            ["cwd"] = Cwd,
            ["since"] = Since,
            ["limit"] = Limit,
            ["channel"] = Channel,
        };
}

public sealed record WaitCommand(string Cwd, int? TimeoutSeconds = null)
    : CommandRequest("wait", Cwd)
{
    public override JsonObject ToJson() =>
        new()
        {
            ["op"] = Op,
            ["cwd"] = Cwd,
            ["timeoutSeconds"] = TimeoutSeconds,
        };
}

public sealed record SourceCommand(
    string Cwd,
    bool Inline,
    string Script,
    bool LastOnly = false,
    int TailCount = 0
) : CommandRequest("source", Cwd)
{
    public override JsonObject ToJson() =>
        new()
        {
            ["op"] = Op,
            ["cwd"] = Cwd,
            ["inline"] = Inline,
            ["script"] = Script,
            ["lastOnly"] = LastOnly,
            ["tailCount"] = TailCount,
        };
}

public sealed record QuitCommand(string Cwd) : CommandRequest("quit", Cwd)
{
    public override JsonObject ToJson() => new() { ["op"] = Op, ["cwd"] = Cwd };
}

public sealed record KillCommand(string Cwd) : CommandRequest("kill", Cwd)
{
    public override JsonObject ToJson() => new() { ["op"] = Op, ["cwd"] = Cwd };
}

public sealed record BreakpointsCommand(string Cwd) : CommandRequest("breakpoints", Cwd)
{
    public override JsonObject ToJson() => new() { ["op"] = Op, ["cwd"] = Cwd };
}
