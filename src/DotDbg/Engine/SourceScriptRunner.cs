using System.Text;
using System.Text.Json.Nodes;
using DotDbg.Cli;
using static DotDbg.Engine.OperationResponse;

namespace DotDbg.Engine;

internal static class SourceScriptRunner
{
    private sealed class RecentResponses
    {
        private const int MaxBytes = 512 * 1024;
        private const int MaxSingleResponseBytes = 64 * 1024;
        private readonly int _maxCount;
        private readonly Queue<(JsonObject Response, int Bytes)> _items = new();
        private int _bytes;

        internal RecentResponses(int maxCount) => _maxCount = maxCount;

        internal int Omitted { get; private set; }

        internal void Add(JsonObject response)
        {
            var bytes = Encoding.UTF8.GetByteCount(response.ToJsonString());
            if (bytes > MaxSingleResponseBytes)
            {
                response = new JsonObject
                {
                    ["success"] = response["success"]?.GetValue<bool>() ?? false,
                    ["message"] = Clip(response["message"]?.ToString() ?? string.Empty, 2048),
                    ["dataOmitted"] = true,
                };
                bytes = Encoding.UTF8.GetByteCount(response.ToJsonString());
            }

            while (_items.Count > 0 && (_items.Count >= _maxCount || _bytes + bytes > MaxBytes))
            {
                var removed = _items.Dequeue();
                _bytes -= removed.Bytes;
                Omitted++;
            }
            _items.Enqueue((response, bytes));
            _bytes += bytes;
        }

        internal JsonArray ToJson()
        {
            var array = new JsonArray();
            foreach (var (response, _) in _items)
                array.Add(response);
            return array;
        }
    }

    private sealed class RecentPrints
    {
        private const int MaxCount = 50;
        private readonly Queue<JsonObject> _items = new();

        internal int Omitted { get; private set; }

        internal void Add(JsonObject print)
        {
            if (_items.Count == MaxCount)
            {
                _items.Dequeue();
                Omitted++;
            }
            _items.Enqueue(print);
        }

        internal JsonArray ToJson()
        {
            var array = new JsonArray();
            foreach (var print in _items)
                array.Add(print);
            return array;
        }
    }

    private static string Clip(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength];

    internal static async Task<JsonObject> RunAsync(
        JsonObject request,
        string cwd,
        CancellationToken cancellationToken,
        Func<JsonObject, CancellationToken, Task<JsonObject>> execute,
        Func<string, string> resolvePath
    )
    {
        var script = request["script"]?.GetValue<string>() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(script))
            return ResponseError("Missing script");

        var inline = request["inline"]?.GetValue<bool>() ?? false;
        var lastOnly = request["lastOnly"]?.GetValue<bool>() ?? false;
        var tailCount = request["tailCount"]?.GetValue<int>() ?? 0;
        if (tailCount is < 0 or > 20 || (lastOnly && tailCount != 0))
            return ResponseError("source: use either --last or --tail 1..20");
        var bounded = lastOnly || tailCount > 0;
        IEnumerable<string> lines;
        if (inline)
        {
            lines = CommandLineTokenizer.SplitScript(script);
        }
        else
        {
            var fullPath = resolvePath(script);
            if (!File.Exists(fullPath))
                return ResponseError($"Script not found: {fullPath}");
            lines = await File.ReadAllLinesAsync(fullPath, cancellationToken);
        }

        var responses = bounded ? null : new JsonArray();
        var recentResponses = bounded ? new RecentResponses(tailCount > 0 ? tailCount : 20) : null;
        var recentPrints = bounded ? new RecentPrints() : null;
        JsonObject? lastResponse = null;
        JsonObject? lastStop = null;
        var commandIndex = 0;

        void RecordResponse(JsonObject response)
        {
            if (bounded)
                recentResponses!.Add(response);
            else
                responses!.Add(response);
        }

        JsonObject PrintSummary()
        {
            var batch = new JsonObject { ["executed"] = commandIndex };
            var prints = recentPrints!.ToJson();
            if (prints.Count > 0)
                batch["prints"] = prints;
            if (recentPrints.Omitted > 0)
                batch["omittedPrints"] = recentPrints.Omitted;
            if (
                lastOnly
                && lastStop is not null
                && lastStop["index"]!.GetValue<int>() < commandIndex
            )
                batch["lastStop"] = lastStop.DeepClone();
            return batch;
        }

        void RecordStop(JsonObject response)
        {
            if (
                !lastOnly
                || response["data"] is not JsonObject stopData
                || stopData["reason"] is null
            )
                return;

            var summary = new JsonObject { ["index"] = commandIndex };
            foreach (
                var key in new[]
                {
                    "reason",
                    "threadId",
                    "filePath",
                    "line",
                    "column",
                    "breakpointId",
                    "breakpoints",
                    "exitCode",
                    "unhandledException",
                    "context",
                    "hint",
                }
            )
            {
                if (stopData[key] is { } value)
                    summary[key] = value.DeepClone();
            }
            lastStop = summary;
        }

        JsonObject Failure(string message, string command, int completed)
        {
            var displayedMessage = bounded ? Clip(message, 2048) : message;
            var error = ResponseError(
                $"Script failed at command {commandIndex}: {displayedMessage}"
            );
            var data = new JsonObject
            {
                ["failedIndex"] = commandIndex,
                ["failedCommand"] = bounded ? Clip(command, 2048) : command,
                ["completed"] = completed,
                ["responses"] = bounded ? recentResponses!.ToJson() : responses,
            };
            if (bounded && recentResponses!.Omitted > 0)
                data["omittedResponses"] = recentResponses.Omitted;
            if (bounded && command.Length > 2048)
                data["failedCommandTruncated"] = true;
            error["data"] = data;
            if (bounded)
                error["batch"] = PrintSummary();
            return error;
        }

        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                continue;

            commandIndex++;
            var result = CommandParser.ParseScriptCommandLine(trimmed, cwd);
            if (!result.Success)
            {
                var parseError = ResponseError(result.Error ?? "Invalid command");
                RecordResponse(parseError);
                return Failure(result.Error ?? "Invalid command", trimmed, commandIndex - 1);
            }

            var response = await execute(result.Request!.ToJson(), cancellationToken);
            lastResponse = response;
            RecordResponse(response);
            RecordStop(response);
            if (!response["success"]!.GetValue<bool>())
            {
                var message = response["message"]?.GetValue<string>() ?? "Unknown error";
                return Failure(message, trimmed, commandIndex - 1);
            }

            if (bounded)
            {
                if (result.Request is PrintCommand print)
                {
                    var value = response["data"]?["value"]?.GetValue<string>();
                    if (value is not null)
                    {
                        var type = response["data"]?["type"]?.ToString() ?? string.Empty;
                        var printResult = new JsonObject
                        {
                            ["index"] = commandIndex,
                            ["expression"] = Clip(print.Expression, 512),
                            ["type"] = Clip(type, 256),
                            ["value"] = Clip(value, 4096),
                        };
                        if (
                            print.Expression.Length > 512
                            || type.Length > 256
                            || value.Length > 4096
                        )
                            printResult["truncated"] = true;
                        if (response["data"]?["hint"]?.GetValue<string>() is { } hint)
                            printResult["hint"] = Clip(hint, 512);
                        recentPrints!.Add(printResult);
                    }
                }
            }
        }

        if (lastOnly && lastResponse is not null)
        {
            lastResponse["batch"] = PrintSummary();
            return lastResponse;
        }

        if (tailCount > 0)
        {
            var data = new JsonObject
            {
                ["responses"] = recentResponses!.ToJson(),
                ["count"] = commandIndex,
            };
            if (recentResponses.Omitted > 0)
                data["omittedResponses"] = recentResponses.Omitted;
            var response = ResponseOk("Script executed", data);
            response["batch"] = PrintSummary();
            return response;
        }

        return ResponseOk(
            "Script executed",
            new JsonObject { ["responses"] = responses, ["count"] = commandIndex }
        );
    }
}
