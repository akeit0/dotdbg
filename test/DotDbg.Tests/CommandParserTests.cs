using DotDbg.Cli;
using DotDbg.Engine;
using Xunit;

namespace DotDbg.Tests;

public class CommandParserTests
{
    [Theory]
    [InlineData("list Program.cs:42 --lines 20")]
    [InlineData("list --lines 20 Program.cs:42")]
    public void ListAcceptsLineCountBeforeOrAfterLocation(string command)
    {
        var result = CommandParser.ParseCommandLine(command, Environment.CurrentDirectory);

        Assert.True(result.Success);
        var list = Assert.IsType<ListCommand>(result.Request);
        Assert.Equal("Program.cs:42", list.Location);
        Assert.Equal(20, list.LineCount);
        Assert.Equal(20, list.ToJson()["lineCount"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("list Program.cs:42 --lines 0")]
    [InlineData("list Program.cs:42 --lines 101")]
    [InlineData("list Program.cs:42 --lines nope")]
    [InlineData("list Program.cs:42 --lines 10 --lines 20")]
    public void ListRejectsInvalidLineCount(string command)
    {
        var result = CommandParser.ParseCommandLine(command, Environment.CurrentDirectory);

        Assert.False(result.Success);
        Assert.Contains("list:", result.Error);
    }

    [Theory]
    [InlineData("run")]
    [InlineData("Run")]
    [InlineData("RUN")]
    [InlineData("r")]
    public void CanonicalizeCommand_IsCaseInsensitive(string raw)
    {
        Assert.Equal("run", CommandParser.CanonicalizeCommand(raw));
    }

    [Fact]
    public void ParseCommand_File_PreservesQuotedPathWithSpaces()
    {
        var result = CommandParser.ParseCommand(
            new[] { "file", @"C:\tmp\my app\App.dll" },
            @"C:\work\dotdbg"
        );

        Assert.True(result.Success);
        var file = Assert.IsType<FileCommand>(result.Request);
        Assert.Equal(@"C:\tmp\my app\App.dll", file.Path);
        Assert.Null(file.Configuration);
        Assert.Null(file.Properties);
    }

    [Fact]
    public void ParseCommand_File_CsprojWithConfigurationAndProperties()
    {
        var result = CommandParser.ParseCommand(
            new[]
            {
                "file",
                @"samples\App\App.csproj",
                "-c",
                "Release",
                "-p:Foo=Bar",
                "--property",
                "Baz=Qux",
            },
            ""
        );

        Assert.True(result.Success);
        var file = Assert.IsType<FileCommand>(result.Request);
        Assert.Equal(@"samples\App\App.csproj", file.Path);
        Assert.Equal("Release", file.Configuration);
        Assert.Equal(new[] { "Foo=Bar", "Baz=Qux" }, file.Properties);
    }

    [Fact]
    public void ParseCommand_File_RejectsBuildOptionsForDll()
    {
        var result = CommandParser.ParseCommand(new[] { "file", "App.dll", "-c", "Release" }, "");

        Assert.False(result.Success);
        Assert.Contains(".csproj or .cs", result.Error);
    }

    [Fact]
    public void ParseCommand_File_CsFileBasedAppWithConfiguration()
    {
        var result = CommandParser.ParseCommand(
            new[] { "file", "hello.cs", "-c", "Release", "-p:PublishAot=false" },
            ""
        );

        Assert.True(result.Success);
        var file = Assert.IsType<FileCommand>(result.Request);
        Assert.Equal("hello.cs", file.Path);
        Assert.Equal("Release", file.Configuration);
        Assert.Equal(new[] { "PublishAot=false" }, file.Properties);
    }

    [Fact]
    public void ParseCommand_File_RejectsInvalidProperty()
    {
        var result = CommandParser.ParseCommand(
            new[] { "file", "App.csproj", "-p", "NoEquals" },
            ""
        );

        Assert.False(result.Success);
        Assert.Contains("Name=Value", result.Error);
    }

    [Fact]
    public void FileCommand_ToJson_IncludesOptionalBuildFields()
    {
        var json = new FileCommand(
            @"C:\repo",
            "App.csproj",
            "Release",
            new[] { "Foo=Bar" }
        ).ToJson();

        Assert.Equal("file", json["op"]?.GetValue<string>());
        Assert.Equal("App.csproj", json["path"]?.GetValue<string>());
        Assert.Equal("Release", json["configuration"]?.GetValue<string>());
        Assert.Equal("Foo=Bar", json["properties"]?[0]?.GetValue<string>());
    }

    [Fact]
    public void ParseCommand_Run_PreservesDebuggeeArgWithSpaces()
    {
        var result = CommandParser.ParseCommand(new[] { "run", "arg with spaces" }, "");

        Assert.True(result.Success);
        var run = Assert.IsType<RunCommand>(result.Request);
        Assert.Single(run.Args);
        Assert.Equal("arg with spaces", run.Args[0]);
    }

    [Fact]
    public void ParseCommand_RunMinusV_PassesVToTarget()
    {
        var result = CommandParser.ParseCommand(new[] { "run", "-v" }, "");

        Assert.True(result.Success);
        var run = Assert.IsType<RunCommand>(result.Request);
        Assert.Single(run.Args);
        Assert.Equal("-v", run.Args[0]);
    }

    [Fact]
    public void ParseCommand_Run_EnvironmentDirectoryAndArguments()
    {
        var result = CommandParser.ParseCommand(
            new[]
            {
                "run",
                "--env",
                "DOTDBG_TEST=one two",
                "-e",
                "EMPTY=",
                "--cwd",
                "sub dir",
                "--",
                "--env",
                "literal",
            },
            @"C:\repo"
        );

        Assert.True(result.Success);
        var run = Assert.IsType<RunCommand>(result.Request);
        Assert.Equal("one two", run.Environment!["DOTDBG_TEST"]);
        Assert.Equal("", run.Environment["EMPTY"]);
        Assert.Equal("sub dir", run.TargetWorkingDirectory);
        Assert.Equal(new[] { "--env", "literal" }, run.Args);
        Assert.Equal("one two", run.ToJson()["env"]?["DOTDBG_TEST"]?.GetValue<string>());
        Assert.Equal("sub dir", run.ToJson()["targetCwd"]?.GetValue<string>());
    }

    [Fact]
    public void ParseCommand_RunWait_KeepsTargetOptionsAfterSeparator()
    {
        var result = CommandParser.ParseCommand(
            new[] { "run", "--wait", "--timeout", "12", "--", "--wait" },
            ""
        );

        var run = Assert.IsType<RunCommand>(result.Request);
        Assert.True(run.Wait);
        Assert.Equal(12, run.WaitTimeoutSeconds);
        Assert.Equal(new[] { "--wait" }, run.Args);
        Assert.True(run.ToJson()["wait"]?.GetValue<bool>());
    }

    [Theory]
    [InlineData("run --env")]
    [InlineData("run --env NAME")]
    [InlineData("run --env =value")]
    [InlineData("run --cwd")]
    [InlineData("run --timeout 3")]
    [InlineData("run --wait --timeout 0")]
    public void ParseCommand_Run_RejectsInvalidLaunchOptions(string line)
    {
        var result = CommandParser.ParseCommandLine(line, "");

        Assert.False(result.Success);
    }

    [Fact]
    public void ParseInvocation_GlobalVerboseBeforeRun_DoesNotPassVToTarget()
    {
        var options = CommandParser.ParseInvocation(new[] { "-v", "run" });

        Assert.True(options.JsonOutput);
        Assert.Equal(new[] { "run" }, options.CommandArgs);
    }

    [Fact]
    public void ParseInvocation_JsonOutputAlias()
    {
        var options = CommandParser.ParseInvocation(new[] { "--json", "info", "status" });

        Assert.True(options.JsonOutput);
        Assert.Equal(new[] { "info", "status" }, options.CommandArgs);
    }

    [Fact]
    public void ParseInvocation_RejectsJsonBatchWithCommand()
    {
        var error = Assert.Throws<CommandParseException>(() =>
            CommandParser.ParseInvocation(new[] { "--json-input", "commands.json", "run" })
        );

        Assert.Contains("cannot be combined", error.Message);
    }

    [Fact]
    public void ParseCommand_SourceLastKeepsInlineScript()
    {
        var result = CommandParser.ParseCommand(
            ["source", "--last", "-c", "file app.csproj; run --wait"],
            ""
        );

        Assert.True(result.Success);
        var source = Assert.IsType<SourceCommand>(result.Request);
        Assert.True(source.Inline);
        Assert.True(source.LastOnly);
        Assert.Equal("file app.csproj; run --wait", source.Script);
        Assert.True(source.ToJson()["lastOnly"]!.GetValue<bool>());
    }

    [Fact]
    public void ParseCommand_SourceTailKeepsCountAndScript()
    {
        var result = CommandParser.ParseCommand(
            ["source", "--tail", "2", "-c", "next; backtrace"],
            ""
        );

        Assert.True(result.Success);
        var source = Assert.IsType<SourceCommand>(result.Request);
        Assert.True(source.Inline);
        Assert.False(source.LastOnly);
        Assert.Equal(2, source.TailCount);
        Assert.Equal("next; backtrace", source.Script);
        Assert.Equal(2, source.ToJson()["tailCount"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("21")]
    [InlineData("oops")]
    public void ParseCommand_SourceTailRejectsInvalidCount(string count)
    {
        var result = CommandParser.ParseCommand(["source", "--tail", count, "-c", "context"], "");
        Assert.False(result.Success);
    }

    [Fact]
    public void ParseCommand_SourceRejectsBothOutputModes()
    {
        var result = CommandParser.ParseCommand(
            ["source", "--last", "--tail", "2", "-c", "context"],
            ""
        );
        Assert.False(result.Success);
    }

    [Theory]
    [InlineData("context")]
    [InlineData("ctx")]
    public void ParseCommand_Context(string command)
    {
        var result = CommandParser.ParseCommand(new[] { command }, "");

        Assert.True(result.Success);
        Assert.IsType<ContextCommand>(result.Request);
    }

    [Theory]
    [InlineData("wait --timeout 1", 1)]
    [InlineData("wait --timeout 3600", 3600)]
    public void ParseCommand_WaitTimeout(string line, int seconds)
    {
        var result = CommandParser.ParseCommandLine(line, "");

        Assert.True(result.Success);
        var wait = Assert.IsType<WaitCommand>(result.Request);
        Assert.Equal(seconds, wait.TimeoutSeconds);
        Assert.Equal(seconds, wait.ToJson()["timeoutSeconds"]?.GetValue<int>());
    }

    [Theory]
    [InlineData("wait --timeout 0")]
    [InlineData("wait --timeout 3601")]
    [InlineData("wait --timeout abc")]
    [InlineData("wait --timeout")]
    [InlineData("wait extra")]
    public void ParseCommand_WaitRejectsInvalidTimeout(string line)
    {
        var result = CommandParser.ParseCommandLine(line, "");

        Assert.False(result.Success);
        Assert.Contains("--timeout", result.Error);
    }

    [Fact]
    public void ParseInvocation_GlobalOptionsStopAtCommand()
    {
        var options = CommandParser.ParseInvocation(new[] { "-v", "run", "-v" });

        Assert.True(options.JsonOutput);
        Assert.Equal(new[] { "run", "-v" }, options.CommandArgs);
    }

    [Fact]
    public void ParseInvocation_SessionIdWithoutValue_Throws()
    {
        var ex = Assert.Throws<CommandParseException>(() =>
            CommandParser.ParseInvocation(new[] { "-s" })
        );
        Assert.Contains("Missing value", ex.Message);
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("delete abc")]
    [InlineData("delete 1 2")]
    public void ParseCommand_Delete_WithMissingOrInvalidId_Fails(string line)
    {
        var args = CommandLineTokenizer.Tokenize(line);
        var result = CommandParser.ParseCommand(args, "");

        Assert.False(result.Success);
        Assert.Contains("delete: missing or invalid breakpoint id", result.Error);
    }

    [Fact]
    public void ParseCommand_Delete_WithValidId_Succeeds()
    {
        var result = CommandParser.ParseCommand(new[] { "delete", "5" }, "");

        Assert.True(result.Success);
        var delete = Assert.IsType<DeleteCommand>(result.Request);
        Assert.Equal(5, delete.BreakpointId);
    }

    [Fact]
    public void ParseCommand_Thread_WithInvalidId_Fails()
    {
        var result = CommandParser.ParseCommand(new[] { "thread", "abc" }, "");

        Assert.False(result.Success);
        Assert.Contains("thread: invalid thread id", result.Error);
    }

    [Fact]
    public void ParseCommand_Backtrace_WithInvalidThreadId_Fails()
    {
        var result = CommandParser.ParseCommand(new[] { "backtrace", "--thread", "abc" }, "");

        Assert.False(result.Success);
        Assert.Contains("backtrace: invalid thread id", result.Error);
    }

    [Fact]
    public void ParseCommand_Backtrace_AllAndThread_Fails()
    {
        var result = CommandParser.ParseCommand(new[] { "backtrace", "all", "--thread", "7" }, "");

        Assert.False(result.Success);
        Assert.Contains("backtrace: 'all' and '--thread' cannot be combined", result.Error);
    }

    [Fact]
    public void ParseCommand_Backtrace_WithThread_Succeeds()
    {
        var result = CommandParser.ParseCommand(new[] { "backtrace", "--thread", "7" }, "");

        Assert.True(result.Success);
        var bt = Assert.IsType<BacktraceCommand>(result.Request);
        Assert.False(bt.All);
        Assert.Equal(7, bt.ThreadId);
    }

    [Fact]
    public void ParseCommand_Break_WithIfCondition_SplitsLocationAndCondition()
    {
        var result = CommandParser.ParseCommand(
            new[] { "break", "Program.cs:26", "if", "n", ">", "1" },
            ""
        );

        Assert.True(result.Success);
        var bp = Assert.IsType<BreakCommand>(result.Request);
        Assert.Equal("Program.cs:26", bp.Location);
        Assert.Equal("n > 1", bp.Condition);
    }

    [Fact]
    public void ParseCommand_Break_ConditionInSingleQuotedToken_Splits()
    {
        var result = CommandParser.ParseCommandLine("break \"Program.cs:26 if n > 1\"", "");

        Assert.True(result.Success);
        var bp = Assert.IsType<BreakCommand>(result.Request);
        Assert.Equal("Program.cs:26", bp.Location);
        Assert.Equal("n > 1", bp.Condition);
    }

    [Theory]
    [InlineData("condition 1 order.Id == \"B200\"")]
    [InlineData("condition 1 order.Id == \\\"B200\\\"")]
    public void ParseScriptCommandLine_ConditionPreservesStringLiteral(string line)
    {
        var result = CommandParser.ParseScriptCommandLine(line, "");

        Assert.True(result.Success);
        var condition = Assert.IsType<ConditionCommand>(result.Request);
        Assert.Equal("order.Id == \"B200\"", condition.Condition);
    }

    [Fact]
    public void ParseScriptCommandLine_BreakPreservesStringLiteral()
    {
        var result = CommandParser.ParseScriptCommandLine(
            "break Program.cs:26 if order.Id == \"B200\"",
            ""
        );

        Assert.True(result.Success);
        var breakpoint = Assert.IsType<BreakCommand>(result.Request);
        Assert.Equal("Program.cs:26", breakpoint.Location);
        Assert.Equal("order.Id == \"B200\"", breakpoint.Condition);
    }

    [Fact]
    public void ParseScriptCommandLine_BreakAcceptsNameAfterCondition()
    {
        var result = CommandParser.ParseScriptCommandLine(
            "break Program.cs:26 if order.Id == \"B200\" --name targetOrder",
            ""
        );

        Assert.True(result.Success, result.Error);
        var breakpoint = Assert.IsType<BreakCommand>(result.Request);
        Assert.Equal("order.Id == \"B200\"", breakpoint.Condition);
        Assert.Equal("targetOrder", breakpoint.Name);
    }

    [Fact]
    public void ParseScriptCommandLine_BreakKeepsNameWordInsideConditionLiteral()
    {
        var result = CommandParser.ParseScriptCommandLine(
            "break Program.cs:26 if order.Id == \"--name\" --name targetOrder",
            ""
        );

        Assert.True(result.Success, result.Error);
        var breakpoint = Assert.IsType<BreakCommand>(result.Request);
        Assert.Equal("order.Id == \"--name\"", breakpoint.Condition);
        Assert.Equal("targetOrder", breakpoint.Name);
    }

    [Fact]
    public void ParseScriptCommandLine_PrintPreservesStringLiteral()
    {
        var result = CommandParser.ParseScriptCommandLine("print order.Id == \"B200\"", "");

        Assert.True(result.Success);
        Assert.Equal(
            "order.Id == \"B200\"",
            Assert.IsType<PrintCommand>(result.Request).Expression
        );
    }

    [Theory]
    [InlineData("print \"hello\"", "\"hello\"")]
    [InlineData("print 'x'", "'x'")]
    [InlineData("print @\"a\"\"b\"", "@\"a\"\"b\"")]
    [InlineData("print $\"item: {n}\"", "$\"item: {n}\"")]
    [InlineData("print order.Id == \\\"B200\\\"", "order.Id == \"B200\"")]
    public void ParseScriptCommandLine_PreservesCSharpExpression(string line, string expression)
    {
        var result = CommandParser.ParseScriptCommandLine(line, "");

        Assert.True(result.Success, result.Error);
        Assert.Equal(expression, Assert.IsType<PrintCommand>(result.Request).Expression);
    }

    [Theory]
    [InlineData("trace Program.cs:26 message + \": \" + n", "message + \": \" + n", null)]
    [InlineData(
        "trace Program.cs:26 message + \": \" + n --name progress",
        "message + \": \" + n",
        "progress"
    )]
    [InlineData(
        "trace Program.cs:26 message + \" --name \" + n --name progress",
        "message + \" --name \" + n",
        "progress"
    )]
    [InlineData("trace Program.cs:26 \"hello\"", "\"hello\"", null)]
    [InlineData("trace Program.cs:26 'x'", "'x'", null)]
    public void ParseScriptCommandLine_TracePreservesCSharpExpression(
        string line,
        string expression,
        string? name
    )
    {
        var result = CommandParser.ParseScriptCommandLine(line, "");

        Assert.True(result.Success, result.Error);
        var trace = Assert.IsType<TraceCommand>(result.Request);
        Assert.Equal(name, trace.Name);
        Assert.Equal(expression, trace.Expression);
    }

    [Fact]
    public void ParseScriptCommandLine_TraceAcceptsQuotedLocation()
    {
        var result = CommandParser.ParseScriptCommandLine(
            "trace \"My Folder/Program.cs:26\" message + \": \" + n --name progress",
            ""
        );

        Assert.True(result.Success, result.Error);
        var trace = Assert.IsType<TraceCommand>(result.Request);
        Assert.Equal("My Folder/Program.cs:26", trace.Location);
        Assert.Equal("message + \": \" + n", trace.Expression);
        Assert.Equal("progress", trace.Name);
    }

    [Fact]
    public void ParseScriptCommandLine_TraceRejectsNameWithoutExpression()
    {
        var result = CommandParser.ParseScriptCommandLine(
            "trace Program.cs:26 --name progress",
            ""
        );

        Assert.False(result.Success);
        Assert.Contains("missing expression", result.Error);
    }

    [Fact]
    public void RunEnvironmentNamesUsePlatformComparison()
    {
        var result = CommandParser.ParseCommand(
            ["run", "--env", "DOTDBG_CASE=upper", "--env", "dotdbg_case=lower"],
            ""
        );

        Assert.True(result.Success, result.Error);
        var environment = Assert.IsType<RunCommand>(result.Request).Environment!;
        Assert.Equal(OperatingSystem.IsWindows() ? 1 : 2, environment.Count);
        Assert.Equal(
            "lower",
            environment[OperatingSystem.IsWindows() ? "DOTDBG_CASE" : "dotdbg_case"]
        );
        Assert.Equal(environment.Count, result.Request!.ToJson()["env"]!.AsObject().Count);
    }

    [Fact]
    public void InfoTraceParsesBoundedPage()
    {
        var result = CommandParser.ParseCommandLine("info trace --since 200 --limit 50", "");

        Assert.True(result.Success, result.Error);
        var info = Assert.IsType<InfoCommand>(result.Request);
        Assert.Equal("trace", info.Subject);
        Assert.Equal(200, info.Since);
        Assert.Equal(50, info.Limit);
        Assert.Equal(200, info.ToJson()["since"]!.GetValue<long>());
    }

    [Fact]
    public void ParseStep_AcceptsRedundantWaitOption()
    {
        var result = CommandParser.ParseCommandLine("next --wait", "");

        Assert.True(result.Success);
        Assert.Equal(StepKind.Next, Assert.IsType<StepCommand>(result.Request).Kind);
    }

    [Fact]
    public void ParseCommand_Break_WithNameAndCondition_PreservesName()
    {
        var result = CommandParser.ParseCommand(
            new[] { "break", "Program.cs:26", "--name", "mainLoop", "if", "n > 1" },
            ""
        );

        Assert.True(result.Success);
        var bp = Assert.IsType<BreakCommand>(result.Request);
        Assert.Equal("Program.cs:26", bp.Location);
        Assert.Equal("mainLoop", bp.Name);
        Assert.Equal("n > 1", bp.Condition);
    }

    [Fact]
    public void ParseCommand_Break_AcceptsNameAfterCondition()
    {
        var result = CommandParser.ParseCommand(
            new[] { "break", "Program.cs:26", "if", "n", ">", "1", "--name", "mainLoop" },
            ""
        );

        Assert.True(result.Success, result.Error);
        var breakpoint = Assert.IsType<BreakCommand>(result.Request);
        Assert.Equal("n > 1", breakpoint.Condition);
        Assert.Equal("mainLoop", breakpoint.Name);
    }

    [Theory]
    [InlineData("break Program.cs:26 if --name mainLoop")]
    [InlineData("break Program.cs:26 if n > 1 --name")]
    public void ParseCommand_Break_RejectsIncompleteConditionOrName(string line)
    {
        var result = CommandParser.ParseCommandLine(line, "");

        Assert.False(result.Success);
    }

    [Fact]
    public void ParseCommand_Trace_WithName_PreservesName()
    {
        var result = CommandParser.ParseCommand(
            new[] { "trace", "Program.cs:26", "n", "--name", "loopCount" },
            ""
        );

        Assert.True(result.Success);
        var tp = Assert.IsType<TraceCommand>(result.Request);
        Assert.Equal("Program.cs:26", tp.Location);
        Assert.Equal("n", tp.Expression);
        Assert.Equal("loopCount", tp.Name);
    }

    [Fact]
    public void ParseCommand_ClearLocation_ErrorsClearly()
    {
        var result = CommandParser.ParseCommand(new[] { "clear", "Program.cs:10" }, "");

        Assert.False(result.Success);
        Assert.Contains("missing or invalid breakpoint id", result.Error);
    }

    [Fact]
    public void ParseCommand_Catch_ReturnsCatchCommand()
    {
        var result = CommandParser.ParseCommand(new[] { "catch" }, "");

        Assert.True(result.Success);
        Assert.Null(Assert.IsType<CatchCommand>(result.Request).Mode);
        Assert.Equal(
            "none",
            Assert
                .IsType<CatchCommand>(
                    CommandParser.ParseCommand(new[] { "catch", "none" }, "").Request
                )
                .Mode
        );
        var unhandled = CommandParser.ParseCommand(new[] { "catch", "unhandled" }, "");
        Assert.True(unhandled.Success);
        Assert.Equal("unhandled", Assert.IsType<CatchCommand>(unhandled.Request).Mode);
        var user = CommandParser.ParseCommand(new[] { "catch", "user" }, "");
        Assert.True(user.Success);
        Assert.Equal("user", Assert.IsType<CatchCommand>(user.Request).Mode);
        Assert.False(CommandParser.ParseCommand(new[] { "catch", "invalid" }, "").Success);
    }

    [Fact]
    public void ParseCommand_InfoSubjectAliases_Canonicalize()
    {
        var result = CommandParser.ParseCommand(new[] { "info", "b" }, "");

        Assert.True(result.Success);
        var info = Assert.IsType<InfoCommand>(result.Request);
        Assert.Equal("breakpoints", info.Subject);
    }

    [Fact]
    public void ParseCommand_HelpSubject_CanonicalizesAliases()
    {
        var result = CommandParser.ParseCommand(new[] { "help", "p" }, "");

        Assert.True(result.Success);
        var help = Assert.IsType<HelpCommand>(result.Request);
        Assert.Equal("print", help.Subject);
    }

    [Fact]
    public void ParseCommand_Run_WithNoJmc_SetsJustMyCodeFalse()
    {
        var result = CommandParser.ParseCommand(new[] { "run", "--no-jmc", "arg1" }, "");

        Assert.True(result.Success);
        var run = Assert.IsType<RunCommand>(result.Request);
        Assert.False(run.JustMyCode);
        Assert.Equal(new[] { "arg1" }, run.Args);
    }

    [Fact]
    public void ParseCommand_Run_WithSeparator_PreservesOptionsAfterDash()
    {
        var result = CommandParser.ParseCommand(new[] { "run", "--", "--no-jmc" }, "");

        Assert.True(result.Success);
        var run = Assert.IsType<RunCommand>(result.Request);
        Assert.True(run.JustMyCode);
        Assert.Equal(new[] { "--no-jmc" }, run.Args);
    }

    [Fact]
    public void ParseCommand_UnknownCommand_Fails()
    {
        var result = CommandParser.ParseCommand(new[] { "foo" }, "");

        Assert.False(result.Success);
        Assert.Contains("Unknown command", result.Error);
    }

    [Theory]
    [InlineData("delete -1")]
    [InlineData("delete 0")]
    [InlineData("delete abc")]
    public void ParseCommand_Delete_InvalidId_Fails(string line)
    {
        var args = CommandLineTokenizer.Tokenize(line);
        var result = CommandParser.ParseCommand(args, "");

        Assert.False(result.Success);
        Assert.Contains("delete: missing or invalid breakpoint id", result.Error);
    }

    [Theory]
    [InlineData("thread 0")]
    [InlineData("thread -1")]
    public void ParseCommand_Thread_InvalidId_Fails(string line)
    {
        var args = CommandLineTokenizer.Tokenize(line);
        var result = CommandParser.ParseCommand(args, "");

        Assert.False(result.Success);
        Assert.Contains("thread: invalid thread id", result.Error);
    }

    [Fact]
    public void ParseCommand_Frame_NegativeIndex_Fails()
    {
        var result = CommandParser.ParseCommand(new[] { "frame", "-1" }, "");

        Assert.False(result.Success);
        Assert.Contains("frame: invalid frame index", result.Error);
    }

    [Fact]
    public void ParseCommand_Backtrace_ThreadZero_Fails()
    {
        var result = CommandParser.ParseCommand(new[] { "backtrace", "--thread", "0" }, "");

        Assert.False(result.Success);
        Assert.Contains("backtrace: invalid thread id", result.Error);
    }

    [Theory]
    [InlineData("ignore 1 -5")]
    [InlineData("ignore 1 abc")]
    public void ParseCommand_Ignore_InvalidCount_Fails(string line)
    {
        var args = CommandLineTokenizer.Tokenize(line);
        var result = CommandParser.ParseCommand(args, "");

        Assert.False(result.Success);
        Assert.Contains("ignore: missing or invalid count", result.Error);
    }

    [Theory]
    [InlineData("break Program.cs:0")]
    [InlineData("break Program.cs:-1")]
    [InlineData("break Program.cs:1:0")]
    public void ParseCommand_Break_InvalidLineOrColumn_Fails(string line)
    {
        var args = CommandLineTokenizer.Tokenize(line);
        var result = CommandParser.ParseCommand(args, "");

        Assert.False(result.Success);
        Assert.Contains("invalid location", result.Error);
    }

    [Fact]
    public void ParseCommand_Attach_ExtraArgs_Fails()
    {
        var result = CommandParser.ParseCommand(new[] { "attach", "123", "extra" }, "");

        Assert.False(result.Success);
        Assert.Contains("attach: takes exactly one target", result.Error);
    }

    [Fact]
    public void ParseCommand_Decompile_Args_Fails()
    {
        var result = CommandParser.ParseCommand(new[] { "decompile", "something" }, "");

        Assert.False(result.Success);
        Assert.Contains("decompile: only --il is supported", result.Error);
    }

    [Fact]
    public void ParseCommand_Decompile_Il_Succeeds()
    {
        var result = CommandParser.ParseCommand(new[] { "decompile", "--il" }, "");

        Assert.True(result.Success);
        var dec = Assert.IsType<DecompileCommand>(result.Request);
        Assert.True(dec.IlMode);
    }

    [Fact]
    public void ParseCommand_InfoBps_CanonicalizesToBreakpoints()
    {
        var result = CommandParser.ParseCommand(new[] { "info", "bps" }, "");

        Assert.True(result.Success);
        var info = Assert.IsType<InfoCommand>(result.Request);
        Assert.Equal("breakpoints", info.Subject);
    }

    [Fact]
    public void ParseCommand_BreakpointsBps_Succeeds()
    {
        var result = CommandParser.ParseCommand(new[] { "bps" }, "");

        Assert.True(result.Success);
        Assert.IsType<BreakpointsCommand>(result.Request);
    }

    [Fact]
    public void ParseCommand_BreakIl_Succeeds()
    {
        var result = CommandParser.ParseCommand(
            new[] { "break", "--il", "Program.Main:IL_0000" },
            ""
        );

        Assert.True(result.Success);
        var bp = Assert.IsType<BreakCommand>(result.Request);
        Assert.True(bp.IlMode);
        Assert.Equal("Program.Main:IL_0000", bp.Location);
    }

    [Fact]
    public void ParseCommand_BreakIl_WithModule_Succeeds()
    {
        var result = CommandParser.ParseCommand(
            new[] { "break", "--il", "DbgTargetSampleApp.dll!Program.Main:0x0" },
            ""
        );

        Assert.True(result.Success);
        var bp = Assert.IsType<BreakCommand>(result.Request);
        Assert.True(bp.IlMode);
        Assert.Equal("Program.Main:IL_0000", bp.Location);
        Assert.Equal("DbgTargetSampleApp.dll", bp.ModuleName);
    }

    [Fact]
    public void ParseCommand_BreakIl_InvalidOffset_Fails()
    {
        var result = CommandParser.ParseCommand(new[] { "break", "--il", "Program.Main:abc" }, "");

        Assert.False(result.Success);
        Assert.Contains("invalid IL offset", result.Error);
    }

    [Theory]
    [InlineData("Program.Main:IL_0000", 0)]
    [InlineData("Program.Main:IL_0009", 9)]
    [InlineData("Program.Main:IL_000A", 10)]
    [InlineData("Program.Main:IL_0010", 16)]
    [InlineData("Program.Main:0x1f", 31)]
    [InlineData("Program.Main:31", 31)]
    public async Task ParseCommand_BreakIl_RoundTripsThroughSession(string location, int offset)
    {
        var parsed = CommandParser.ParseCommand(new[] { "break", "--il", location }, "");
        Assert.True(parsed.Success);
        var command = Assert.IsType<BreakCommand>(parsed.Request);
        Assert.Equal($"Program.Main:IL_{offset:X4}", command.Location);

        using var session = new DebugSession();
        var response = await session.HandleOperationAsync(command.ToJson(), CancellationToken.None);
        Assert.True(response["success"]!.GetValue<bool>());
        var breakpoints = await session.HandleOperationAsync(
            new System.Text.Json.Nodes.JsonObject { ["op"] = "breakpoints" },
            CancellationToken.None
        );
        Assert.Equal(offset, breakpoints["data"]!["breakpoints"]![0]!["ilOffset"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("Program.Main:IL_001G")]
    [InlineData("Program.Main:IL_FFFFFFFF")]
    [InlineData("Program.Main:0x80000000")]
    [InlineData("Program.Main:-1")]
    [InlineData("Program.Main:IL_")]
    public void ParseCommand_BreakIl_RejectsInvalidOrOverflowingOffsets(string location)
    {
        var result = CommandParser.ParseCommand(new[] { "break", "--il", location }, "");
        Assert.False(result.Success);
        Assert.Contains("invalid IL offset", result.Error);
    }

    [Fact]
    public void ParseInvocation_DaemonWithCommand_Throws()
    {
        var ex = Assert.Throws<CommandParseException>(() =>
            CommandParser.ParseInvocation(new[] { "--daemon", "run" })
        );

        Assert.Contains("--daemon cannot be combined with a command", ex.Message);
    }

    [Fact]
    public void ParseCommand_Run_PreservesEmptyQuotedArg()
    {
        var args = CommandLineTokenizer.Tokenize("run \"\" arg2");
        var result = CommandParser.ParseCommand(args, "");

        Assert.True(result.Success);
        var run = Assert.IsType<RunCommand>(result.Request);
        Assert.Equal(new[] { "", "arg2" }, run.Args);
    }

    [Fact]
    public void ParseCommand_Schema_ReturnsSchemaCommand()
    {
        var result = CommandParser.ParseCommand(new[] { "schema" }, "");

        Assert.True(result.Success);
        Assert.IsType<SchemaCommand>(result.Request);
    }

    [Fact]
    public void GetSchema_IncludesBreakAndRunCommands()
    {
        var schema = CommandSchema.GetSchema();

        var commands = schema["commands"] as System.Text.Json.Nodes.JsonArray;
        Assert.NotNull(commands);
        var names = commands!.Select(c => (c!["name"]?.GetValue<string>())!).ToHashSet();
        Assert.Contains("break", names);
        Assert.Contains("run", names);
        Assert.Contains("schema", names);
    }

    [Fact]
    public void GetSchema_BreakIncludesAliasesAndArguments()
    {
        var schema = CommandSchema.GetSchema();
        var commands = schema["commands"] as System.Text.Json.Nodes.JsonArray;
        Assert.NotNull(commands);
        var breakCommand = commands!
            .OfType<System.Text.Json.Nodes.JsonObject>()
            .First(c => c["name"]?.GetValue<string>() == "break");

        var aliases = breakCommand["aliases"] as System.Text.Json.Nodes.JsonArray;
        Assert.NotNull(aliases);
        Assert.Contains("b", aliases!.Select(a => a!.GetValue<string>()));

        var args = breakCommand["arguments"] as System.Text.Json.Nodes.JsonArray;
        Assert.NotNull(args);
        var argNames = args!.Select(a => a!["name"]?.GetValue<string>()).ToList();
        Assert.Contains("location", argNames);
        Assert.Contains("name", argNames);
        Assert.Contains("condition", argNames);
    }

    [Fact]
    public void GetSchema_AllAdvertisedCommandsAndAliasesAreRecognized()
    {
        var commands = CommandSchema.GetSchema()["commands"]!.AsArray();
        foreach (var command in commands)
        {
            var name = command!["name"]!.GetValue<string>();
            foreach (var alias in command["aliases"]!.AsArray())
            {
                var token = alias!.GetValue<string>();
                var result = CommandParser.ParseCommand(new[] { token }, "");
                Assert.DoesNotContain("Unknown command:", result.Error ?? string.Empty);
                Assert.Equal(name, CommandParser.CanonicalizeCommand(token));
            }
        }
    }

    [Fact]
    public void ParseCommand_OutputAndEventsQueries()
    {
        var output = CommandParser.ParseCommandLine(
            "output --since 12 --limit 5 --channel stderr",
            ""
        );
        Assert.True(output.Success);
        Assert.Equal(new OutputCommand("", 12, 5, "stderr"), output.Request);

        var events = CommandParser.ParseCommandLine("events --since 12 --limit 5 --kind trace", "");
        Assert.True(events.Success);
        Assert.Equal(new EventsCommand("", 12, 5, "trace"), events.Request);
    }

    [Fact]
    public void ParseCommand_ContinueCanWaitForStop()
    {
        var result = CommandParser.ParseCommandLine("continue --wait --timeout 10", "");

        Assert.True(result.Success);
        Assert.Equal(new ContinueCommand("", true, 10), result.Request);
        var request = result.Request!.ToJson();
        Assert.True(request["wait"]!.GetValue<bool>());
        Assert.Equal(10, request["timeoutSeconds"]!.GetValue<int>());
    }

    [Fact]
    public void ParseCommand_ContinueCanTargetBreakpoint()
    {
        var result = CommandParser.ParseCommandLine("continue --to 3 --timeout 10", "");

        Assert.True(result.Success);
        Assert.Equal(new ContinueCommand("", true, 10, 3), result.Request);
        Assert.Equal(3, result.Request!.ToJson()["targetBreakpointId"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("continue --timeout 10")]
    [InlineData("continue --wait --timeout 0")]
    [InlineData("continue --wait extra")]
    [InlineData("continue --to 0")]
    [InlineData("continue --to 1 --to 2")]
    public void ParseCommand_ContinueRejectsInvalidOptions(string line)
    {
        Assert.False(CommandParser.ParseCommandLine(line, "").Success);
    }

    [Theory]
    [InlineData("output --channel other")]
    [InlineData("output --limit 0")]
    [InlineData("events --since -1")]
    [InlineData("events --channel stdout")]
    [InlineData("events --kind command")]
    public void ParseCommand_EventQueryRejectsInvalidOptions(string line)
    {
        Assert.False(CommandParser.ParseCommandLine(line, "").Success);
    }
}
