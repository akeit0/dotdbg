using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotDbg.Cli;
using DotDbg.Engine;
using DotDbg.Ipc;
using DotDbg.Util;

namespace DotDbg;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        GlobalOptions options;
        try
        {
            options = CommandParser.ParseInvocation(args);
        }
        catch (CommandParseException ex)
        {
            var jsonOutputRequested = IsJsonOutputRequestedBeforeError(args);
            if (jsonOutputRequested)
            {
                Console.WriteLine(
                    JsonSerializer.Serialize(
                        new JsonObject
                        {
                            ["success"] = false,
                            ["phase"] = "invocation",
                            ["error"] = new JsonObject { ["message"] = ex.Message },
                        },
                        OperationProtocol.JsonOptions
                    )
                );
            }
            else
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
            }
            return ex.ExitCode;
        }

        if (
            options.Help
            || options.CommandArgs.Count == 0
                && !options.Daemon
                && string.IsNullOrEmpty(options.JsonInputFile)
        )
        {
            PrintHelpResponse(string.Empty, options.JsonOutput);
            return 0;
        }

        if (options.Daemon)
        {
            return await RunDaemonAsync(options.SessionId, options.JsonOutput)
                .ConfigureAwait(false);
        }

        if (!string.IsNullOrEmpty(options.JsonInputFile))
        {
            return await RunJsonBatchAsync(options.SessionId, options.JsonInputFile)
                .ConfigureAwait(false);
        }

        var result = CommandParser.ParseCommand(options.CommandArgs, Environment.CurrentDirectory);
        if (!result.Success)
        {
            if (options.JsonOutput)
            {
                Console.WriteLine(
                    JsonSerializer.Serialize(
                        new JsonObject
                        {
                            ["success"] = false,
                            ["phase"] = "parse",
                            ["error"] = new JsonObject { ["message"] = result.Error },
                        },
                        OperationProtocol.JsonOptions
                    )
                );
            }
            else
            {
                Console.Error.WriteLine($"Error: {result.Error}");
            }
            return result.ExitCode;
        }

        if (result.Request is HelpCommand help)
        {
            PrintHelpResponse(help.Subject ?? string.Empty, options.JsonOutput);
            return 0;
        }

        if (result.Request is SchemaCommand)
        {
            Console.WriteLine(
                JsonSerializer.Serialize(CommandSchema.GetSchema(), OperationProtocol.JsonOptions)
            );
            return 0;
        }

        if (result.Request is KillCommand or QuitCommand)
        {
            return await RunShutdownAsync(options.SessionId, result.Request!, options.JsonOutput)
                .ConfigureAwait(false);
        }

        return await RunClientAsync(options.SessionId, result.Request!, options.JsonOutput)
            .ConfigureAwait(false);
    }

    private static void PrintHelpResponse(string subject, bool jsonOutput)
    {
        if (!jsonOutput)
        {
            CommandHelp.PrintCommandHelp(subject);
            return;
        }

        Console.WriteLine(
            JsonSerializer.Serialize(
                new JsonObject
                {
                    ["success"] = true,
                    ["message"] = "Help",
                    ["data"] = new JsonObject { ["text"] = CommandHelp.RenderCommandHelp(subject) },
                },
                OperationProtocol.JsonOptions
            )
        );
    }

    private static async Task<int> RunDaemonAsync(string sessionId, bool verbose)
    {
        var pipeName = PipeServer.ComputePipeName(sessionId);

        Action<string> logger = verbose
            ? msg => Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {msg}")
            : msg => { };

        using var session = new DebugSession(logger, sessionId);
        using var server = new PipeServer(session, sessionId);

        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            session.Log("Shutdown requested.");
            session.Shutdown();
        };

        await server.StartAsync().ConfigureAwait(false);
        if (verbose)
            Console.WriteLine($"dotdbg daemon listening on {pipeName} (session: {sessionId})");

        try
        {
            await server.WaitForShutdownAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }

        return 0;
    }

    private static Task<int> RunClientAsync(
        string sessionId,
        CommandRequest request,
        bool verbose
    ) => RunClientAsync(sessionId, request.ToJson(), verbose);

    private static async Task<int> RunClientAsync(
        string sessionId,
        JsonObject request,
        bool verbose
    )
    {
        var op = request["op"]?.GetValue<string>() ?? string.Empty;
        var timeout = op switch
        {
            "file"
            or "wait"
            or "next"
            or "step"
            or "finish"
            or "interrupt"
            or "source"
            or "until" => -1,
            "run" when request["wait"]?.GetValue<bool>() == true => -1,
            "continue" when request["wait"]?.GetValue<bool>() == true => -1,
            "run" or "attach" => 30000,
            _ => 5000,
        };
        using var cts = new CancellationTokenSource();
        if (timeout > 0)
            cts.CancelAfter(TimeSpan.FromMilliseconds(timeout));

        try
        {
            await EnsureDaemonRunningAsync(sessionId).ConfigureAwait(false);
            var response = await PipeClient
                .SendAsync(sessionId, request, timeout, cts.Token)
                .ConfigureAwait(false);
            ResponsePathPresenter.MakePathsRelative(response, Environment.CurrentDirectory);
            if (verbose)
            {
                Console.WriteLine(
                    JsonSerializer.Serialize(response, OperationProtocol.JsonOptions)
                );
            }
            else
            {
                ConsoleResponseWriter.PrintResponse(response);
            }

            var success = response["success"]?.GetValue<bool>() ?? false;
            return success ? 0 : 1;
        }
        catch (TimeoutException)
        {
            PrintClientError("Timed out waiting for daemon.", verbose);
            return 1;
        }
        catch (OperationCanceledException)
        {
            PrintClientError("Timed out waiting for the daemon to respond.", verbose);
            return 1;
        }
        catch (Exception ex)
        {
            PrintClientError(ex.Message, verbose);
            return 1;
        }
    }

    private static async Task<int> RunShutdownAsync(
        string sessionId,
        CommandRequest request,
        bool verbose
    )
    {
        using var cts = new CancellationTokenSource(5000);
        try
        {
            var response = await PipeClient
                .SendAsync(sessionId, request.ToJson(), 5000, cts.Token)
                .ConfigureAwait(false);
            ResponsePathPresenter.MakePathsRelative(response, Environment.CurrentDirectory);
            if (verbose)
                Console.WriteLine(
                    JsonSerializer.Serialize(response, OperationProtocol.JsonOptions)
                );
            else
                ConsoleResponseWriter.PrintResponse(response);

            var success = response["success"]?.GetValue<bool>() ?? false;
            return success ? 0 : 1;
        }
        catch (Exception ex)
        {
            if (verbose)
            {
                Console.WriteLine(
                    JsonSerializer.Serialize(
                        new JsonObject
                        {
                            ["success"] = false,
                            ["phase"] = "transport",
                            ["error"] = new JsonObject { ["message"] = ex.Message },
                        },
                        OperationProtocol.JsonOptions
                    )
                );
            }
            else
            {
                Console.Error.WriteLine($"Unable to confirm shutdown: {ex.Message}");
            }
            return 1;
        }
    }

    private static void PrintClientError(string message, bool json)
    {
        if (json)
        {
            Console.WriteLine(
                JsonSerializer.Serialize(
                    new JsonObject
                    {
                        ["success"] = false,
                        ["phase"] = "transport",
                        ["error"] = new JsonObject { ["message"] = message },
                    },
                    OperationProtocol.JsonOptions
                )
            );
        }
        else
        {
            Console.Error.WriteLine($"Error: {message}");
        }
    }

    private static async Task<int> RunJsonBatchAsync(string sessionId, string filePath)
    {
        var batchVerbose = true; // JSON batch mode always produces machine-readable output.
        string text;
        try
        {
            text = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            WriteBatchError($"Failed to read batch file: {ex.Message}");
            return 1;
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text);
        }
        catch (JsonException ex)
        {
            WriteBatchError($"Invalid batch JSON: {ex.Message}");
            return 1;
        }

        var stopOnError = true;
        JsonArray? commands;

        if (node is JsonObject root)
        {
            if (root["stopOnError"] is { } stopOnErrorNode)
            {
                if (
                    stopOnErrorNode is not JsonValue stopOnErrorValue
                    || !stopOnErrorValue.TryGetValue<bool>(out stopOnError)
                )
                {
                    WriteBatchError("'stopOnError' must be a boolean");
                    return 1;
                }
            }
            if (root["commands"] is JsonArray arr)
                commands = arr;
            else
            {
                WriteBatchError("Batch object must have a 'commands' array");
                return 1;
            }
        }
        else if (node is JsonArray arr)
        {
            commands = arr;
        }
        else
        {
            WriteBatchError("Batch file must be a JSON array or object with a 'commands' array");
            return 1;
        }

        var firstError = 0;
        var cwd = Environment.CurrentDirectory;

        foreach (var commandNode in commands)
        {
            if (commandNode is null)
                continue;

            int exitCode;
            if (commandNode is JsonValue value && value.TryGetValue<string>(out var commandString))
            {
                var result = CommandParser.ParseScriptCommandLine(commandString, cwd);
                if (!result.Success)
                {
                    WriteBatchError(result.Error ?? "Unknown parse error");
                    if (stopOnError)
                        return result.ExitCode;
                    firstError = firstError == 0 ? result.ExitCode : firstError;
                    continue;
                }
                exitCode = await RunClientAsync(sessionId, result.Request!.ToJson(), batchVerbose)
                    .ConfigureAwait(false);
            }
            else if (commandNode is JsonObject request)
            {
                if (
                    request["op"] is not JsonValue opValue
                    || !opValue.TryGetValue<string>(out var operation)
                    || string.IsNullOrWhiteSpace(operation)
                )
                {
                    WriteBatchError("JSON command needs a nonempty string 'op' field");
                    if (stopOnError)
                        return 1;
                    firstError = firstError == 0 ? 1 : firstError;
                    continue;
                }
                if (request["cwd"] is null)
                    request["cwd"] = cwd;
                exitCode = await RunClientAsync(sessionId, request, batchVerbose)
                    .ConfigureAwait(false);
            }
            else
            {
                WriteBatchError("Each command must be a string or a JSON object");
                if (stopOnError)
                    return 1;
                firstError = firstError == 0 ? 1 : firstError;
                continue;
            }

            if (exitCode != 0)
            {
                if (stopOnError)
                    return exitCode;
                firstError = firstError == 0 ? exitCode : firstError;
            }
        }

        return firstError;
    }

    private static void WriteBatchError(string message)
    {
        var json = new JsonObject
        {
            ["success"] = false,
            ["phase"] = "batch",
            ["error"] = new JsonObject { ["message"] = message },
        };
        Console.WriteLine(JsonSerializer.Serialize(json, OperationProtocol.JsonOptions));
    }

    private static async Task EnsureDaemonRunningAsync(string sessionId)
    {
        try
        {
            using var cts = new CancellationTokenSource(500);
            await PipeClient
                .SendAsync(sessionId, new JsonObject { ["op"] = "ping" }, 500, cts.Token)
                .ConfigureAwait(false);
            return;
        }
        catch
        {
            // Daemon not running; start it.
        }

        var startInfo = new ProcessStartInfo
        {
            // Shell launch detaches the daemon from a caller's captured standard
            // handles. Redirected child pipes still inherit other shell handles
            // on Windows and can keep command substitution open indefinitely.
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        var assemblyPath = typeof(Program).Assembly.Location;
        var processPath = Environment.ProcessPath;

        if (
            !string.IsNullOrEmpty(processPath)
            && Path.GetFileNameWithoutExtension(processPath)
                .Equals("dotnet", StringComparison.OrdinalIgnoreCase)
        )
        {
            startInfo.FileName = processPath;
            startInfo.ArgumentList.Add("exec");
            startInfo.ArgumentList.Add(assemblyPath);
            startInfo.ArgumentList.Add("--daemon");
            startInfo.ArgumentList.Add("--session-id");
            startInfo.ArgumentList.Add(sessionId);
        }
        else if (
            !string.IsNullOrEmpty(processPath)
            && Path.GetFileNameWithoutExtension(processPath)
                .Equals("dotdbg", StringComparison.OrdinalIgnoreCase)
        )
        {
            startInfo.FileName = processPath;
            startInfo.ArgumentList.Add("--daemon");
            startInfo.ArgumentList.Add("--session-id");
            startInfo.ArgumentList.Add(sessionId);
        }
        else
        {
            startInfo.FileName = "dotnet";
            startInfo.ArgumentList.Add("exec");
            startInfo.ArgumentList.Add(assemblyPath);
            startInfo.ArgumentList.Add("--daemon");
            startInfo.ArgumentList.Add("--session-id");
            startInfo.ArgumentList.Add(sessionId);
        }

        var daemon =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start dotdbg daemon");

        // Wait a moment for the daemon to start and try to connect.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await Task.Delay(300).ConfigureAwait(false);
            try
            {
                using var cts = new CancellationTokenSource(500);
                await PipeClient
                    .SendAsync(sessionId, new JsonObject { ["op"] = "ping" }, 500, cts.Token)
                    .ConfigureAwait(false);
                return;
            }
            catch { }
        }

        throw new InvalidOperationException("Failed to start dotdbg daemon");
    }

    private static bool IsJsonOutputRequestedBeforeError(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--")
                break;
            if (arg is "--json" or "-v" or "--verbose")
                return true;
            if (arg is "-s" or "--session-id" or "--json-input")
            {
                i++;
                continue;
            }
            if (arg.StartsWith("--session-id=") || arg.StartsWith("--json-input="))
                continue;
            if (arg is "-d" or "--daemon" or "-h" or "--help")
                continue;
            // Unknown options and the command boundary are where invocation parsing stops.
            break;
        }
        return false;
    }
}
