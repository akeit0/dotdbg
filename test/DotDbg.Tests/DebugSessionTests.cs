using System.Text.Json.Nodes;
using DotDbg.Engine;
using DotDbg.Ipc;
using Xunit;

namespace DotDbg.Tests;

public class DebugSessionTests
{
    [Fact]
    public async Task Source_CanBatchHelpAndSchema()
    {
        using var session = new DebugSession();
        var result = await session.HandleOperationAsync(
            new JsonObject
            {
                ["op"] = "source",
                ["script"] = "help source; schema",
                ["inline"] = true,
            },
            CancellationToken.None
        );

        Assert.True(result["success"]!.GetValue<bool>(), result["message"]?.ToString());
        var responses = result["data"]!["responses"]!.AsArray();
        Assert.Equal(2, responses.Count);
        Assert.Contains(
            "source [--last|--tail N]",
            responses[0]!["data"]!["text"]!.GetValue<string>()
        );
        Assert.NotEmpty(responses[1]!["data"]!["schema"]!["commands"]!.AsArray());
    }

    [Fact]
    public async Task HelpAllListsCommandSyntaxInOneResponse()
    {
        using var session = new DebugSession();
        var response = await session.HandleOperationAsync(
            new JsonObject { ["op"] = "help", ["subject"] = "all" },
            CancellationToken.None
        );

        Assert.True(response["success"]!.GetValue<bool>());
        var text = response["data"]!["text"]!.GetValue<string>();
        Assert.Contains("break [--il]", text);
        Assert.Contains("run [--no-jmc]", text);
        Assert.Contains("source [--last|--tail N]", text);
    }

    [Fact]
    public async Task File_RejectsInvalidTargetWithoutReplacingCurrentTarget()
    {
        using var session = new DebugSession();
        var existing = typeof(DebugSession).Assembly.Location;
        var selected = await session.HandleOperationAsync(
            new JsonObject { ["op"] = "file", ["path"] = existing },
            CancellationToken.None
        );
        Assert.True(selected["success"]!.GetValue<bool>());

        var missing = await session.HandleOperationAsync(
            new JsonObject
            {
                ["op"] = "file",
                ["path"] = Path.Combine(
                    Path.GetTempPath(),
                    $"dotdbg-missing-{Guid.NewGuid():N}.dll"
                ),
            },
            CancellationToken.None
        );
        Assert.False(missing["success"]!.GetValue<bool>());
        Assert.Contains("Target not found", missing["message"]!.GetValue<string>());

        var unsupportedPath = Path.GetTempFileName();
        try
        {
            var unsupported = await session.HandleOperationAsync(
                new JsonObject { ["op"] = "file", ["path"] = unsupportedPath },
                CancellationToken.None
            );
            Assert.False(unsupported["success"]!.GetValue<bool>());
            Assert.Contains("Unsupported target type", unsupported["message"]!.GetValue<string>());
        }
        finally
        {
            File.Delete(unsupportedPath);
        }

        var current = await session.HandleOperationAsync(
            new JsonObject { ["op"] = "info", ["subject"] = "files" },
            CancellationToken.None
        );
        Assert.Equal(existing, current["data"]?["file"]?.GetValue<string>());
    }

    [Theory]
    [InlineData("{\"op\":42}")]
    [InlineData("{\"op\":\"ping\",\"cwd\":true}")]
    public async Task HandleOperation_MalformedEnvelope_ReturnsError(string json)
    {
        using var session = new DebugSession();
        var request = JsonNode.Parse(json)!.AsObject();

        var response = await session.HandleOperationAsync(request, CancellationToken.None);

        Assert.False(response["success"]!.GetValue<bool>());
        Assert.False(string.IsNullOrWhiteSpace(response["message"]?.GetValue<string>()));
    }

    [Fact]
    public async Task EventsQueriesAreEmptyWithoutTargetActivity()
    {
        using var session = new DebugSession();
        await session.HandleOperationAsync(
            new JsonObject { ["op"] = "info", ["subject"] = "status" },
            CancellationToken.None
        );

        var events = await session.HandleOperationAsync(
            new JsonObject { ["op"] = "events", ["since"] = 0 },
            CancellationToken.None
        );
        Assert.True(events["success"]!.GetValue<bool>());
        Assert.Empty(events["data"]!["events"]!.AsArray());
        Assert.Equal(0, events["data"]!["nextSeq"]?.GetValue<long>());

        var output = await session.HandleOperationAsync(
            new JsonObject { ["op"] = "output" },
            CancellationToken.None
        );
        Assert.Empty(output["data"]!["events"]!.AsArray());
    }

    [Fact]
    public async Task TraceLogRejectsInvalidSinkWithoutReplacingActiveSink()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dotdbg-trace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var blocker = Path.Combine(directory, "blocker");
        var validLog = Path.Combine(directory, "valid.log");
        await File.WriteAllTextAsync(blocker, string.Empty);
        try
        {
            using var session = new DebugSession();
            var valid = await session.HandleOperationAsync(
                new JsonObject { ["op"] = "trace-log", ["file"] = validLog },
                CancellationToken.None
            );
            Assert.True(valid["success"]!.GetValue<bool>());
            Assert.True(File.Exists(validLog));

            var invalid = await session.HandleOperationAsync(
                new JsonObject
                {
                    ["op"] = "trace-log",
                    ["file"] = Path.Combine(blocker, "trace.log"),
                },
                CancellationToken.None
            );
            Assert.False(invalid["success"]!.GetValue<bool>());
            Assert.Contains("Failed to open trace log", invalid["message"]!.GetValue<string>());

            var current = await session.HandleOperationAsync(
                new JsonObject { ["op"] = "info", ["subject"] = "trace" },
                CancellationToken.None
            );
            Assert.Equal(validLog, current["data"]!["file"]!.GetValue<string>());
        }
        finally
        {
            File.Delete(validLog);
            File.Delete(blocker);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task InfoTraceReturnsBoundedTailAndForwardPages()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dotdbg-trace-{Guid.NewGuid():N}.log");
        try
        {
            var expected = Enumerable.Range(0, 249).Select(i => $"line-{i}").ToList();
            expected.Add(new string('x', 20_000));
            await File.WriteAllLinesAsync(path, expected);
            using var session = new DebugSession();
            var configured = await session.HandleOperationAsync(
                new JsonObject { ["op"] = "trace-log", ["file"] = path },
                CancellationToken.None
            );
            Assert.True(configured["success"]!.GetValue<bool>());

            var tail = await session.HandleOperationAsync(
                new JsonObject { ["op"] = "info", ["subject"] = "trace" },
                CancellationToken.None
            );
            var tailData = tail["data"]!;
            Assert.Equal(100, tailData["traceLog"]!.AsArray().Count);
            Assert.Equal(150, tailData["startLine"]!.GetValue<long>());
            Assert.Equal(250, tailData["nextLine"]!.GetValue<long>());
            Assert.Equal(250, tailData["totalLines"]!.GetValue<long>());
            Assert.Equal(1, tailData["truncatedLines"]!.GetValue<int>());
            Assert.Equal(4096, tailData["traceLog"]!.AsArray()[^1]!.GetValue<string>().Length);

            var first = await session.HandleOperationAsync(
                new JsonObject
                {
                    ["op"] = "info",
                    ["subject"] = "trace",
                    ["since"] = 0,
                    ["limit"] = 200,
                },
                CancellationToken.None
            );
            Assert.True(first["success"]!.GetValue<bool>(), first.ToJsonString());
            Assert.Equal(200, first["data"]!["traceLog"]!.AsArray().Count);
            Assert.True(first["data"]!["hasMore"]!.GetValue<bool>());
            Assert.Equal(200, first["data"]!["nextLine"]!.GetValue<long>());

            var second = await session.HandleOperationAsync(
                new JsonObject
                {
                    ["op"] = "info",
                    ["subject"] = "trace",
                    ["since"] = 200,
                    ["limit"] = 200,
                },
                CancellationToken.None
            );
            Assert.Equal(50, second["data"]!["traceLog"]!.AsArray().Count);
            Assert.False(second["data"]!["hasMore"]!.GetValue<bool>());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task InfoTraceStaysBelowIpcLimitForLargeLog()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dotdbg-trace-{Guid.NewGuid():N}.log");
        try
        {
            var longLine = new string('x', 50_000);
            await File.WriteAllLinesAsync(path, Enumerable.Repeat(longLine, 250));
            using var session = new DebugSession();
            await session.HandleOperationAsync(
                new JsonObject { ["op"] = "trace-log", ["file"] = path },
                CancellationToken.None
            );

            var response = await session.HandleOperationAsync(
                new JsonObject
                {
                    ["op"] = "info",
                    ["subject"] = "trace",
                    ["since"] = 0,
                    ["limit"] = 200,
                },
                CancellationToken.None
            );
            Assert.True(response["success"]!.GetValue<bool>());
            Assert.True(
                OperationProtocol.Serialize(response).Length < OperationProtocol.MaxMessageBytes
            );
            Assert.Equal(200, response["data"]!["truncatedLines"]!.GetValue<int>());
            Assert.True(response["data"]!["hasMore"]!.GetValue<bool>());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IpcRejectsOversizedOutgoingMessage()
    {
        var response = new JsonObject
        {
            ["data"] = new string('x', OperationProtocol.MaxMessageBytes),
        };

        Assert.Throws<InvalidDataException>(() => OperationProtocol.Serialize(response));
    }

    [Fact]
    public async Task CoLocatedBreakpointsRejectRulesTheyCannotApplyIndependently()
    {
        using var session = new DebugSession();
        const string location = "Program.cs:10";
        var first = await session.HandleOperationAsync(
            new JsonObject
            {
                ["op"] = "break",
                ["location"] = location,
                ["condition"] = "n == 1",
            },
            CancellationToken.None
        );
        Assert.True(first["success"]!.GetValue<bool>());
        var firstId = first["data"]!["id"]!.GetValue<int>();

        var conflictingBreak = await session.HandleOperationAsync(
            new JsonObject { ["op"] = "break", ["location"] = location },
            CancellationToken.None
        );
        Assert.False(conflictingBreak["success"]!.GetValue<bool>());
        var conflictingTrace = await session.HandleOperationAsync(
            new JsonObject
            {
                ["op"] = "trace",
                ["location"] = location,
                ["expression"] = "n",
            },
            CancellationToken.None
        );
        Assert.False(conflictingTrace["success"]!.GetValue<bool>());

        await session.HandleOperationAsync(
            new JsonObject { ["op"] = "disable", ["breakpointId"] = firstId },
            CancellationToken.None
        );
        var second = await session.HandleOperationAsync(
            new JsonObject { ["op"] = "break", ["location"] = location },
            CancellationToken.None
        );
        var third = await session.HandleOperationAsync(
            new JsonObject { ["op"] = "break", ["location"] = location },
            CancellationToken.None
        );
        Assert.True(second["success"]!.GetValue<bool>());
        Assert.True(third["success"]!.GetValue<bool>());
        var secondId = second["data"]!["id"]!.GetValue<int>();
        var thirdId = third["data"]!["id"]!.GetValue<int>();

        foreach (
            var request in new[]
            {
                new JsonObject
                {
                    ["op"] = "condition",
                    ["breakpointId"] = secondId,
                    ["condition"] = "n == 2",
                },
                new JsonObject
                {
                    ["op"] = "ignore",
                    ["breakpointId"] = thirdId,
                    ["count"] = 1,
                },
                new JsonObject { ["op"] = "enable", ["breakpointId"] = firstId },
            }
        )
        {
            var rejected = await session.HandleOperationAsync(request, CancellationToken.None);
            Assert.False(rejected["success"]!.GetValue<bool>());
            Assert.Contains("Co-located", rejected["message"]!.GetValue<string>());
        }

        var listed = await session.HandleOperationAsync(
            new JsonObject { ["op"] = "breakpoints" },
            CancellationToken.None
        );
        Assert.Equal(3, listed["data"]!["breakpoints"]!.AsArray().Count);
        Assert.Null(listed["data"]!["breakpoints"]![1]!["condition"]);
        Assert.Null(listed["data"]!["breakpoints"]![2]!["hitCondition"]);
    }

    [Fact]
    public async Task ListAtFileStartShowsFiveLines()
    {
        using var session = new DebugSession();
        var source = Path.Combine(Path.GetTempPath(), $"dotdbg-list-{Guid.NewGuid():N}.cs");
        await File.WriteAllLinesAsync(source, ["one", "two", "three", "four", "five", "six"]);
        try
        {
            var response = await session.HandleOperationAsync(
                new JsonObject { ["op"] = "list", ["location"] = $"{source}:1" },
                CancellationToken.None
            );

            Assert.True(response["success"]!.GetValue<bool>());
            Assert.Equal(1, response["data"]!["startLine"]!.GetValue<int>());
            Assert.Equal(5, response["data"]!["lines"]!.AsArray().Count);

            var outside = await session.HandleOperationAsync(
                new JsonObject { ["op"] = "list", ["location"] = $"{source}:7" },
                CancellationToken.None
            );
            Assert.False(outside["success"]!.GetValue<bool>());
            Assert.Contains("outside", outside["message"]!.GetValue<string>());
        }
        finally
        {
            File.Delete(source);
        }
    }

    [Fact]
    public async Task ListCanShowLargerSpanAroundLineAndAtFileEnd()
    {
        using var session = new DebugSession();
        var source = Path.Combine(Path.GetTempPath(), $"dotdbg-list-{Guid.NewGuid():N}.cs");
        await File.WriteAllLinesAsync(source, Enumerable.Range(1, 30).Select(n => $"line {n}"));
        try
        {
            var middle = await session.HandleOperationAsync(
                new JsonObject
                {
                    ["op"] = "list",
                    ["location"] = $"{source}:15",
                    ["lineCount"] = 20,
                },
                CancellationToken.None
            );
            Assert.True(middle["success"]!.GetValue<bool>());
            Assert.Equal(6, middle["data"]!["startLine"]!.GetValue<int>());
            Assert.Equal(20, middle["data"]!["lines"]!.AsArray().Count);

            var end = await session.HandleOperationAsync(
                new JsonObject
                {
                    ["op"] = "list",
                    ["location"] = $"{source}:30",
                    ["lineCount"] = 20,
                },
                CancellationToken.None
            );
            Assert.True(end["success"]!.GetValue<bool>());
            Assert.Equal(11, end["data"]!["startLine"]!.GetValue<int>());
            Assert.Equal("line 30", end["data"]!["lines"]![19]!.GetValue<string>());

            var tooLarge = await session.HandleOperationAsync(
                new JsonObject
                {
                    ["op"] = "list",
                    ["location"] = $"{source}:15",
                    ["lineCount"] = 101,
                },
                CancellationToken.None
            );
            Assert.False(tooLarge["success"]!.GetValue<bool>());
        }
        finally
        {
            File.Delete(source);
        }
    }

    [Fact]
    public async Task SourceTailKeepsRequestedRecentResponses()
    {
        using var session = new DebugSession();
        var response = await session.HandleOperationAsync(
            new JsonObject
            {
                ["op"] = "source",
                ["inline"] = true,
                ["tailCount"] = 2,
                ["script"] = "info status; info status; info status; info status",
            },
            CancellationToken.None
        );

        Assert.True(response["success"]!.GetValue<bool>());
        Assert.Equal(4, response["data"]!["count"]!.GetValue<int>());
        Assert.Equal(2, response["data"]!["omittedResponses"]!.GetValue<int>());
        Assert.Equal(2, response["data"]!["responses"]!.AsArray().Count);
    }

    [Fact]
    public async Task SourceLastReportsPartialResponsesOnFailure()
    {
        using var session = new DebugSession();
        var response = await session.HandleOperationAsync(
            new JsonObject
            {
                ["op"] = "source",
                ["inline"] = true,
                ["lastOnly"] = true,
                ["script"] = "info status; unknown-command",
            },
            CancellationToken.None
        );

        Assert.False(response["success"]!.GetValue<bool>());
        Assert.Equal(2, response["data"]!["failedIndex"]!.GetValue<int>());
        Assert.Equal("unknown-command", response["data"]!["failedCommand"]!.GetValue<string>());
        Assert.Equal(1, response["data"]!["completed"]!.GetValue<int>());
        var responses = response["data"]!["responses"]!.AsArray();
        Assert.Equal(2, responses.Count);
        Assert.True(responses[0]!["success"]!.GetValue<bool>());
        Assert.False(responses[1]!["success"]!.GetValue<bool>());
    }

    [Fact]
    public async Task SourceLastBoundsFailureHistory()
    {
        using var session = new DebugSession();
        var script = string.Join("; ", Enumerable.Repeat("info status", 45)) + "; unknown-command";
        var response = await session.HandleOperationAsync(
            new JsonObject
            {
                ["op"] = "source",
                ["inline"] = true,
                ["lastOnly"] = true,
                ["script"] = script,
            },
            CancellationToken.None
        );

        Assert.False(response["success"]!.GetValue<bool>());
        var data = response["data"]!;
        Assert.Equal(46, data["failedIndex"]!.GetValue<int>());
        Assert.Equal(45, data["completed"]!.GetValue<int>());
        Assert.Equal(20, data["responses"]!.AsArray().Count);
        Assert.Equal(26, data["omittedResponses"]!.GetValue<int>());
        Assert.False(data["responses"]!.AsArray()[^1]!["success"]!.GetValue<bool>());
    }

    [Fact]
    public async Task SourceLastSummarizesLargeIntermediateResponse()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dotdbg-source-{Guid.NewGuid():N}.cs");
        await File.WriteAllTextAsync(path, new string('x', 70_000));
        try
        {
            using var session = new DebugSession();
            var response = await session.HandleOperationAsync(
                new JsonObject
                {
                    ["op"] = "source",
                    ["inline"] = true,
                    ["lastOnly"] = true,
                    ["script"] = $"list \"{path}:1\" --lines 1; unknown-command",
                },
                CancellationToken.None
            );

            Assert.False(response["success"]!.GetValue<bool>());
            var responses = response["data"]!["responses"]!.AsArray();
            Assert.Equal(2, responses.Count);
            Assert.True(responses[0]!["success"]!.GetValue<bool>());
            Assert.True(responses[0]!["dataOmitted"]!.GetValue<bool>());
            Assert.Null(responses[0]!["data"]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
