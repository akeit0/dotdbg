using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotDbg.Cli;

internal static class ConsoleResponseWriter
{
    private static readonly JsonSerializerOptions DisplayStringOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static void PrintResponse(JsonObject response)
    {
        var success = response["success"]?.GetValue<bool>() ?? false;
        var message = response["message"]?.GetValue<string>() ?? string.Empty;
        var data = response["data"];

        if (!success)
        {
            Console.Error.WriteLine($"Error: {message}");
        }
        else if (!string.IsNullOrEmpty(message))
        {
            Console.WriteLine(message);
        }

        if (response["batch"]?["prints"] is JsonArray batchPrints)
        {
            foreach (var print in batchPrints)
                Console.WriteLine($"{print?["expression"]} = ({print?["type"]}) {print?["value"]}");
        }

        if (data is null)
            return;

        if (data["mode"] is JsonValue mode)
            Console.WriteLine($"Mode: {mode.GetValue<string>()}");
        if (
            data["reason"]?.GetValue<string>() == "exited"
            && data["exitCode"] is JsonValue exitCode
        )
            Console.WriteLine($"Exit code: {exitCode.GetValue<int>()}");
        if (data["unhandledException"]?.GetValue<bool>() == true)
            Console.WriteLine("Unhandled exception: true");
        if (data["reason"] is JsonValue)
            PrintStopSummary(data);

        if (data["frames"] is JsonArray frames)
        {
            foreach (var frame in frames)
            {
                var tid = frame?["threadId"];
                if (tid is not null)
                {
                    Console.WriteLine($"Thread {tid}: {frame!["name"]?.GetValue<string>()}");
                    var inner = frame["frames"] as JsonArray;
                    if (inner is not null)
                    {
                        foreach (var f in inner)
                        {
                            PrintFrame(f);
                        }
                    }
                }
                else
                {
                    PrintFrame(frame);
                }
            }
        }

        if (data["lines"] is JsonArray lines && data["startLine"] is not null)
        {
            var start = data["startLine"]!.GetValue<int>();
            var currentLine = data["currentLine"]?.GetValue<int?>();
            var index = 0;
            foreach (var line in lines)
            {
                var lineNo = start + index;
                var paddedLineNo = lineNo.ToString().PadLeft(4);
                if (currentLine == lineNo && paddedLineNo[0] == ' ')
                    paddedLineNo = "->" + paddedLineNo.Substring(1);
                else if (currentLine == lineNo)
                    paddedLineNo = "->" + paddedLineNo;
                Console.WriteLine($"{paddedLineNo}: {line}");
                index++;
            }
        }

        // Stop responses carry matched breakpoint identities, not listing state.
        if (data["reason"] is null && data["breakpoints"] is JsonArray bps)
        {
            foreach (var bp in bps)
            {
                var id = bp!["id"]?.GetValue<int>() ?? bp["breakpointId"]?.GetValue<int>() ?? 0;
                var enabled = bp["enabled"]?.GetValue<bool>() ?? false;
                var verified = bp["verified"]?.GetValue<bool>() ?? false;
                var name = bp["name"]?.GetValue<string>();
                var condition = bp["condition"]?.GetValue<string>();
                var hitCondition = bp["hitCondition"]?.GetValue<string>();
                var log = bp["log"]?.GetValue<string>();
                var isIl = bp["isIl"]?.GetValue<bool>() ?? false;
                var extra = new System.Text.StringBuilder();
                if (!string.IsNullOrWhiteSpace(name))
                    extra.Append($" name={JsonSerializer.Serialize(name, DisplayStringOptions)}");
                if (!string.IsNullOrWhiteSpace(log))
                    extra.Append($" trace={JsonSerializer.Serialize(log, DisplayStringOptions)}");
                if (!string.IsNullOrWhiteSpace(condition))
                    extra.Append(
                        $" condition={JsonSerializer.Serialize(condition, DisplayStringOptions)}"
                    );
                if (!string.IsNullOrWhiteSpace(hitCondition))
                    extra.Append($" hit={hitCondition}");

                string location;
                if (isIl)
                {
                    var method = bp["method"]?.GetValue<string>() ?? "";
                    var ilOffset = bp["ilOffset"]?.GetValue<int>() ?? 0;
                    var module = bp["module"]?.GetValue<string>();
                    location = string.IsNullOrWhiteSpace(module)
                        ? $"{method}:IL_{ilOffset:X4}"
                        : $"{module}!{method}:IL_{ilOffset:X4}";
                }
                else
                {
                    var bpFile = bp["file"]?.GetValue<string>() ?? "";
                    var line = bp["line"]?.GetValue<int>() ?? 0;
                    location = $"{bpFile}:{line}";
                    var boundLine = bp["boundLine"]?.GetValue<int?>();
                    if (boundLine is not null && boundLine != line)
                        location += $" -> {bpFile}:{boundLine}";
                }

                Console.WriteLine($"{id}: {location} enabled={enabled} verified={verified}{extra}");
            }
        }

        if (data["value"] is not null)
        {
            var type = data["type"]?.GetValue<string>();
            if (string.IsNullOrEmpty(type))
            {
                Console.WriteLine(data["value"]?.GetValue<string>());
            }
            else
            {
                Console.WriteLine($"({type}) {data["value"]?.GetValue<string>()}");
            }
        }

        if (data["variables"] is JsonArray variables)
        {
            foreach (var v in variables)
            {
                var name = v!["name"]?.GetValue<string>() ?? "";
                var value = v["value"]?.GetValue<string>() ?? "";
                var type = v["type"]?.GetValue<string>();
                if (string.IsNullOrEmpty(type))
                    Console.WriteLine($"{name} = {value}");
                else
                    Console.WriteLine($"{name} = ({type}) {value}");
            }
        }

        if (data["threads"] is JsonArray threads)
        {
            foreach (var t in threads)
            {
                var id = t!["id"]?.GetValue<int>() ?? 0;
                var name = t["name"]?.GetValue<string>() ?? "";
                var selected = t["selected"]?.GetValue<bool>() ?? false;
                var marker = selected ? "*" : " ";
                Console.WriteLine($"{marker} {id}: {name}");
            }
        }

        if (data["files"] is JsonArray filesList)
        {
            Console.WriteLine($"Target: {data["target"]?.GetValue<string>() ?? "<none>"}");
            foreach (var file in filesList)
            {
                Console.WriteLine(file);
            }
        }
        else if (data["lines"] is not JsonArray && data["file"]?.GetValue<string>() is { } filePath)
        {
            Console.WriteLine(filePath);
        }

        if (data["responses"] is JsonArray responses)
        {
            foreach (var item in responses)
            {
                if (item is JsonObject r)
                    PrintResponse(r);
            }
        }

        if (data["modules"] is JsonArray modules)
        {
            foreach (var m in modules)
            {
                var name = m!["name"]?.GetValue<string>() ?? "";
                var path = m["path"]?.GetValue<string>() ?? "";
                var baseAddress = m["baseAddress"]?.GetValue<string>() ?? "";
                var isUserCode = m["isUserCode"]?.GetValue<bool>() ?? false;
                var hasSymbols = m["hasSymbols"]?.GetValue<bool>() ?? false;
                var marker = isUserCode ? "*" : " ";
                var symMarker = hasSymbols ? "(symbols)" : "(no symbols)";
                Console.WriteLine($"{marker} {name} {baseAddress} {symMarker} {path}");
            }
        }

        if (data["exception"] is JsonObject ex)
        {
            Console.WriteLine(
                $"Type: {ex["type"]?.GetValue<string>() ?? ex["fullType"]?.GetValue<string>() ?? ""}"
            );
            Console.WriteLine($"Message: {ex["message"]?.GetValue<string>() ?? ""}");
            Console.WriteLine($"HResult: {ex["hresult"]?.GetValue<int>() ?? 0}");
            Console.WriteLine($"Source: {ex["source"]?.GetValue<string>() ?? ""}");
            var stackTrace = ex["stackTrace"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(stackTrace))
            {
                Console.WriteLine("StackTrace:");
                foreach (
                    var line in stackTrace.Split(
                        ['\r', '\n'],
                        StringSplitOptions.RemoveEmptyEntries
                    )
                )
                    Console.WriteLine($"  {line}");
            }
        }

        if (data["status"] is JsonObject status)
        {
            Console.WriteLine($"Session: {status["sessionId"]?.GetValue<string>()}");
            var target = status["target"]?.GetValue<string>();
            Console.WriteLine($"Target: {(string.IsNullOrEmpty(target) ? "(none)" : target)}");
            Console.WriteLine($"Has process: {status["hasProcess"]?.GetValue<bool>() ?? false}");
            Console.WriteLine($"Process: {status["processId"]?.GetValue<int>() ?? 0}");
            Console.WriteLine($"Running: {status["running"]?.GetValue<bool>() ?? false}");
            if (status["terminalReason"] is JsonValue terminalReason)
                Console.WriteLine($"Terminal state: {terminalReason.GetValue<string>()}");
            if (status["exitCode"] is JsonValue statusExitCode)
                Console.WriteLine($"Exit code: {statusExitCode.GetValue<int>()}");
            if (status["unhandledException"]?.GetValue<bool>() == true)
                Console.WriteLine("Unhandled exception: true");
            var selectedThread = status["selectedThread"]?.GetValue<int>() ?? 0;
            var selectedFrame = status["selectedFrameIndex"]?.GetValue<int>() ?? 0;
            Console.WriteLine(
                $"Selected thread: {(selectedThread == 0 ? "(none)" : selectedThread)}"
            );
            Console.WriteLine(
                $"Selected frame: {(selectedThread == 0 ? "(none)" : selectedFrame)}"
            );
            Console.WriteLine($"Breakpoints: {status["breakpointCount"]?.GetValue<int>() ?? 0}");
            Console.WriteLine($"Watches: {status["watchCount"]?.GetValue<int>() ?? 0}");
        }
        if (data["hint"] is JsonValue hint)
            Console.WriteLine(hint.GetValue<string>());

        if (data["trace"] is JsonArray traceOutput)
        {
            foreach (var line in traceOutput)
                Console.WriteLine($"  {line?.GetValue<string>() ?? line?.ToString()}");
        }

        if (data["traceLog"] is JsonArray traceLog)
        {
            foreach (var line in traceLog)
                Console.WriteLine(line?.GetValue<string>() ?? line?.ToString());
        }

        if (data["events"] is JsonArray events)
        {
            foreach (var entry in events)
            {
                var seq = entry?["seq"]?.GetValue<long>() ?? 0;
                var kind = entry?["kind"]?.GetValue<string>() ?? string.Empty;
                var channel = entry?["channel"]?.GetValue<string>();
                var text = entry?["text"]?.GetValue<string>() ?? string.Empty;
                var exitSuffix = entry?["exitCode"] is JsonValue eventExitCode
                    ? $" (exit code {eventExitCode.GetValue<int>()})"
                    : string.Empty;
                var exceptionSuffix =
                    entry?["unhandledException"]?.GetValue<bool>() == true
                        ? " (unhandled exception)"
                        : string.Empty;
                var breakpoints = entry?["breakpoints"] as JsonArray;
                var breakpointSuffix = breakpoints is { Count: > 0 }
                    ? $" [bp {string.Join(",", breakpoints.Select(bp => bp?["id"]?.GetValue<int>() ?? 0))}]"
                    : string.Empty;
                Console.WriteLine(
                    $"{seq} {channel ?? kind}{breakpointSuffix}: {text}{exitSuffix}{exceptionSuffix}"
                );
            }
            Console.WriteLine($"Next sequence: {data["nextSeq"]?.GetValue<long>() ?? 0}");
            if (data["truncated"]?.GetValue<bool>() == true)
                Console.WriteLine("Older events were discarded.");
            if (data["hasMore"]?.GetValue<bool>() == true)
                Console.WriteLine("More events are available.");
        }

        if (data["source"] is JsonValue sourceNode)
        {
            Console.WriteLine(sourceNode.GetValue<string>());
        }

        if (data["frame"] is JsonObject contextFrame)
            PrintFrame(contextFrame);

        if (
            data["source"] is JsonObject contextSource
            && contextSource["lines"] is JsonArray contextLines
        )
        {
            var firstLine = contextSource["startLine"]?.GetValue<int>() ?? 1;
            var currentLine = contextSource["currentLine"]?.GetValue<int>() ?? 0;
            for (var i = 0; i < contextLines.Count; i++)
            {
                var lineNumber = firstLine + i;
                var marker = lineNumber == currentLine ? ">" : " ";
                Console.WriteLine(
                    $"{marker}{lineNumber, 4}: {contextLines[i]?.GetValue<string>()}"
                );
            }
        }

        PrintContextVariables(
            "Args",
            data["arguments"] as JsonArray,
            omitted: data["omittedArguments"]?.GetValue<int>() ?? 0
        );
        PrintContextVariables(
            "Locals",
            data["locals"] as JsonArray,
            omitted: data["omittedLocals"]?.GetValue<int>() ?? 0
        );

        if (data["watches"] is JsonObject watchValues)
        {
            foreach (var (key, node) in watchValues)
            {
                if (node is not JsonObject w)
                    continue;
                var expr = w["expression"]?.GetValue<string>() ?? key;
                var value = w["value"]?.GetValue<string>() ?? "";
                var type = w["type"]?.GetValue<string>();
                var changed = w["changed"]?.GetValue<bool>() ?? false;
                var marker = changed ? "*" : " ";
                if (string.IsNullOrEmpty(type))
                    Console.WriteLine($"{marker} {expr} = {value}");
                else
                    Console.WriteLine($"{marker} {expr} = ({type}) {value}");
            }
        }

        if (data["watches"] is JsonArray watchList)
        {
            foreach (var w in watchList)
            {
                var id = w!["id"]?.GetValue<int>() ?? w["watchId"]?.GetValue<int>() ?? 0;
                var expr = w["expression"]?.GetValue<string>() ?? "";
                var lastValue = w["lastValue"]?.GetValue<string>() ?? "";
                Console.WriteLine($"{id}: {expr} = {lastValue}");
            }
        }
    }

    private static void PrintFrame(JsonNode? frame)
    {
        if (frame is null)
            return;
        var index = frame["index"]?.GetValue<int>() ?? frame["id"]?.GetValue<int>() ?? 0;
        var name = frame["name"]?.GetValue<string>() ?? "";
        var line = frame["line"]?.GetValue<int>() ?? 0;
        var source = frame["source"]?.GetValue<string>() ?? "";
        var asyncCaller =
            frame["syntheticAsyncCaller"]?.GetValue<bool>() == true
                ? " [async caller; use info locals]"
                : string.Empty;
        Console.WriteLine($"#{index} {name} at {source}:{line}{asyncCaller}");
    }

    private static void PrintStopSummary(JsonNode data)
    {
        if (data["context"] is JsonObject context && context["frame"] is JsonObject frame)
        {
            PrintFrame(frame);
            if (context["source"] is JsonObject source && source["lines"] is JsonArray lines)
            {
                var start = source["startLine"]?.GetValue<int>() ?? 1;
                var current = source["currentLine"]?.GetValue<int>() ?? 0;
                var index = current - start;
                if (index >= 0 && index < lines.Count)
                    Console.WriteLine($"> {current}: {lines[index]?.GetValue<string>()}");
            }
            PrintContextVariables(
                "Args",
                context["arguments"] as JsonArray,
                3,
                context["omittedArguments"]?.GetValue<int>() ?? 0
            );
            PrintContextVariables(
                "Locals",
                context["locals"] as JsonArray,
                5,
                context["omittedLocals"]?.GetValue<int>() ?? 0
            );
        }
        else if (data["filePath"]?.GetValue<string>() is { Length: > 0 } filePath)
        {
            Console.WriteLine($"At: {filePath}:{data["line"]?.GetValue<int>() ?? 0}");
        }

        if (data["breakpoints"] is JsonArray breakpoints)
        {
            foreach (var breakpoint in breakpoints)
            {
                var id = breakpoint?["id"]?.GetValue<int>() ?? 0;
                var kind =
                    breakpoint?["isTracepoint"]?.GetValue<bool>() == true
                        ? "Tracepoint"
                        : "Breakpoint";
                var requestedFile = breakpoint?["file"]?.GetValue<string>();
                var requestedLine = breakpoint?["line"]?.GetValue<int>() ?? 0;
                var boundFile = data["filePath"]?.GetValue<string>();
                var boundLine = data["line"]?.GetValue<int>() ?? 0;
                if (
                    !string.IsNullOrWhiteSpace(requestedFile)
                    && (requestedLine != boundLine || requestedFile != boundFile)
                )
                    Console.WriteLine(
                        $"{kind} #{id}: requested {requestedFile}:{requestedLine}, bound at {boundFile}:{boundLine}"
                    );
                else
                    Console.WriteLine($"{kind} #{id}");
            }
        }

        if (data["recentOutput"] is JsonArray output && output.Count > 0)
        {
            Console.WriteLine("Recent output:");
            foreach (var entry in output)
                Console.WriteLine($"  {entry?["channel"]}: {entry?["text"]}");
            var omitted = data["omittedOutput"]?.GetValue<int>() ?? 0;
            if (omitted > 0)
                Console.WriteLine($"  ({omitted} earlier entries; use 'output' for more)");
        }

        if (data["recentDiagnostics"] is JsonArray diagnostics && diagnostics.Count > 0)
        {
            Console.WriteLine("Debugger diagnostics:");
            foreach (var entry in diagnostics)
                Console.WriteLine($"  {entry?["text"]}");
            var omitted = data["omittedDiagnostics"]?.GetValue<int>() ?? 0;
            if (omitted > 0)
                Console.WriteLine($"  ({omitted} earlier entries; use 'events --kind diagnostic')");
        }
    }

    private static void PrintContextVariables(
        string label,
        JsonArray? variables,
        int? limit = null,
        int omitted = 0
    )
    {
        if (variables is null)
            return;
        if (variables.Count == 0 && omitted == 0)
            return;
        Console.WriteLine($"{label}:");
        foreach (var variable in limit.HasValue ? variables.Take(limit.Value) : variables)
        {
            var name = variable?["name"]?.GetValue<string>() ?? string.Empty;
            var type = variable?["type"]?.GetValue<string>() ?? string.Empty;
            var value = variable?["value"]?.GetValue<string>() ?? string.Empty;
            Console.WriteLine($"  {name} = ({type}) {value}");
        }
        var additional = Math.Max(0, variables.Count - (limit ?? variables.Count)) + omitted;
        if (additional > 0)
            Console.WriteLine($"  ({additional} more; use 'info {label.ToLowerInvariant()}')");
    }
}
