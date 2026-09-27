using System.Text.Json.Nodes;

namespace DotDbg.Engine;

internal sealed class UserBreakpoint
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int Line { get; set; }
    public int? Column { get; set; }
    public bool IsIlBreakpoint { get; set; }
    public string? IlMethod { get; set; }
    public int? IlOffset { get; set; }
    public string? IlModule { get; set; }
    public string? Condition { get; set; }
    public string? HitCondition { get; set; }
    public string? LogExpression { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Verified { get; set; }
    public string? VerifiedMessage { get; set; }
    public int? BoundLine { get; set; }
    public int? BoundColumn { get; set; }
    public int InternalId { get; set; }
    public bool IsTemporary { get; set; }
}

internal sealed class WatchEntry
{
    public int Id { get; set; }
    public string Expression { get; set; } = string.Empty;
    public string? LastValue { get; set; }
    public string? LastType { get; set; }
}

internal sealed class StopInfo
{
    public int ThreadId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int? ExitCode { get; set; }
    public bool UnhandledException { get; set; }
    public string? FilePath { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public string Description { get; set; } = string.Empty;
    public int FrameId { get; set; }
    public int Index { get; set; }
    public JsonObject? Watches { get; set; }
    public JsonArray Breakpoints { get; set; } = new();
    public List<string> TraceExpressions { get; set; } = new();
    public JsonArray TraceOutput { get; set; } = new();

    public JsonObject ToJson()
    {
        var data = new JsonObject
        {
            ["threadId"] = ThreadId,
            ["reason"] = Reason,
            ["filePath"] = FilePath,
            ["line"] = Line,
            ["column"] = Column,
            ["description"] = Description,
            ["frameId"] = FrameId,
            ["index"] = Index,
            ["watches"] = Watches?.DeepClone(),
            ["trace"] = TraceOutput.DeepClone(),
        };
        if (Reason == "exited")
        {
            data["exitCode"] = ExitCode;
            data["unhandledException"] = UnhandledException;
        }
        else if (Reason == "exception")
            data["unhandledException"] = UnhandledException;
        if (Reason is "breakpoint" or "trace")
        {
            data["breakpoints"] = Breakpoints.DeepClone();
            if (Breakpoints.Count == 1)
                data["breakpointId"] = Breakpoints[0]?["id"]?.GetValue<int>();
        }
        return data;
    }
}
