using System.Text.Json.Nodes;

namespace DotDbg.Engine;

internal sealed class SessionEventBuffer(int capacity = 1024)
{
    private readonly Lock _lock = new();
    private readonly Queue<SessionEvent> _entries = new();
    private long _sequence;

    public long LatestSequence
    {
        get
        {
            lock (_lock)
                return _sequence;
        }
    }

    public void AddOutput(string channel, string text) => Add("output", text, channel);

    public void AddDiagnostic(string text) => Add("diagnostic", text);

    public void AddStop(
        string reason,
        string? filePath,
        int line,
        int threadId,
        int? exitCode = null,
        bool unhandledException = false,
        JsonArray? breakpoints = null
    ) =>
        Add(
            "stop",
            DescribeLocation(reason, filePath, line),
            reason: reason,
            filePath: filePath,
            line: line,
            threadId: threadId,
            exitCode: exitCode,
            unhandledException: unhandledException,
            breakpoints: breakpoints
        );

    public void AddTrace(string text, string? filePath, int line, int threadId) =>
        Add("trace", text, filePath: filePath, line: line, threadId: threadId);

    private void Add(
        string kind,
        string text,
        string? channel = null,
        string? reason = null,
        string? filePath = null,
        int line = 0,
        int threadId = 0,
        int? exitCode = null,
        bool unhandledException = false,
        JsonArray? breakpoints = null
    )
    {
        lock (_lock)
        {
            var entry = new SessionEvent(
                ++_sequence,
                DateTimeOffset.UtcNow,
                kind,
                channel,
                text.Length > 2048 ? text[..2048] + "…" : text,
                reason,
                filePath,
                line,
                threadId,
                exitCode,
                unhandledException,
                breakpoints is null ? null : (JsonArray)breakpoints.DeepClone()
            );
            _entries.Enqueue(entry);
            if (_entries.Count > capacity)
                _entries.Dequeue();
        }
    }

    public JsonObject Read(long? since, int limit, string? kind, string? channel)
    {
        lock (_lock)
        {
            var matching = _entries
                .Where(entry =>
                    (!since.HasValue || entry.Sequence > since.Value)
                    && (kind is null || entry.Kind == kind)
                    && (channel is null || entry.Channel == channel)
                )
                .ToList();
            if (!since.HasValue && matching.Count > limit)
                matching = matching.TakeLast(limit).ToList();

            var selected = matching.Take(limit).ToList();
            var hasMore = since.HasValue && matching.Count > limit;
            var nextSequence = hasMore ? selected[^1].Sequence : _sequence;
            var firstAvailable = _entries.Count > 0 ? _entries.Peek().Sequence : _sequence + 1;
            var events = new JsonArray();
            foreach (var entry in selected)
            {
                var item = new JsonObject
                {
                    ["seq"] = entry.Sequence,
                    ["time"] = entry.Time.ToString("O"),
                    ["kind"] = entry.Kind,
                    ["text"] = entry.Text,
                };
                if (entry.Channel is not null)
                    item["channel"] = entry.Channel;
                if (entry.Reason is not null)
                    item["reason"] = entry.Reason;
                if (entry.FilePath is not null)
                    item["filePath"] = entry.FilePath;
                if (entry.Line > 0)
                    item["line"] = entry.Line;
                if (entry.ThreadId > 0)
                    item["threadId"] = entry.ThreadId;
                if (entry.ExitCode.HasValue)
                    item["exitCode"] = entry.ExitCode.Value;
                if (entry.UnhandledException || entry.Reason == "exception")
                    item["unhandledException"] = entry.UnhandledException;
                if (entry.Breakpoints is not null)
                {
                    item["breakpoints"] = entry.Breakpoints.DeepClone();
                    if (entry.Breakpoints.Count == 1)
                        item["breakpointId"] = entry.Breakpoints[0]?["id"]?.GetValue<int>();
                }
                events.Add(item);
            }

            return new JsonObject
            {
                ["events"] = events,
                ["nextSeq"] = nextSequence,
                ["hasMore"] = hasMore,
                ["truncated"] = since.HasValue && since.Value < firstAvailable - 1,
            };
        }
    }

    private static string DescribeLocation(string reason, string? filePath, int line) =>
        string.IsNullOrWhiteSpace(filePath) || line < 1 ? reason : $"{reason} {filePath}:{line}";

    private sealed record SessionEvent(
        long Sequence,
        DateTimeOffset Time,
        string Kind,
        string? Channel,
        string Text,
        string? Reason,
        string? FilePath,
        int Line,
        int ThreadId,
        int? ExitCode,
        bool UnhandledException,
        JsonArray? Breakpoints
    );
}
