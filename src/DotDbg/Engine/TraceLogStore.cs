using System.Text;
using System.Text.Json.Nodes;
using static DotDbg.Engine.OperationResponse;

namespace DotDbg.Engine;

internal sealed class TraceLogStore : IDisposable
{
    private const int MaxReturnedLineLength = 4096;
    private string? _traceLogFilePath;
    private StreamWriter? _traceLogWriter;
    private readonly Lock _traceLogLock = new();

    internal JsonObject Configure(JsonObject request, Func<string, string> resolvePath)
    {
        var clear = request["clear"]?.GetValue<bool>() ?? false;
        var filePath = request["file"]?.GetValue<string>();
        var fullPath = string.IsNullOrWhiteSpace(filePath) ? null : resolvePath(filePath);
        lock (_traceLogLock)
        {
            var destination = fullPath ?? (clear ? _traceLogFilePath : null);
            if (destination is null)
            {
                if (clear)
                    return ResponseOk("Trace log cleared (no file set)");
                var previousWriter = _traceLogWriter;
                _traceLogWriter = null;
                _traceLogFilePath = null;
                try
                {
                    previousWriter?.Dispose();
                }
                catch (Exception ex)
                {
                    return ResponseError(
                        $"Trace log disabled, but closing it failed: {ex.Message}"
                    );
                }
                return ResponseOk("Trace log disabled");
            }

            var sameFile = string.Equals(
                destination,
                _traceLogFilePath,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal
            );
            if (sameFile && !clear && _traceLogWriter is not null)
                return ResponseOk($"Trace log set to {destination}");

            if (sameFile)
            {
                try
                {
                    _traceLogWriter?.Dispose();
                }
                catch (Exception ex)
                {
                    _traceLogWriter = null;
                    _traceLogFilePath = null;
                    return ResponseError($"Failed to close trace log {destination}: {ex.Message}");
                }
                _traceLogWriter = null;
            }

            StreamWriter writer;
            try
            {
                writer = OpenWriter(destination, clear);
            }
            catch (Exception ex)
            {
                if (sameFile)
                    _traceLogFilePath = null;
                return ResponseError($"Failed to open trace log {destination}: {ex.Message}");
            }

            var previous = _traceLogWriter;
            _traceLogWriter = writer;
            _traceLogFilePath = destination;
            try
            {
                previous?.Dispose();
            }
            catch (Exception ex)
            {
                return ResponseOk(
                    $"Trace log set to {destination}; previous log close failed: {ex.Message}"
                );
            }
            return ResponseOk(
                clear ? $"Cleared trace log {destination}" : $"Trace log set to {destination}"
            );
        }
    }

    internal string? Append(IEnumerable<string> lines)
    {
        lock (_traceLogLock)
        {
            if (_traceLogWriter is null)
                return null;

            try
            {
                foreach (var line in lines)
                    _traceLogWriter.WriteLine(line);
                return null;
            }
            catch (Exception ex)
            {
                var failedPath = _traceLogFilePath;
                try
                {
                    _traceLogWriter.Dispose();
                }
                catch
                { /* The sink has already failed. */
                }
                _traceLogWriter = null;
                _traceLogFilePath = null;
                return $"Trace log disabled after write failure at {failedPath}: {ex.Message}";
            }
        }
    }

    private static StreamWriter OpenWriter(string path, bool clear)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var stream = new FileStream(
            path,
            clear ? FileMode.Create : FileMode.Append,
            FileAccess.Write,
            FileShare.Read
        );
        return new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
    }

    internal JsonObject Read(long? since = null, int limit = 100)
    {
        if (since is < 0 || limit is < 1 or > 200)
            return ResponseError(
                "info trace: --since must be nonnegative and --limit must be 1-200"
            );

        var lines = new JsonArray();
        string? filePath;
        long totalLines = 0;
        long startLine = since ?? 0;
        var truncatedLines = 0;

        lock (_traceLogLock)
        {
            filePath = _traceLogFilePath;
            try
            {
                _traceLogWriter?.Flush();
            }
            catch (Exception ex)
            {
                return ResponseError($"Failed to flush trace log: {ex.Message}");
            }
        }

        if (filePath is not null && File.Exists(filePath))
        {
            try
            {
                using var fs = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite
                );
                using var reader = new StreamReader(fs, Encoding.UTF8);
                var tail = since is null ? new Queue<(string Text, bool Truncated)>() : null;
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    if (since is null)
                    {
                        var truncated = line.Length > MaxReturnedLineLength;
                        tail!.Enqueue(
                            (truncated ? line[..MaxReturnedLineLength] : line, truncated)
                        );
                        if (tail.Count > limit)
                            tail.Dequeue();
                    }
                    else if (totalLines >= since && lines.Count < limit)
                    {
                        if (line.Length > MaxReturnedLineLength)
                        {
                            line = line[..MaxReturnedLineLength];
                            truncatedLines++;
                        }
                        lines.Add(line);
                    }
                    totalLines++;
                }
                if (tail is not null)
                {
                    startLine = totalLines - tail.Count;
                    foreach (var tailLine in tail)
                    {
                        lines.Add(tailLine.Text);
                        if (tailLine.Truncated)
                            truncatedLines++;
                    }
                }
            }
            catch (Exception ex)
            {
                return ResponseError($"Failed to read trace log: {ex.Message}");
            }
        }

        startLine = Math.Min(startLine, totalLines);
        var nextLine = startLine + lines.Count;

        return ResponseOk(
            $"Trace log ({lines.Count} of {totalLines} lines)",
            new JsonObject
            {
                ["file"] = filePath ?? string.Empty,
                ["traceLog"] = lines,
                ["startLine"] = startLine,
                ["nextLine"] = nextLine,
                ["totalLines"] = totalLines,
                ["hasMore"] = nextLine < totalLines,
                ["truncatedLines"] = truncatedLines,
            }
        );
    }

    public void Dispose()
    {
        lock (_traceLogLock)
        {
            _traceLogWriter?.Dispose();
            _traceLogWriter = null;
        }
    }
}
