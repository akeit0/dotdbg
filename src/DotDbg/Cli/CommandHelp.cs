using System.Text.Json.Nodes;

namespace DotDbg.Cli;

internal static class CommandHelp
{
    internal static void PrintCommandHelp(string subject) => WriteCommandHelp(subject, Console.Out);

    internal static string RenderCommandHelp(string subject)
    {
        using var writer = new StringWriter();
        WriteCommandHelp(subject, writer);
        return writer.ToString().TrimEnd('\r', '\n');
    }

    private static void WriteCommandHelp(string subject, TextWriter writer)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            WriteHelp(writer);
            return;
        }

        subject = CommandParser.CanonicalizeCommand(subject);
        switch (subject)
        {
            case "all" or "--all" or "commands":
                writer.WriteLine("Command syntax (use 'help <command>' for details):");
                foreach (var entry in CommandSchema.GetSchema()["commands"]!.AsArray())
                    writer.WriteLine(entry?["usage"]?.GetValue<string>());
                break;
            case "powershell" when OperatingSystem.IsWindows():
                PrintPowerShellHelp(writer);
                break;
            case "print":
                writer.WriteLine("print <expr>");
                writer.WriteLine();
                writer.WriteLine("Evaluate a C# expression in the selected stopped frame.");
                writer.WriteLine("Locals, parameters, and visible members are available.");
                writer.WriteLine(
                    "At an exception stop, $exception exposes members of its runtime type."
                );
                writer.WriteLine(
                    "A breakpoint on a throw line stops before the throw; continue to inspect $exception."
                );
                writer.WriteLine("Use m for decimal literals: 7.00m is decimal; 7.00 is double.");
                writer.WriteLine(
                    "A quoted expression such as \"hello\" is a string literal, not a comparison."
                );
                writer.WriteLine(
                    "Inside source -c, write print attempt < attemptLimit without inner quotes."
                );
                writer.WriteLine();
                writer.WriteLine("Examples:");
                writer.WriteLine("  print n");
                writer.WriteLine("  print '$exception.Code'");
                writer.WriteLine("  print \"subtotal + 7.00m\"  (decimal literal)");
                writer.WriteLine("For several expressions in one call, see 'help source'.");
                break;
            case "watch" or "unwatch" or "watches":
                writer.WriteLine("watch <expr>");
                writer.WriteLine("unwatch <id>");
                writer.WriteLine("watches");
                writer.WriteLine();
                writer.WriteLine(
                    "Manage expressions that are re-evaluated every time the debuggee stops."
                );
                writer.WriteLine("Changed values are marked with '*' in the stop output.");
                writer.WriteLine("A new watch has no value until the next stop.");
                writer.WriteLine();
                writer.WriteLine("Examples:");
                writer.WriteLine("  watch n");
                writer.WriteLine("  watch n * 2");
                writer.WriteLine("  unwatch 1");
                writer.WriteLine("  watches");
                break;
            case "trace-log":
                writer.WriteLine("trace-log [file]");
                writer.WriteLine("trace-log --clear");
                writer.WriteLine();
                writer.WriteLine(
                    "Append all tracepoint output to a file. Without a file argument,"
                );
                writer.WriteLine("trace logging is disabled. Use --clear to empty the file.");
                writer.WriteLine("Use 'info trace' to read the latest 100 lines.");
                break;
            case "break" or "b":
                writer.WriteLine("break [--il] <location> [--name <name>] [if <condition>]");
                writer.WriteLine();
                writer.WriteLine(
                    "Set a breakpoint at file:line[:column]. An if condition stops only when true."
                );
                writer.WriteLine(
                    "--name may appear before or after if. Use ignore <id> <count> to skip hits."
                );
                writer.WriteLine(
                    "The stop is before the line executes; next shows an assignment's result."
                );
                writer.WriteLine(
                    "Multi-line statements may bind earlier; breakpoints shows boundLine."
                );
                writer.WriteLine(
                    "Co-located source breakpoints cannot combine conditions or ignore counts."
                );
                writer.WriteLine("--il uses Type.Method:IL_0000 or module.dll!Type.Method:0x1f.");
                writer.WriteLine("IL_ and 0x offsets are hex; bare offsets are decimal.");
                writer.WriteLine();
                writer.WriteLine("Examples:");
                writer.WriteLine("  break Program.cs:26");
                writer.WriteLine(
                    "  source -c 'break Program.cs:20 if parcel.Id == \"P200\" --name p200'"
                );
                writer.WriteLine("  break --il Program.Main:IL_0000");
                break;
            case "trace" or "tp":
                writer.WriteLine("trace <location> <expression> [--name <name>]");
                writer.WriteLine();
                writer.WriteLine("Set a tracepoint that evaluates the expression when hit.");
                writer.WriteLine("If no regular breakpoint shares the location, execution");
                writer.WriteLine("auto-continues.");
                writer.WriteLine("The value appears as a trace event and in the trace log.");
                writer.WriteLine(
                    "A waiting command can return the trace result; execution then resumes."
                );
                writer.WriteLine("Use --name to give the tracepoint a label.");
                writer.WriteLine(
                    "Use 'trace-log' to write to a file and 'info trace' to read the log."
                );
                writer.WriteLine();
                writer.WriteLine("Examples:");
                writer.WriteLine("  trace Program.cs:26 n");
                writer.WriteLine("  trace Program.cs:26 \"n + 1\"");
                writer.WriteLine("  trace Program.cs:26 n --name loopCount");
                writer.WriteLine(
                    "String + number concatenates without a separator; include one in the expression."
                );
                break;
            case "condition" or "cond":
                writer.WriteLine("condition <bp> [expression]");
                writer.WriteLine();
                writer.WriteLine("Set or clear the condition for breakpoint <bp>.");
                writer.WriteLine("Without an expression, the condition is cleared.");
                writer.WriteLine("Check the stored expression with breakpoints.");
                writer.WriteLine();
                writer.WriteLine("Examples:");
                writer.WriteLine("  condition 1 'parcel.Id == \"P200\"'");
                writer.WriteLine("  condition 1");
                break;
            case "ignore" or "ign":
                writer.WriteLine("ignore <bp> <count>");
                writer.WriteLine();
                writer.WriteLine("Skip the first <count> hits of breakpoint <bp>.");
                writer.WriteLine("Use 0 to clear the hit count.");
                writer.WriteLine();
                writer.WriteLine("Examples:");
                writer.WriteLine("  ignore 1 5");
                writer.WriteLine("  ignore 1 0");
                break;
            case "attach":
                writer.WriteLine("attach [--no-jmc] <pid|name>");
                writer.WriteLine();
                writer.WriteLine("Attach to a running .NET process by process ID or process name.");
                writer.WriteLine("If a name matches multiple processes, the command lists them");
                writer.WriteLine("and you can retry with a specific PID.");
                writer.WriteLine("Use --no-jmc to allow stepping into non-user code.");
                writer.WriteLine();
                writer.WriteLine("Examples:");
                writer.WriteLine("  attach 12345");
                writer.WriteLine("  attach dotnet");
                writer.WriteLine("  attach --no-jmc 12345");
                break;
            case "run" or "r":
                writer.WriteLine(
                    "run [--no-jmc] [--env NAME=VALUE]... [--cwd PATH] [--wait [--timeout SECONDS]] [--] [args...]"
                );
                writer.WriteLine();
                writer.WriteLine("Launch the program previously loaded with 'file'.");
                writer.WriteLine(
                    "Recognized options before -- configure dotdbg; other arguments go to the target."
                );
                writer.WriteLine(
                    "Use -- before target arguments that match dotdbg options, such as --env."
                );
                writer.WriteLine(
                    "--env overrides a target environment variable; repeat as needed."
                );
                writer.WriteLine("--cwd sets the target's working directory.");
                writer.WriteLine(
                    "--wait returns the first stop, trace result, or exit; default timeout is 30 seconds."
                );
                writer.WriteLine("Use --no-jmc to allow stepping into non-user code.");
                writer.WriteLine("Example: run --env APP_MODE=debug --wait -- --plan plus");
                break;
            case "file":
                writer.WriteLine("file <path> [-c Configuration] [-p Name=Value]...");
                writer.WriteLine();
                writer.WriteLine("Load the executable to debug.");
                writer.WriteLine(
                    "Accepts a .dll, .exe, Unix executable, .csproj, or file-based app (.cs)."
                );
                writer.WriteLine(
                    "For a .csproj or .cs, builds then uses the output assembly (TargetPath)."
                );
                writer.WriteLine("  -c, --configuration   Build configuration (Debug/Release)");
                writer.WriteLine("  -p, --property        MSBuild property as Name=Value");
                break;
            case "next" or "n":
                writer.WriteLine("next [--wait]");
                writer.WriteLine();
                writer.WriteLine("Step over the current line.");
                writer.WriteLine(
                    "If stopped on an assignment, this executes it before the next stop."
                );
                writer.WriteLine("Stepping already waits; --wait is accepted for consistency.");
                writer.WriteLine(
                    "Use this to avoid stepping into a property getter or method call."
                );
                writer.WriteLine(
                    "An awaited throw can stop in the callee first; inspect data.reason before printing caller locals."
                );
                break;
            case "step" or "s":
                writer.WriteLine("step [--wait]");
                writer.WriteLine();
                writer.WriteLine("Step into the current call.");
                writer.WriteLine("Stepping already waits; --wait is accepted for consistency.");
                writer.WriteLine(
                    "User-defined property getters can be entered; JMC filters non-user code."
                );
                break;
            case "finish":
                writer.WriteLine("finish [--wait]");
                writer.WriteLine();
                writer.WriteLine("Step out of the current function.");
                writer.WriteLine("Stepping already waits; --wait is accepted for consistency.");
                break;
            case "until":
                writer.WriteLine("until [location]");
                writer.WriteLine();
                writer.WriteLine(
                    "Continue running until a temporary breakpoint at the current line or <location> is hit."
                );
                writer.WriteLine(
                    "Useful for skipping to a specific line or advancing to the next iteration."
                );
                break;
            case "continue" or "c":
                writer.WriteLine("continue [--wait] [--to <breakpoint-id>] [--timeout <seconds>]");
                writer.WriteLine();
                writer.WriteLine(
                    "Resume execution. Use --wait to return the next stop, trace, or exit."
                );
                writer.WriteLine(
                    "--to waits for that enabled breakpoint or exit, passing other stops including exceptions."
                );
                writer.WriteLine(
                    "It reports skippedStops; use events to inspect passed stops. Default timeout: 30 seconds."
                );
                writer.WriteLine(
                    "After exit, use 'run' to restart or 'wait' to read the exit result."
                );
                break;
            case "interrupt" or "int":
                writer.WriteLine("interrupt");
                writer.WriteLine();
                writer.WriteLine("Pause the debuggee if it is running.");
                break;
            case "breakpoints" or "bps":
                writer.WriteLine("breakpoints");
                writer.WriteLine();
                writer.WriteLine("List all breakpoints and tracepoints.");
                writer.WriteLine("Equivalent to 'info breakpoints'.");
                writer.WriteLine(
                    "verified=false before launch means the breakpoint is pending binding."
                );
                writer.WriteLine(
                    "The listed line is the request; boundLine is the resolved line after binding."
                );
                break;
            case "thread" or "t":
                writer.WriteLine("thread [id]");
                writer.WriteLine();
                writer.WriteLine("Show the current thread or switch to <id>.");
                writer.WriteLine();
                writer.WriteLine("Examples:");
                writer.WriteLine("  thread");
                writer.WriteLine("  thread 1234");
                break;
            case "up" or "u":
                writer.WriteLine("up");
                writer.WriteLine();
                writer.WriteLine("Move the selected frame up toward older stack frames.");
                break;
            case "down" or "dn":
                writer.WriteLine("down");
                writer.WriteLine();
                writer.WriteLine("Move the selected frame down toward newer stack frames.");
                break;
            case "wait":
                writer.WriteLine("wait [--timeout <seconds>]");
                writer.WriteLine();
                writer.WriteLine(
                    "Wait for a stop, trace result, or exit. Useful after 'continue'."
                );
                writer.WriteLine(
                    "Use --timeout to return an error if no result arrives in 1-3600 seconds."
                );
                break;
            case "info":
                writer.WriteLine("info <subject>");
                writer.WriteLine("info trace [--since LINE] [--limit N]");
                writer.WriteLine();
                writer.WriteLine(
                    "Subjects: breakpoints, modules, exception, locals, args, threads, files, status, trace"
                );
                writer.WriteLine(
                    "Trace defaults to the latest 100 lines; --since starts at a zero-based line."
                );
                writer.WriteLine("Use data.nextLine with --since to page through a growing log.");
                writer.WriteLine(
                    "Each page has at most 200 lines; long lines are clipped to 4096 characters."
                );
                writer.WriteLine(
                    "A cursor is invalid after trace-log --clear or a log-file change."
                );
                break;
            case "context" or "ctx":
                writer.WriteLine("context");
                writer.WriteLine();
                writer.WriteLine("Show session status and, when stopped, the selected frame,");
                writer.WriteLine("five nearby source lines, and up to eight arguments and locals.");
                writer.WriteLine(
                    "When locals are clipped, selection favors names on the current statement."
                );
                break;
            case "events":
                writer.WriteLine(
                    "events [--since SEQ] [--limit N] [--kind stop|trace|output|diagnostic]"
                );
                writer.WriteLine();
                writer.WriteLine(
                    "Read stops, trace results, target output, and debugger diagnostics."
                );
                writer.WriteLine("Use data.nextSeq with --since to read later events.");
                writer.WriteLine(
                    "Breakpoint stop events include user IDs and requested locations."
                );
                break;
            case "output":
                writer.WriteLine(
                    "output [--since SEQ] [--limit N] [--channel stdout|stderr|debug]"
                );
                writer.WriteLine();
                writer.WriteLine(
                    "Read only target output from the events feed; filter by channel."
                );
                writer.WriteLine("Use data.nextSeq with --since to read later output.");
                writer.WriteLine(
                    "Output remains available after exit until quit. --since filters out earlier events; use --since 0 for retained history."
                );
                break;
            case "backtrace":
                writer.WriteLine("backtrace [all] [--thread <tid>]");
                writer.WriteLine();
                writer.WriteLine("Print the stack trace for the current thread.");
                writer.WriteLine(
                    "Use 'all' to show all threads, or --thread <tid> for a specific thread."
                );
                writer.WriteLine(
                    "Synthetic async caller frames are marked; use frame N then info locals for captured values."
                );
                writer.WriteLine("print cannot evaluate C# expressions in those frames.");
                break;
            case "frame":
                writer.WriteLine("frame [index]");
                writer.WriteLine();
                writer.WriteLine("Show or select the current stack frame.");
                break;
            case "list":
                writer.WriteLine("list [file:line] [--lines N]");
                writer.WriteLine();
                writer.WriteLine("List source lines around the current location or <file:line>.");
                writer.WriteLine("Shows five lines by default; --lines accepts 1-100.");
                writer.WriteLine("Examples: list --lines 20; list Program.cs:42 --lines 20");
                break;
            case "delete":
                writer.WriteLine("delete <bp>");
                writer.WriteLine("clear <bp>");
                writer.WriteLine();
                writer.WriteLine("Delete the breakpoint with the given number.");
                break;
            case "enable":
                writer.WriteLine("enable <bp>");
                writer.WriteLine();
                writer.WriteLine("Enable the breakpoint with the given number.");
                break;
            case "disable":
                writer.WriteLine("disable <bp>");
                writer.WriteLine();
                writer.WriteLine("Disable the breakpoint with the given number.");
                break;
            case "source":
                writer.WriteLine("source [--last|--tail N] <file>");
                writer.WriteLine("source [--last|--tail N] -c <commands>");
                writer.WriteLine();
                writer.WriteLine(
                    "Execute file commands or semicolon-separated inline commands in order."
                );
                writer.WriteLine(
                    "Stops at the first failure; earlier commands are not rolled back."
                );
                writer.WriteLine("Default: all responses appear in data.responses.");
                writer.WriteLine(
                    "--last: final response plus up to 50 prints in batch.prints, including the final print."
                );
                writer.WriteLine(
                    "If later commands replace a stop response, batch.lastStop retains its compact location and context."
                );
                writer.WriteLine(
                    "The final response follows the final command: print has data.value; continue has data.reason."
                );
                writer.WriteLine(
                    "--tail N: last N responses in data.responses (1-20); earlier responses are counted in data.omittedResponses."
                );
                writer.WriteLine(
                    "--last and --tail N retain up to 50 print values in batch.prints, including earlier prints."
                );
                writer.WriteLine(
                    "Each print index is its 1-based command position in the script, including non-print commands."
                );
                writer.WriteLine("Use default output to retain every response.");
                writer.WriteLine("help and schema work in scripts; read them in data.responses.");
                writer.WriteLine(
                    "Batch only steps known in advance; inspect a stop before choosing dependent commands."
                );
                writer.WriteLine();
                writer.WriteLine("Examples:");
                writer.WriteLine(
                    "  source --last -c 'file app.csproj -c Debug; break Program.cs:20; run --wait'"
                );
                writer.WriteLine(
                    "  source --last -c 'print merchandise; print shipping; print rebate'"
                );
                writer.WriteLine("  source --tail 2 -c 'continue --wait; backtrace'");
                break;
            case "catch":
                writer.WriteLine("catch [user|all|unhandled|none]");
                writer.WriteLine();
                writer.WriteLine("Query or set whether thrown exceptions stop execution.");
                writer.WriteLine(
                    "user stops at user-code throws and unhandled exceptions (default); all stops on every throw."
                );
                writer.WriteLine(
                    "unhandled stops only before termination; none lets exceptions run."
                );
                writer.WriteLine("user skips framework async rethrows; all includes them.");
                writer.WriteLine(
                    "A throw in user code after an await still stops in user mode, even with a synthetic async caller."
                );
                break;
            case "decompile" or "dec" or "decomp":
                writer.WriteLine("decompile [--il]");
                writer.WriteLine();
                writer.WriteLine("Decompile the current frame to C# source or IL.");
                writer.WriteLine("Use --il to output intermediate language (IL) instructions.");
                writer.WriteLine("Useful when source files are not available.");
                break;
            case "quit":
                writer.WriteLine("quit");
                writer.WriteLine();
                writer.WriteLine("End this session and shut down its daemon.");
                writer.WriteLine("A still-running target is terminated.");
                writer.WriteLine("Always quit when finished, including after target exit.");
                writer.WriteLine("To leave an attached target running, detach first, then quit.");
                break;
            case "kill":
                writer.WriteLine("kill");
                writer.WriteLine();
                writer.WriteLine("Kill the target process without exiting the daemon.");
                break;
            case "detach":
                writer.WriteLine("detach");
                writer.WriteLine();
                writer.WriteLine("Detach from the target process without terminating it.");
                break;
            default:
                var command = CommandSchema.GetSchema()["commands"]!
                    .AsArray()
                    .FirstOrDefault(item => item?["name"]?.GetValue<string>() == subject);
                if (command is null)
                {
                    writer.WriteLine(
                        $"Unknown command '{subject}'. Use 'dotdbg help' for the command list."
                    );
                    break;
                }

                writer.WriteLine(command["usage"]?.GetValue<string>());
                writer.WriteLine();
                writer.WriteLine(command["description"]?.GetValue<string>());
                break;
        }
    }

    internal static void PrintHelp() => WriteHelp(Console.Out);

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine(
            """
            dotdbg - .NET debugger CLI for AI agents
            Usage: dotdbg [global options] <command> [command args]

            Session workflow (replace task-123 with a unique ID; reuse it on every call):
              dotdbg -s task-123 --json source --last -c 'file app.csproj -c Debug; break Program.cs:20; run --wait -- --plan plus'
              dotdbg -s task-123 --json source --last -c 'print subtotal; print total'
              dotdbg -s task-123 --json continue --wait
              dotdbg -s task-123 --json events --since 0
              dotdbg -s task-123 --json quit

            Read the result after each stop. Batch only commands known in advance.
            source --last returns the final response; bounded print values are in batch.prints.
            Use source --tail N to keep the last N responses plus earlier print values.
            Default 'catch user' skips framework async rethrows; use 'catch all' for every throw.
            Always quit after the investigation, even if the target exited. Quit also ends the daemon.
            If an attached target must keep running, detach before quit.

            Global options (before the command):
              -s, --session-id ID   Session key; default is a hash of the current directory
              --json                JSON responses (-v and --verbose are aliases)
              --json-input FILE     Run a JSON batch file
              -h, --help            Show this guide
              -d, --daemon          Internal background service

            Commands by task (use 'help all' for syntax in one call):
              Start/end:     file run attach detach kill quit
              Stop points:   break breakpoints condition ignore enable disable delete trace trace-log
              Execution:     continue wait interrupt next step finish until catch
              Inspection:    context print backtrace frame thread up down list info output events decompile
              Watches:       watch watches unwatch
              Automation:    source help schema

            Use -- between run options and target arguments. 'run --wait' returns a stop or exit.
            With --json, help returns data.text. Use 'schema' for machine-readable syntax.
            Use 'help source' for batching.
            """
        );
        if (OperatingSystem.IsWindows())
            writer.WriteLine("PowerShell quoting: dotdbg help powershell");
    }

    private static void PrintPowerShellHelp(TextWriter writer)
    {
        writer.WriteLine("PowerShell quoting");
        writer.WriteLine();
        writer.WriteLine("Single-quote a whole C# expression containing string literals:");
        writer.WriteLine("  dotdbg condition 1 'parcel.Id == \"P200\"'");
        writer.WriteLine("  dotdbg break 'Program.cs:20 if item.Id == \"P200\"'");
        writer.WriteLine("  dotdbg print 'parcel.Id == \"P200\"'");
        writer.WriteLine("  dotdbg trace Program.cs:20 'item.Id + \": \" + item.Price'");
        writer.WriteLine("Backslash does not escape double quotes in PowerShell.");
        writer.WriteLine();
        writer.WriteLine("For source -c, single-quote the script; keep inner C# double quotes:");
        writer.WriteLine("  dotdbg source -c 'condition 1 parcel.Id == \"P200\"; continue --wait'");
        writer.WriteLine(
            "  dotdbg --json source --last -c 'print merchandise; print shipping; print rebate'"
        );
        writer.WriteLine("Do not add backslashes before the inner double quotes.");
        writer.WriteLine("Check the stored condition with 'breakpoints'.");
    }
}
