using System.Diagnostics;
using System.Text.Json.Nodes;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace DotDbg.WorkloadTests;

public sealed class DebuggerWorkloads
{
    private static readonly string Root = FindRoot();
    private static readonly string ToolConfiguration = new DirectoryInfo(AppContext.BaseDirectory)
        .Parent!
        .Name;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvocationErrorAfterSessionValueRemainsJson(bool equalsForm)
    {
        var session = $"dotdbg-invalid-{Guid.NewGuid():N}";
        var args = equalsForm
            ? new[] { $"--session-id={session}", "--json", "--bad-option" }
            : new[] { "-s", session, "--json", "--bad-option" };
        var (exitCode, stdout, _) = InvokeCli(args);
        Assert.Equal(2, exitCode);
        var response = JsonNode.Parse(stdout)!;
        Assert.False(Boolean(response["success"]));
        Assert.Equal("invocation", String(response["phase"]));
    }

    [Fact]
    public void QuitWithoutResponseDoesNotClaimSuccess()
    {
        var (exitCode, stdout, _) = InvokeCli(
            "-s",
            $"dotdbg-absent-{Guid.NewGuid():N}",
            "--json",
            "quit"
        );
        Assert.Equal(1, exitCode);
        var response = JsonNode.Parse(stdout)!;
        Assert.False(Boolean(response["success"]));
        Assert.Equal("transport", String(response["phase"]));
    }

    [Fact]
    public void HelpHonorsJsonOutputWithoutStartingSession()
    {
        foreach (
            var args in new[]
            {
                new[] { "--json", "--help" },
                new[] { "--json", "help", "run" },
                new[] { "--json", "help", "all" },
            }
        )
        {
            var (exitCode, stdout, _) = InvokeCli(args);
            Assert.Equal(0, exitCode);
            var response = JsonNode.Parse(stdout)!;
            Assert.True(Boolean(response["success"]));
            Assert.Equal("Help", String(response["message"]));
            Assert.False(string.IsNullOrWhiteSpace(String(response["data"]!["text"])));
        }

        var plain = InvokeCli("help", "run");
        Assert.Equal(0, plain.ExitCode);
        Assert.StartsWith("run ", plain.Stdout);
    }

    [Fact]
    public void TextIlStopDoesNotRenderListingFlags()
    {
        var session = $"dotdbg-text-{Guid.NewGuid():N}";
        try
        {
            Assert.Equal(
                0,
                InvokeCli(
                    "-s",
                    session,
                    "file",
                    "samples/DbgTargetShapeApp/DbgTargetShapeApp.csproj",
                    "-c",
                    "Debug"
                ).ExitCode
            );
            Assert.Equal(
                0,
                InvokeCli(
                    "-s",
                    session,
                    "break",
                    "--il",
                    "DbgTargetShapeApp.ShapeService.Add:0x1a"
                ).ExitCode
            );
            var (exitCode, stdout, _) = InvokeCli("-s", session, "run", "--wait");
            Assert.Equal(0, exitCode);
            Assert.Contains("Breakpoint #1", stdout);
            Assert.DoesNotContain("enabled=False", stdout);
            Assert.DoesNotContain("verified=False", stdout);
        }
        finally
        {
            InvokeCli("-s", session, "quit");
        }
    }

    [Fact]
    public void KillTerminatesRunningTarget()
    {
        using var debug = new Debugger();
        debug.Call("file", "samples/DbgTargetTickerApp/DbgTargetTickerApp.csproj", "-c", "Debug");
        debug.Call("run", "--", "--ticks", "1000", "--delay-ms", "1000");
        var processId = Integer(debug.Call("info", "status")["data"]!["status"]!["processId"]);
        Assert.True(processId > 0);
        try
        {
            debug.Call("kill");
            Assert.False(Boolean(debug.Call("info", "status")["data"]!["status"]!["hasProcess"]));
            try
            {
                using var process = Process.GetProcessById(processId);
                Assert.True(process.WaitForExit(5000));
            }
            catch (ArgumentException)
            { /* Already exited and removed from the process table. */
            }
        }
        finally
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (ArgumentException)
            { /* Already exited and removed from the process table. */
            }
        }
    }

    [Fact]
    public void ArgumentsEnvironmentOutputAndCompactContext()
    {
        using var debug = new Debugger();
        debug.Call("file", "samples/DbgTargetAgentApp/DbgTargetAgentApp.csproj", "-c", "Debug");
        debug.Call("break", "samples/DbgTargetAgentApp/Program.cs:15 if index == 1");
        debug.Call(
            "run",
            "--env",
            "DOTDBG_MARKER=hello world",
            "--cwd",
            "samples/DbgTargetAgentApp",
            "upper"
        );
        var stop = debug.Call("wait", "--timeout", "10")["data"]!;
        Assert.Equal("breakpoint", String(stop["reason"]));
        Assert.Equal(15, Integer(stop["line"]));

        var arguments = debug.Call("info", "args")["data"]!["variables"]!;
        Assert.Equal("hello world", Variable(arguments, "marker"));
        Assert.Equal("upper", Variable(arguments, "mode"));
        Assert.NotEmpty(String(debug.Call("decompile")["data"]!["source"]));
        Assert.Contains("IL_", String(debug.Call("decompile", "--il")["data"]!["source"]));
        var context = debug.Call("context")["data"]!;
        Assert.Equal(15, Integer(context["frame"]!["line"]));
        Assert.Equal("HELLO WORLD:1:upper", Variable(context["locals"]!, "result"));

        var output = debug.Call("output")["data"]!["events"]!;
        foreach (var channel in new[] { "stdout", "stderr", "debug" })
            Assert.Contains(output.AsArray(), item => String(item!["channel"]) == channel);
        var events = debug.Call("events")["data"]!;
        Assert.Contains(events["events"]!.AsArray(), item => String(item!["kind"]) == "stop");
        var cursor = String(events["nextSeq"]);
        Assert.Empty(debug.Call("events", "--since", cursor)["data"]!["events"]!.AsArray());
        debug.Call("frame", "1");
        Assert.EndsWith(
            "DbgTargetAgentApp",
            Variable(debug.Call("info", "locals")["data"]!["variables"]!, "workDirectory")
        );

        var longMarker = new string('x', 300);
        debug.Call("run", "--env", $"DOTDBG_MARKER={longMarker}", "upper");
        debug.Call("wait", "--timeout", "10");
        context = debug.Call("context")["data"]!;
        Assert.True(Variable(context["arguments"]!, "marker").Length <= 161);
    }

    [Fact]
    public void EnvironmentOverrideNamesMatchTargetPlatform()
    {
        using var debug = new Debugger();
        debug.Call("file", "samples/DbgTargetAgentApp/DbgTargetAgentApp.csproj", "-c", "Debug");
        var stop = debug.Call(
            "run",
            "--wait",
            "--env",
            "DOTDBG_MARKER=UPPER",
            "--env",
            "dotdbg_marker=lower"
        );
        Assert.Equal("exited", String(stop["data"]!["reason"]));
        var stdout = debug.Call("output")["data"]!["events"]!
            .AsArray()
            .Where(item => String(item!["channel"]) == "stdout")
            .Select(item => String(item!["text"]))
            .ToList();
        Assert.Contains(
            stdout,
            line =>
                line.Contains(
                    OperatingSystem.IsWindows() ? "lower:0:default" : "UPPER:0:default",
                    StringComparison.Ordinal
                )
        );
    }

    [Fact]
    public void IlBreakpointAndStep()
    {
        using var debug = new Debugger();
        debug.Call("file", "samples/DbgTargetShapeApp/DbgTargetShapeApp.csproj", "-c", "Debug");
        var breakpoint = debug.Call("break", "--il", "DbgTargetShapeApp.ShapeService.Add:IL_0000");
        debug.Call("run");
        Assert.Equal(
            "breakpoint",
            String(debug.Call("wait", "--timeout", "10")["data"]!["reason"])
        );
        Assert.Equal("step", String(debug.Call("next")["data"]!["reason"]));
        debug.Call("catch", "none");
        debug.Call("delete", String(breakpoint["data"]!["breakpointId"]));
        Assert.Equal(
            "exited",
            String(debug.Call("continue", "--wait", "--timeout", "10")["data"]!["reason"])
        );
    }

    [Fact]
    public void FileAppTracepointAndBreakpoint()
    {
        using var debug = new Debugger();
        debug.Call("file", "samples/DbgTargetFileApp/hello.cs", "-c", "Debug");
        debug.Call(
            "source",
            "--last",
            "-c",
            "trace samples/DbgTargetFileApp/hello.cs:27 n + \": trace\""
        );
        debug.Call("run", "loop");
        var stop = debug.Call("wait", "--timeout", "10")["data"]!;
        Assert.Equal("trace", String(stop["reason"]));
        Assert.NotEmpty(stop["trace"]!.AsArray());
        Assert.Contains(stop["trace"]!.AsArray(), item => String(item).Contains(": trace"));
        Assert.NotEmpty(debug.Call("events", "--kind", "trace")["data"]!["events"]!.AsArray());
        Assert.Empty(debug.Call("events", "--kind", "stop")["data"]!["events"]!.AsArray());
        var breakpoint = debug.Call("break", "samples/DbgTargetFileApp/hello.cs:27");
        var mixedStop = debug.Call("wait", "--timeout", "10")["data"]!;
        Assert.Equal("breakpoint", String(mixedStop["reason"]));
        Assert.Equal(2, mixedStop["breakpoints"]!.AsArray().Count);
        Assert.Null(mixedStop["breakpointId"]);
        Assert.Contains(
            mixedStop["breakpoints"]!.AsArray(),
            item => Integer(item!["id"]) == Integer(breakpoint["data"]!["breakpointId"])
        );
        Assert.Contains(
            debug.Call("events", "--kind", "stop")["data"]!["events"]!.AsArray(),
            item => String(item!["reason"]) == "breakpoint"
        );
        debug.Call("disable", String(breakpoint["data"]!["breakpointId"]));
        debug.Call("continue");
        Assert.Equal("trace", String(debug.Call("wait", "--timeout", "10")["data"]!["reason"]));
    }

    [Fact]
    public void FloatingPointExpression()
    {
        using var debug = new Debugger();
        debug.Call("file", "samples/DbgTargetShapeApp/DbgTargetShapeApp.csproj", "-c", "Debug");
        debug.Call("break", "samples/DbgTargetShapeApp/Program.cs:93 if metric.Area > 90");
        debug.Call("run");
        var stop = debug.Call("wait", "--timeout", "10")["data"]!;
        Assert.Equal("breakpoint", String(stop["reason"]));
        Assert.Equal(93, Integer(stop["line"]));
        var comparison = debug.Call("print", "metric.Area > 90")["data"]!;
        Assert.Equal("bool", String(comparison["type"]));
        Assert.Equal("true", String(comparison["value"]));
        Assert.Equal("double", String(debug.Call("print", "metric.Area + 1")["data"]!["type"]));
    }

    [Fact]
    public void BreakpointReportsRequestedAndBoundLines()
    {
        using var debug = new Debugger();
        var source = "samples/DbgTargetCheckoutApp/Program.cs";
        var requestedLine = SourceLine(
            source,
            "double.Parse(fields[1], CultureInfo.InvariantCulture),"
        );
        var boundLine = SourceLine(source, "var order = new Order(");
        debug.Call(
            "file",
            "samples/DbgTargetCheckoutApp/DbgTargetCheckoutApp.csproj",
            "-c",
            "Debug"
        );
        var pending = debug.Call("break", $"{source}:{requestedLine}")["data"]!;
        Assert.Equal(requestedLine, Integer(pending["line"]));
        Assert.False(Boolean(pending["verified"]));
        Assert.Null(pending["boundLine"]);

        var stop = debug.Call("run", "--cwd", "samples/DbgTargetCheckoutApp", "--wait")["data"]!;
        Assert.Equal(boundLine, Integer(stop["line"]));
        Assert.Equal(Integer(pending["id"]), Integer(stop["breakpointId"]));
        var stopBreakpoint = stop["breakpoints"]![0]!;
        Assert.Equal(requestedLine, Integer(stopBreakpoint["line"]));
        Assert.Equal(
            Path.Combine("samples", "DbgTargetCheckoutApp", "Program.cs"),
            String(stopBreakpoint["file"])
        );
        var stopEvent = debug.Call("events", "--kind", "stop")["data"]!["events"]![0]!;
        Assert.Equal(Integer(pending["id"]), Integer(stopEvent["breakpointId"]));
        Assert.Equal(requestedLine, Integer(stopEvent["breakpoints"]![0]!["line"]));
        var breakpoint = debug.Call("breakpoints")["data"]!["breakpoints"]![0]!;
        Assert.Equal(requestedLine, Integer(breakpoint["line"]));
        Assert.Equal(boundLine, Integer(breakpoint["boundLine"]));
        Assert.True(Boolean(breakpoint["verified"]));
    }

    [Fact]
    public void TracepointOnContinuationLineUsesBoundBreakpoint()
    {
        using var debug = new Debugger();
        var source = "samples/DbgTargetCheckoutApp/Program.cs";
        var requestedLine = SourceLine(
            source,
            "double.Parse(fields[1], CultureInfo.InvariantCulture),"
        );
        var boundLine = SourceLine(source, "var order = new Order(");
        debug.Call(
            "file",
            "samples/DbgTargetCheckoutApp/DbgTargetCheckoutApp.csproj",
            "-c",
            "Debug"
        );
        debug.Call("trace", $"{source}:{requestedLine}", "fields[0]");

        var stop = debug.Call("run", "--cwd", "samples/DbgTargetCheckoutApp", "--wait")["data"]!;
        Assert.Equal("trace", String(stop["reason"]));
        Assert.Equal(boundLine, Integer(stop["line"]));
        Assert.Equal(requestedLine, Integer(stop["breakpoints"]![0]!["line"]));
        Assert.True(Boolean(stop["breakpoints"]![0]!["isTracepoint"]));
        Assert.Contains("A100", String(stop["trace"]![0]));
    }

    [Fact]
    public void CheckoutInvestigationAndExitStatus()
    {
        using var debug = new Debugger();
        var line = SourceLine("samples/DbgTargetCheckoutApp/Program.cs", "return total;");
        var target = debug.Call(
            "file",
            "samples/DbgTargetCheckoutApp/DbgTargetCheckoutApp.csproj",
            "-c",
            "Debug"
        );
        Assert.Equal(
            Path.Combine("samples", "DbgTargetCheckoutApp", "DbgTargetCheckoutApp.csproj"),
            String(target["data"]!["source"])
        );
        Assert.False(Path.IsPathFullyQualified(String(target["data"]!["file"])));
        var relativeSource = Path.Combine("samples", "DbgTargetCheckoutApp", "Program.cs");
        var breakpoint = debug.Call("break", $"samples/DbgTargetCheckoutApp/Program.cs:{line}");
        Assert.Contains($"{relativeSource}:{line}", String(breakpoint["message"]));
        Assert.Equal(
            relativeSource,
            String(debug.Call("breakpoints")["data"]!["breakpoints"]![0]!["file"])
        );
        debug.Call(
            "run",
            "--env",
            "DOTDBG_DISCOUNT=0.10",
            "--cwd",
            "samples/DbgTargetCheckoutApp",
            "--",
            "--tier",
            "vip"
        );
        var stop = debug.Call("wait", "--timeout", "10")["data"]!;
        Assert.Equal("breakpoint", String(stop["reason"]));
        Assert.Equal(line, Integer(stop["line"]));
        Assert.Equal(relativeSource, String(stop["filePath"]));
        Assert.Equal(relativeSource, String(stop["context"]!["frame"]!["source"]));
        Assert.Equal(line, Integer(stop["context"]!["frame"]!["line"]));
        Assert.Equal("53", Variable(stop["context"]!["locals"]!, "total"));
        var context = debug.Call("context")["data"]!;
        Assert.Equal(relativeSource, String(context["source"]!["file"]));
        Assert.Equal("0.1", Variable(context["arguments"]!, "rate"));
        Assert.Equal("60", Variable(context["locals"]!, "subtotal"));
        Assert.Equal("6", Variable(context["locals"]!, "discount"));
        Assert.Equal("53", Variable(context["locals"]!, "total"));
        stop = debug.Call("continue", "--wait", "--timeout", "10")["data"]!;
        Assert.Equal("breakpoint", String(stop["reason"]));
        Assert.Equal("32", String(debug.Call("print", "total")["data"]!["value"]));
        stop = debug.Call("continue", "--wait", "--timeout", "10")["data"]!;
        Assert.Equal("exited", String(stop["reason"]));
        Assert.Equal(1, Integer(stop["exitCode"]));
        Assert.True(stop["recentOutput"]!.AsArray().Count <= 4);
        Assert.True(stop["outputNextSeq"]!.GetValue<long>() > 0);
        Assert.Equal(1, Integer(debug.Call("wait", "--timeout", "1")["data"]!["exitCode"]));
        var afterExit = debug.CallFailure("continue", "--wait");
        Assert.Contains("Target has exited (code 1)", String(afterExit["message"]));
        Assert.Contains("run", String(afterExit["message"]));
        var exitedContext = debug.Call("context")["data"]!;
        Assert.Contains("run", String(exitedContext["hint"]));
        Assert.Null(exitedContext["inspectionError"]);
        var status = debug.Call("info", "status")["data"]!["status"]!;
        Assert.Equal(1, Integer(status["exitCode"]));
        Assert.False(Boolean(status["hasProcess"]));
        var output = debug.Call("output")["data"]!["events"]!.AsArray();
        Assert.Contains(
            output,
            item =>
                String(item!["channel"]) == "stdout"
                && String(item["text"]).Contains("actual=53.00 expected=59.00")
        );
        Assert.Contains(
            output,
            item =>
                String(item!["channel"]) == "stderr"
                && String(item["text"]).Contains("mismatch B200")
        );
        var stopEvents = debug.Call("events", "--kind", "stop")["data"]!["events"]!.AsArray();
        Assert.Contains(
            stopEvents,
            item => String(item!["reason"]) == "exited" && Integer(item["exitCode"]) == 1
        );
        Assert.Contains(
            stopEvents,
            item =>
                String(item!["reason"]) == "breakpoint"
                && String(item["filePath"]) == relativeSource
                && !String(item["text"]).Contains(Root, StringComparison.OrdinalIgnoreCase)
        );

        var outsideTarget = typeof(object).Assembly.Location;
        Assert.Contains(outsideTarget, String(debug.Call("file", outsideTarget)["message"]));
    }

    [Fact]
    public void ContextIncludesLocalsOnCurrentStatement()
    {
        using var debug = new Debugger();
        var line = SourceLine(
            "samples/DbgTargetCheckoutApp/Program.cs",
            "Console.WriteLine($\"order"
        );
        debug.Call(
            "file",
            "samples/DbgTargetCheckoutApp/DbgTargetCheckoutApp.csproj",
            "-c",
            "Debug"
        );
        debug.Call("break", $"samples/DbgTargetCheckoutApp/Program.cs:{line}");
        var stop = debug.Call(
            "run",
            "--cwd",
            "samples/DbgTargetCheckoutApp",
            "--wait",
            "--timeout",
            "10",
            "--",
            "--tier",
            "vip"
        )["data"]!;

        Assert.Equal("breakpoint", String(stop["reason"]));
        var context = stop["context"]!;
        Assert.True(Integer(context["omittedLocals"]) > 0);
        Assert.Equal("53", Variable(context["locals"]!, "actual"));
        Assert.Equal("59", Variable(context["locals"]!, "expected"));
    }

    [Fact]
    public void InlineBatchReturnsOnlyConditionalStop()
    {
        using var debug = new Debugger();
        var line = SourceLine("samples/DbgTargetCheckoutApp/Program.cs", "return total;");
        var script =
            $"file samples/DbgTargetCheckoutApp/DbgTargetCheckoutApp.csproj -c Debug; "
            + $"break samples/DbgTargetCheckoutApp/Program.cs:{line}; "
            + """condition 1 order.Id == "B200"; """
            + "run --env DOTDBG_DISCOUNT=0.10 --cwd samples/DbgTargetCheckoutApp --wait -- --tier vip";

        var response = debug.Call("source", "--last", "-c", script);

        Assert.Equal(4, Integer(response["batch"]!["executed"]));
        Assert.Null(response["batch"]!["lastStop"]);
        Assert.Null(response["data"]!["responses"]);
        Assert.Equal("breakpoint", String(response["data"]!["reason"]));
        var fullBatch = debug.Call("source", "-c", "print order.Id; print order.Subtotal");
        var results = fullBatch["data"]!["responses"]!.AsArray();
        Assert.Equal(2, results.Count);
        Assert.Equal("B200", String(results[0]!["data"]!["value"]));
        Assert.NotEmpty(String(results[1]!["data"]!["value"]));

        var stringLiteral = debug.Call("source", "--last", "-c", "print \"hello\"");
        Assert.Equal("hello", String(stringLiteral["data"]!["value"]));
        Assert.Null(stringLiteral["data"]!["hint"]);

        var quotedComparison = debug.Call("source", "--last", "-c", "print \"order.Subtotal > 0\"");
        Assert.Equal("order.Subtotal > 0", String(quotedComparison["data"]!["value"]));
        Assert.Contains("string literal", String(quotedComparison["data"]!["hint"]));
        Assert.Equal(
            "true",
            String(
                debug.Call("source", "--last", "-c", "print order.Subtotal > 0")["data"]!["value"]
            )
        );

        var compactBatch = debug.Call(
            "source",
            "--last",
            "-c",
            "print order.Id; print order.Subtotal; print order.Id"
        );
        Assert.Equal("B200", String(compactBatch["data"]!["value"]));
        var prints = compactBatch["batch"]!["prints"]!.AsArray();
        Assert.Equal(3, prints.Count);
        Assert.Equal(1, Integer(prints[0]!["index"]));
        Assert.Equal("B200", String(prints[0]!["value"]));
        Assert.Equal("order.Subtotal", String(prints[1]!["expression"]));
        Assert.Equal("30", String(prints[1]!["value"]));
        Assert.Equal(3, Integer(prints[2]!["index"]));
        Assert.Equal("B200", String(prints[2]!["value"]));

        var tailResponse = debug.Call(
            "source",
            "--tail",
            "2",
            "-c",
            "print order.Id; print order.Subtotal; context; backtrace"
        );
        var tail = tailResponse["data"]!;
        Assert.Equal(2, tail["responses"]!.AsArray().Count);
        Assert.Equal(2, Integer(tail["omittedResponses"]));
        Assert.NotNull(tail["responses"]![0]!["data"]!["frame"]);
        Assert.NotNull(tail["responses"]![1]!["data"]!["frames"]);
        var tailPrints = tailResponse["batch"]!["prints"]!.AsArray();
        Assert.Equal(2, tailPrints.Count);
        Assert.Equal("B200", String(tailPrints[0]!["value"]));
        Assert.Equal("30", String(tailPrints[1]!["value"]));

        var failedTail = debug.CallFailure(
            "source",
            "--tail",
            "1",
            "-c",
            "print order.Id; unknown-command"
        );
        Assert.Equal(1, Integer(failedTail["data"]!["omittedResponses"]));
        Assert.Equal("B200", String(failedTail["batch"]!["prints"]![0]!["value"]));

        var manyPrints =
            string.Join("; ", Enumerable.Repeat("print order.Id", 55)) + "; print order.Subtotal";
        var boundedBatch = debug.Call("source", "--last", "-c", manyPrints);
        Assert.Equal("30", String(boundedBatch["data"]!["value"]));
        Assert.Equal(50, boundedBatch["batch"]!["prints"]!.AsArray().Count);
        Assert.Equal(6, Integer(boundedBatch["batch"]!["omittedPrints"]));
        Assert.Equal(7, Integer(boundedBatch["batch"]!["prints"]!.AsArray()[0]!["index"]));
    }

    [Fact]
    public void InvalidConditionAppearsInExitDiagnostics()
    {
        using var debug = new Debugger();
        var line = SourceLine("samples/DbgTargetCheckoutApp/Program.cs", "return total;");
        debug.Call(
            "file",
            "samples/DbgTargetCheckoutApp/DbgTargetCheckoutApp.csproj",
            "-c",
            "Debug"
        );
        debug.Call("catch", "none");
        debug.Call("break", $"samples/DbgTargetCheckoutApp/Program.cs:{line} if order.Id == B200");
        var exit = debug.Call(
            "run",
            "--cwd",
            "samples/DbgTargetCheckoutApp",
            "--wait",
            "--timeout",
            "10"
        )["data"]!;
        Assert.Equal("exited", String(exit["reason"]));
        Assert.Contains(
            exit["recentDiagnostics"]!.AsArray(),
            item => String(item!["text"]).Contains("order.Id == B200")
        );
        Assert.NotEmpty(debug.Call("events", "--kind", "diagnostic")["data"]!["events"]!.AsArray());
    }

    [Fact]
    public void DeletingLastBreakpointRemovesEngineStop()
    {
        using var debug = new Debugger();
        var line = SourceLine("samples/DbgTargetCheckoutApp/Program.cs", "return total;");
        debug.Call(
            "file",
            "samples/DbgTargetCheckoutApp/DbgTargetCheckoutApp.csproj",
            "-c",
            "Debug"
        );
        var breakpoint = debug.Call("break", $"samples/DbgTargetCheckoutApp/Program.cs:{line}");
        var firstStop = debug.Call(
            "run",
            "--env",
            "DOTDBG_DISCOUNT=0.10",
            "--cwd",
            "samples/DbgTargetCheckoutApp",
            "--wait",
            "--",
            "--tier",
            "vip"
        );
        Assert.Equal("breakpoint", String(firstStop["data"]!["reason"]));

        debug.Call("delete", String(breakpoint["data"]!["breakpointId"]));
        Assert.Empty(debug.Call("breakpoints")["data"]!["breakpoints"]!.AsArray());
        var finalStop = debug.Call("continue", "--wait", "--timeout", "10")["data"]!;
        Assert.Equal("exited", String(finalStop["reason"]));
        Assert.Equal(1, Integer(finalStop["exitCode"]));
    }

    [Fact]
    public void TickerOutputAndEventPaging()
    {
        using var debug = new Debugger();
        debug.Call("file", "samples/DbgTargetTickerApp/DbgTargetTickerApp.csproj", "-c", "Debug");
        debug.Call("run", "--", "--ticks", "4", "--delay-ms", "100");
        var stop = debug.Call("wait", "--timeout", "10")["data"]!;
        Assert.Equal("exited", String(stop["reason"]));
        Assert.Contains(
            stop["recentOutput"]!.AsArray(),
            item => String(item!["channel"]) == "stdout" && String(item["text"]) == "tick 4"
        );
        var output = debug.Call("output")["data"]!["events"]!.AsArray();
        foreach (
            var (channel, expected) in new[]
            {
                ("stdout", "tick 4"),
                ("stderr", "warning tick 4"),
                ("debug", "checkpoint 4"),
            }
        )
            Assert.Contains(
                output,
                item => String(item!["channel"]) == channel && String(item["text"]) == expected
            );
        var events = debug.Call("events", "--since", "0", "--limit", "2")["data"]!;
        Assert.True(Boolean(events["hasMore"]));
        var nextSeq = Integer(events["nextSeq"]);
        var next = debug.Call("events", "--since", nextSeq.ToString(), "--limit", "2")["data"]!;
        Assert.NotEmpty(next["events"]!.AsArray());
        Assert.True(Integer(next["events"]![0]!["seq"]) > nextSeq);
    }

    [Fact]
    public void ApphostLaunchAndTargetArguments()
    {
        using var debug = new Debugger();
        debug.Call("file", "samples/DbgTargetTickerApp/DbgTargetTickerApp.csproj", "-c", "Debug");
        var apphost =
            $"samples/DbgTargetTickerApp/bin/Debug/net10.0/DbgTargetTickerApp{(OperatingSystem.IsWindows() ? ".exe" : "")}";
        var line = SourceLine("samples/DbgTargetTickerApp/Program.cs", "BREAK_POINT") + 1;
        debug.Call("file", apphost);
        var breakpoint = debug.Call("break", $"samples/DbgTargetTickerApp/Program.cs:{line}");
        var stop = debug.Call(
            "run",
            "--wait",
            "--timeout",
            "10",
            "--",
            "--ticks",
            "2",
            "--delay-ms",
            "50"
        )["data"]!;
        Assert.Equal("breakpoint", String(stop["reason"]));
        Assert.Equal("1", Variable(debug.Call("context")["data"]!["locals"]!, "tick"));
        debug.Call("disable", String(breakpoint["data"]!["breakpointId"]));
        debug.Call("continue");
        stop = debug.Call("wait", "--timeout", "10")["data"]!;
        Assert.Equal("exited", String(stop["reason"]));
        Assert.Equal(0, Integer(stop["exitCode"]));
        Assert.Contains(
            debug.Call("output", "--channel", "stdout")["data"]!["events"]!.AsArray(),
            item => String(item!["text"]) == "tick 2"
        );
    }

    [Fact]
    public void SourceLastKeepsPrintAfterStep()
    {
        using var debug = new Debugger();
        debug.Call("file", "test/fixtures/AsyncDispatch/AsyncDispatch.csproj", "-c", "Debug");
        debug.Call("break", "test/fixtures/AsyncDispatch/Processing/DispatchCoordinator.cs:14");
        var stop = debug.Call(
            "run",
            "--cwd",
            "test/fixtures/AsyncDispatch",
            "--wait",
            "--timeout",
            "10",
            "--",
            "--retries",
            "1"
        )["data"]!;
        Assert.Equal("breakpoint", String(stop["reason"]));

        var batch = debug.Call(
            "source",
            "--last",
            "-c",
            "print options.RetryCount; next --wait; print attemptLimit"
        );
        Assert.Equal("2", String(batch["data"]!["value"]));
        var prints = batch["batch"]!["prints"]!.AsArray();
        Assert.Equal(2, prints.Count);
        Assert.Equal(1, Integer(prints[0]!["index"]));
        Assert.Equal(3, Integer(prints[1]!["index"]));
        Assert.Equal("attemptLimit", String(prints[1]!["expression"]));
        Assert.Equal("2", String(prints[1]!["value"]));
    }

    [Fact]
    public void DefaultUserExceptionModeAndAsyncFrameVariables()
    {
        using var debug = new Debugger();
        debug.Call("file", "test/fixtures/AsyncDispatch/AsyncDispatch.csproj", "-c", "Debug");
        Assert.Equal("user", String(debug.Call("catch")["data"]!["mode"]));
        debug.Call("break", "test/fixtures/AsyncDispatch/Processing/RetryPolicy.cs:22");
        debug.Call("break", "test/fixtures/AsyncDispatch/Processing/RetryPolicy.cs:29");

        var stop = debug.Call(
            "run",
            "--env",
            "DISPATCH_FAIL_ONCE=J200",
            "--cwd",
            "test/fixtures/AsyncDispatch",
            "--wait",
            "--timeout",
            "10",
            "--",
            "--retries",
            "0"
        )["data"]!;
        Assert.Equal("exception", String(stop["reason"]));
        Assert.EndsWith("SimulatedGateway.cs", String(stop["filePath"]));

        var catchBatch = debug.Call("source", "--last", "-c", "continue --wait; print attempt");
        Assert.Equal("1", String(catchBatch["data"]!["value"]));
        stop = catchBatch["batch"]!["lastStop"]!;
        Assert.Equal(1, Integer(stop["index"]));
        Assert.Equal("breakpoint", String(stop["reason"]));
        Assert.EndsWith("RetryPolicy.cs", String(stop["filePath"]));
        var errors = stop["context"]!["locals"]!
            .AsArray()
            .Where(item => String(item!["name"]) == "error")
            .ToList();
        Assert.Single(errors);
        Assert.NotEqual("null", String(errors[0]!["value"]));
        var allErrors = debug.Call("info", "locals")["data"]!["variables"]!
            .AsArray()
            .Where(item => String(item!["name"]) == "error")
            .ToList();
        Assert.Single(allErrors);

        var args = stop["context"]!["arguments"]!.AsArray();
        Assert.Contains(
            "Id = J200",
            String(args.Single(item => String(item!["name"]) == "job")!["value"])
        );
        Assert.Equal(
            "1",
            String(args.Single(item => String(item!["name"]) == "attemptLimit")!["value"])
        );
        Assert.Contains(
            debug.Call("info", "args")["data"]!["variables"]!.AsArray(),
            item => String(item!["name"]) == "attemptLimit"
        );

        var failedBatch = debug.CallFailure(
            "source",
            "--last",
            "-c",
            "continue --wait; print missingVariableForBatchTest"
        );
        Assert.Equal(2, Integer(failedBatch["data"]!["failedIndex"]));
        stop = failedBatch["batch"]!["lastStop"]!;
        Assert.Equal("breakpoint", String(stop["reason"]));
        Assert.Equal(29, Integer(stop["line"]));
        Assert.Single(
            stop["context"]!["locals"]!.AsArray().Where(item => String(item!["name"]) == "error")
        );
        Assert.Single(
            debug.Call("info", "locals")["data"]!["variables"]!
                .AsArray()
                .Where(item => String(item!["name"]) == "error")
        );
    }

    [Fact]
    public void ContinueToBreakpointPassesOtherStopsAndPreservesEvents()
    {
        using var debug = new Debugger();
        debug.Call("file", "test/fixtures/AsyncDispatch/AsyncDispatch.csproj", "-c", "Debug");
        debug.Call("break", "test/fixtures/AsyncDispatch/Gateway/SimulatedGateway.cs:18");
        debug.Call("break", "test/fixtures/AsyncDispatch/Processing/RetryPolicy.cs:29");
        debug.Call("break", "test/fixtures/AsyncDispatch/Processing/RetryPolicy.cs:24");

        var stop = debug.Call(
            "run",
            "--env",
            "DISPATCH_FAIL_ONCE=J200",
            "--cwd",
            "test/fixtures/AsyncDispatch",
            "--wait",
            "--timeout",
            "10",
            "--",
            "--retries",
            "0"
        )["data"]!;
        Assert.Equal("breakpoint", String(stop["reason"]));
        Assert.Equal(1, Integer(stop["breakpointId"]));
        debug.CallFailure("continue", "--to", "999");

        stop = debug.Call("continue", "--to", "2", "--timeout", "10")["data"]!;
        Assert.Equal("breakpoint", String(stop["reason"]));
        Assert.Equal(29, Integer(stop["line"]));
        Assert.True(Boolean(stop["targetReached"]));
        Assert.Equal(2, Integer(stop["targetBreakpointId"]));
        Assert.True(Integer(stop["skippedStops"]) >= 1);
        Assert.Contains(
            debug.Call("events", "--kind", "stop")["data"]!["events"]!.AsArray(),
            item => String(item!["reason"]) == "exception"
        );

        var exit = debug.Call("continue", "--to", "3", "--timeout", "10")["data"]!;
        Assert.Equal("exited", String(exit["reason"]));
        Assert.False(Boolean(exit["targetReached"]));
        Assert.Equal(1, Integer(exit["exitCode"]));
    }

    [Fact]
    public void StepOverAwaitedThrowIdentifiesExceptionFrame()
    {
        using var debug = new Debugger();
        debug.Call("file", "test/fixtures/AsyncDispatch/AsyncDispatch.csproj", "-c", "Debug");
        debug.Call(
            "break",
            "test/fixtures/AsyncDispatch/Processing/RetryPolicy.cs:19 if job.Id == \"J200\""
        );
        var stop = debug.Call(
            "run",
            "--env",
            "DISPATCH_FAIL_ONCE=J200",
            "--cwd",
            "test/fixtures/AsyncDispatch",
            "--wait",
            "--timeout",
            "10",
            "--",
            "--retries",
            "0"
        )["data"]!;
        Assert.Equal("breakpoint", String(stop["reason"]));

        stop = debug.Call("next")["data"]!;
        Assert.Equal("exception", String(stop["reason"]));
        Assert.EndsWith("SimulatedGateway.cs", String(stop["filePath"]));
        Assert.True(Boolean(stop["stepInterrupted"]));
        Assert.Contains("caller locals", String(stop["hint"]));
    }

    [Fact]
    public void DerivedExceptionMembersArePrintableAtFirstChanceStop()
    {
        using var debug = new Debugger();
        debug.Call("file", "test/fixtures/AsyncDispatch/AsyncDispatch.csproj", "-c", "Debug");
        debug.Call("catch", "none");
        debug.Call("break", "test/fixtures/AsyncDispatch/Gateway/SimulatedGateway.cs:18");
        var stop = debug.Call(
            "run",
            "--env",
            "DISPATCH_FAIL_ONCE=J200",
            "--cwd",
            "test/fixtures/AsyncDispatch",
            "--wait",
            "--timeout",
            "10",
            "--",
            "--retries",
            "0"
        )["data"]!;
        Assert.Equal("breakpoint", String(stop["reason"]));
        debug.Call("catch", "all");
        stop = debug.Call("continue", "--wait", "--timeout", "10")["data"]!;
        Assert.Equal("exception", String(stop["reason"]));
        Assert.False(Boolean(stop["unhandledException"]));
        Assert.Contains("First-chance exception", String(stop["hint"]));
        Assert.Equal("BUSY", String(debug.Call("print", "$exception.Code")["data"]!["value"]));
        Assert.Equal(
            "Gateway is busy for J200",
            String(debug.Call("print", "$exception.Message")["data"]!["value"])
        );
        var quoted = debug.Call(
            "source",
            "--last",
            "-c",
            "print \"$exception.Code\"; print $exception.Code"
        );
        var prints = quoted["batch"]!["prints"]!.AsArray();
        Assert.Equal(2, prints.Count);
        Assert.Equal("$exception.Code", String(prints[0]!["value"]));
        Assert.Contains("string literal", String(prints[0]!["hint"]));
        Assert.Equal("BUSY", String(prints[1]!["value"]));
        Assert.Null(prints[1]!["hint"]);
    }

    [Fact]
    public void ExceptionModesAndUnhandledState()
    {
        using var debug = new Debugger();
        debug.Call(
            "file",
            "samples/DbgTargetExceptionApp/DbgTargetExceptionApp.csproj",
            "-c",
            "Debug"
        );
        Assert.Equal("all", String(debug.Call("catch", "all")["data"]!["mode"]));
        var stop = debug.Call("run", "--wait", "--timeout", "10", "--", "handled")["data"]!;
        Assert.Equal("exception", String(stop["reason"]));
        var exception = debug.Call("info", "exception")["data"]!["exception"]!;
        Assert.Equal("InvalidOperationException", String(exception["type"]));
        Assert.Equal("order A100 was rejected", String(exception["message"]));
        Assert.DoesNotContain(
            Root,
            String(exception["stackTrace"]),
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Equal("A100", Variable(debug.Call("context")["data"]!["locals"]!, "orderId"));
        debug.Call("continue");
        Assert.Equal(0, Integer(debug.Call("wait", "--timeout", "10")["data"]!["exitCode"]));
        Assert.Equal("none", String(debug.Call("catch", "none")["data"]!["mode"]));
        stop = debug.Call("run", "--wait", "--timeout", "10", "--", "handled")["data"]!;
        Assert.Equal("exited", String(stop["reason"]));
        Assert.Equal(0, Integer(stop["exitCode"]));
        Assert.False(Boolean(stop["unhandledException"]));
        stop = debug.Call("run", "--wait", "--timeout", "10", "--", "unhandled")["data"]!;
        Assert.Equal("exited", String(stop["reason"]));
        Assert.True(Boolean(stop["unhandledException"]));
        Assert.True(
            Boolean(debug.Call("info", "status")["data"]!["status"]!["unhandledException"])
        );
        Assert.Contains(
            debug.Call("events", "--kind", "stop")["data"]!["events"]!.AsArray(),
            item =>
                String(item!["reason"]) == "exited"
                && item["unhandledException"] is not null
                && Boolean(item["unhandledException"])
        );
        Assert.Equal("unhandled", String(debug.Call("catch", "unhandled")["data"]!["mode"]));
        stop = debug.Call("run", "--wait", "--timeout", "10", "--", "handled")["data"]!;
        Assert.Equal("exited", String(stop["reason"]));
        Assert.False(Boolean(stop["unhandledException"]));
        stop = debug.Call("run", "--wait", "--timeout", "10", "--", "unhandled")["data"]!;
        Assert.Equal("exception", String(stop["reason"]));
        Assert.True(Boolean(stop["unhandledException"]));
        exception = debug.Call("info", "exception")["data"]!["exception"]!;
        Assert.Equal("Unhandled", String(exception["breakMode"]));
        Assert.Equal("order A100 was rejected", String(exception["message"]));
        Assert.Contains(
            debug.Call("events", "--kind", "stop")["data"]!["events"]!.AsArray(),
            item =>
                String(item!["reason"]) == "exception"
                && item["unhandledException"] is not null
                && Boolean(item["unhandledException"])
        );
        debug.Call("continue");
        Assert.True(Boolean(debug.Call("wait", "--timeout", "10")["data"]!["unhandledException"]));
    }

    [Fact]
    public void AttachAndDetachLeavesTargetRunning()
    {
        using (var builder = new Debugger())
            builder.Call(
                "file",
                "samples/DbgTargetTickerApp/DbgTargetTickerApp.csproj",
                "-c",
                "Debug"
            );
        var target = Path.Combine(
            Root,
            "samples/DbgTargetTickerApp/bin/Debug/net10.0/DbgTargetTickerApp.dll"
        );
        using var process = Process.Start(
            new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { target, "--ticks", "80", "--delay-ms", "200" },
            }
        )!;
        try
        {
            using var debug = new Debugger();
            debug.Call("attach", process.Id.ToString());
            var line = SourceLine("samples/DbgTargetTickerApp/Program.cs", "BREAK_POINT") + 1;
            debug.Call("break", $"samples/DbgTargetTickerApp/Program.cs:{line}");
            var stop = debug.Call("wait", "--timeout", "5")["data"]!;
            Assert.Equal("breakpoint", String(stop["reason"]));
            Assert.Equal(line, Integer(stop["line"]));
            debug.Call("detach");
            Assert.False(process.HasExited);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
    }

    private static string String(JsonNode? node) => node?.ToString() ?? "";

    private static (int ExitCode, string Stdout, string Stderr) InvokeCli(params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(
            Path.Combine(Root, $"src/DotDbg/bin/{ToolConfiguration}/net10.0/DotDbg.dll")
        );
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(45_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"dotdbg {string.Join(' ', arguments)} timed out");
        }
        return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    private static int Integer(JsonNode? node) => node!.GetValue<int>();

    private static bool Boolean(JsonNode? node) => node!.GetValue<bool>();

    private static string Variable(JsonNode variables, string name) =>
        String(variables.AsArray().Single(item => String(item!["name"]) == name)!["value"]);

    private static int SourceLine(string path, string text) =>
        Array.FindIndex(File.ReadAllLines(Path.Combine(Root, path)), line => line.Contains(text))
        + 1;

    private static string FindRoot()
    {
        for (
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory != null;
            directory = directory.Parent
        )
            if (File.Exists(Path.Combine(directory.FullName, "DotDbg.slnx")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate DotDbg.slnx");
    }

    private sealed class Debugger : IDisposable
    {
        private readonly string session = $"dotdbg-workload-{Guid.NewGuid():N}";
        private readonly string tool = Path.Combine(
            Root,
            $"src/DotDbg/bin/{ToolConfiguration}/net10.0/DotDbg.dll"
        );

        public JsonNode Call(params string[] arguments) => CallResponse(true, arguments);

        public JsonNode CallFailure(params string[] arguments) => CallResponse(false, arguments);

        private JsonNode CallResponse(bool expectedSuccess, params string[] arguments)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in new[] { tool, "-s", session, "--json" }.Concat(arguments))
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            // Decompilation can exceed a pipe buffer, so drain stdout before waiting.
            var lineTask = process.StandardOutput.ReadLineAsync();
            if (!process.WaitForExit(45_000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException($"dotdbg {string.Join(' ', arguments)} timed out");
            }
            // On Unix the daemon can inherit a pipe handle, so read one response line
            // without waiting for the pipe to close.
            var line = lineTask.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            if (line is null || (process.ExitCode == 0) != expectedSuccess)
            {
                var error = process
                    .StandardError.ReadLineAsync()
                    .WaitAsync(TimeSpan.FromMilliseconds(200));
                var diagnostic = error.IsCompletedSuccessfully ? error.Result : "";
                throw new Xunit.Sdk.XunitException(
                    $"dotdbg {string.Join(' ', arguments)} exited {process.ExitCode}: {line} {diagnostic}"
                );
            }
            var response =
                JsonNode.Parse(line) ?? throw new Xunit.Sdk.XunitException("Empty JSON response");
            Assert.True(
                Boolean(response["success"]) == expectedSuccess,
                $"dotdbg {string.Join(' ', arguments)}: {line}"
            );
            return response;
        }

        public void Dispose()
        {
            try
            {
                Call("quit");
            }
            catch
            { /* Preserve the original test failure. */
            }
        }
    }
}
