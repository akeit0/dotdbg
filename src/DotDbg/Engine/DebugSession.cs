using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DotDbg.Cli;
using DotDbg.Util;
using SharpDbg.Infrastructure.Debugger;
using SharpDbg.Infrastructure.Debugger.Models;
using SharpDbg.Infrastructure.Debugger.Models.Response;
using static DotDbg.Engine.OperationResponse;

namespace DotDbg.Engine;

public sealed class DebugSession : IDisposable
{
    private readonly Action<string>? _logger;
    private readonly string _sessionId;
    private readonly AsyncAutoResetEvent<StopInfo> _stopEvent = new();
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly List<UserBreakpoint> _breakpoints = new();
    private readonly Lock _breakpointsLock = new();
    private readonly List<WatchEntry> _watches = new();
    private readonly SessionEventBuffer _events = new();
    private long _resumeSequence;
    private int _nextWatchId = 1;

    private readonly TraceLogStore _traceLog = new();

    private ManagedDebugger? _debugger;
    private string _targetFilePath = string.Empty;
    private string _workingDirectory = string.Empty;
    private int _nextBreakpointId = 1;

    private int _selectedThreadId;
    private int _selectedFrameIndex;
    private int _selectedFrameId;
    private StopInfo? _terminalStop;
    private bool _unhandledException;
    private string _exceptionBreakMode = "user";

    public DebugSession(Action<string>? logger = null, string? sessionId = null)
    {
        _logger = logger;
        _sessionId = sessionId ?? string.Empty;
    }

    public CancellationToken ShutdownToken => _shutdownCts.Token;
    public bool ShutdownRequested => _shutdownCts.IsCancellationRequested;

    public void Shutdown() => _shutdownCts.Cancel();

    public void Log(string message) => _logger?.Invoke(message);

    public async Task<JsonObject> HandleOperationAsync(
        JsonObject request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var op = request["op"]?.GetValue<string>() ?? string.Empty;
            var cwd = request["cwd"]?.GetValue<string>() ?? string.Empty;
            if (!string.IsNullOrEmpty(cwd))
                _workingDirectory = cwd;

            return op switch
            {
                "ping" => ResponseOk("pong"),
                "file" => await HandleFileAsync(request, cwd, cancellationToken),
                "run" => await HandleRunAsync(request, cwd, cancellationToken),
                "attach" => await HandleAttachAsync(request, cancellationToken),
                "detach" => await HandleDetachAsync(),
                "break" => await HandleBreakAsync(request, cwd),
                "trace" => await HandleTraceAsync(request, cwd),
                "delete" => await HandleDeleteAsync(request),
                "enable" => await HandleEnableAsync(request),
                "disable" => await HandleDisableAsync(request),
                "condition" => await HandleConditionAsync(request),
                "ignore" => await HandleIgnoreAsync(request),
                "continue" => await HandleContinueAsync(request, cancellationToken),
                "interrupt" => await HandleInterruptAsync(cancellationToken),
                "next" => await HandleStepAsync(StepKind.Next, cancellationToken),
                "step" => await HandleStepAsync(StepKind.In, cancellationToken),
                "finish" => await HandleStepAsync(StepKind.Out, cancellationToken),
                "until" => await HandleUntilAsync(request, cwd, cancellationToken),
                "backtrace" => await HandleBacktraceAsync(request),
                "frame" => await HandleFrameAsync(request),
                "up" => await HandleUpAsync(),
                "down" => await HandleDownAsync(),
                "thread" => await HandleThreadAsync(request),
                "list" => await HandleListAsync(request, cwd),
                "decompile" => await HandleDecompileAsync(request),
                "print" => await HandlePrintAsync(request),
                "watch" => await HandleWatchAsync(request),
                "unwatch" => await HandleUnwatchAsync(request),
                "watches" => await HandleWatchesAsync(),
                "trace-log" => _traceLog.Configure(request, path => ResolvePath(path, cwd)),
                "catch" => await HandleCatchAsync(request),
                "wait" => await HandleWaitAsync(request, cancellationToken),
                "source" => await HandleSourceAsync(request, cwd, cancellationToken),
                "help" => ResponseOk(
                    "Help",
                    new JsonObject
                    {
                        ["text"] = CommandHelp.RenderCommandHelp(
                            request["subject"]?.GetValue<string>() ?? string.Empty
                        ),
                    }
                ),
                "schema" => ResponseOk(
                    "Schema",
                    new JsonObject { ["schema"] = CommandSchema.GetSchema() }
                ),
                "info" => await HandleInfoAsync(request, cwd),
                "context" => await HandleContextAsync(),
                "events" => HandleEventQuery(request, false),
                "output" => HandleEventQuery(request, true),
                "quit" => await HandleQuitAsync(),
                "kill" => await HandleKillAsync(),
                "breakpoints" => await HandleBreakpointsAsync(),
                _ => ResponseError($"Unknown operation: {op}"),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log($"Operation failed: {ex}");
            return ResponseError(ex.Message);
        }
    }

    private async Task<JsonObject> HandleFileAsync(
        JsonObject request,
        string cwd,
        CancellationToken cancellationToken
    )
    {
        var path = request["path"]?.GetValue<string>() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return ResponseError("Missing file path");

        var resolved = ResolvePath(path, cwd);
        if (!File.Exists(resolved))
            return ResponseError($"Target not found: {resolved}");

        var extension = Path.GetExtension(resolved);
        var isUnixExecutable =
            !OperatingSystem.IsWindows()
            && extension.Length == 0
            && (
                File.GetUnixFileMode(resolved)
                & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)
            ) != 0;
        if (
            !extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            && !isUnixExecutable
            && !ProjectTargetResolver.IsBuildableTarget(resolved)
        )
            return ResponseError(
                "Unsupported target type. Use a .dll, .exe, Unix executable, .csproj, or .cs file."
            );

        if (!ProjectTargetResolver.IsBuildableTarget(resolved))
        {
            _targetFilePath = resolved;
            return ResponseOk($"Target set to {_targetFilePath}");
        }

        var configuration = request["configuration"]?.GetValue<string>();
        var properties =
            request["properties"]
                ?.AsArray()
                .Select(node => node?.GetValue<string>() ?? string.Empty)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList()
            ?? new List<string>();

        Log($"Building {resolved}...");
        var result = await ProjectTargetResolver
            .BuildAndResolveAsync(resolved, configuration, properties, cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
            return ResponseError(result.Error ?? "Failed to build target");

        _targetFilePath = result.TargetPath!;
        var data = new JsonObject { ["source"] = resolved, ["file"] = _targetFilePath };
        if (!string.IsNullOrWhiteSpace(configuration))
            data["configuration"] = configuration;

        var configPart = string.IsNullOrWhiteSpace(configuration)
            ? "default configuration"
            : configuration;
        return ResponseOk(
            $"Built {resolved} ({configPart}); target set to {_targetFilePath}",
            data
        );
    }

    private async Task<JsonObject> HandleRunAsync(
        JsonObject request,
        string cwd,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(_targetFilePath))
            return ResponseError("No target file. Use 'file <path>' first.");

        if (ProjectTargetResolver.IsBuildableTarget(_targetFilePath))
        {
            return ResponseError(
                $"Target is still a source project/file ({Path.GetExtension(_targetFilePath)}). "
                    + "Use 'file <path.csproj|.cs>' so it builds to a .dll first, then 'run'."
            );
        }

        var args =
            request["targetArgs"]
                ?.AsArray()
                .Select(node => node?.GetValue<string>() ?? string.Empty)
                .ToList()
            ?? new List<string>();
        var stopAtEntry = request["stopAtEntry"]?.GetValue<bool>() ?? false;
        var justMyCode = request["justMyCode"]?.GetValue<bool?>() ?? true;
        var targetCwd = request["targetCwd"]?.GetValue<string>();
        var launchCwd = string.IsNullOrWhiteSpace(targetCwd)
            ? (string.IsNullOrEmpty(cwd) ? _workingDirectory : cwd)
            : ResolvePath(targetCwd, cwd);
        if (!Directory.Exists(launchCwd))
            return ResponseError($"Working directory not found: {launchCwd}");

        var environment = new Dictionary<string, string>(EnvironmentNameComparer.Instance);
        if (request["env"] is JsonObject envObject)
        {
            foreach (var (name, value) in envObject)
            {
                if (string.IsNullOrWhiteSpace(name) || name.Contains('='))
                    return ResponseError($"Invalid environment variable name: {name}");
                environment[name] = value?.GetValue<string>() ?? string.Empty;
            }
        }
        else if (request["env"] is not null)
        {
            return ResponseError("run: env must be an object of string values");
        }

        await ResetDebuggerAsync(cancellationToken);

        var launchInfo = new LaunchInfo
        {
            Program = _targetFilePath,
            Arguments = args,
            Cwd = launchCwd,
            Env = environment,
            StopAtEntry = stopAtEntry,
            LaunchRequestConsoleType = LaunchRequestConsoleType.InternalConsole,
        };

        _resumeSequence = _events.LatestSequence;
        _stopEvent.Reset();
        _debugger!.Launch(launchInfo, justMyCode);

        try
        {
            await _debugger.ConfigurationDone().WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return ResponseError("Timed out waiting for launch to complete");
        }

        if (request["wait"]?.GetValue<bool>() == true)
        {
            var waitRequest = new JsonObject
            {
                ["timeoutSeconds"] = request["timeoutSeconds"]?.GetValue<int?>() ?? 30,
            };
            return await HandleWaitAsync(waitRequest, cancellationToken);
        }

        return ResponseOk($"Started {launchInfo.Program}");
    }

    private async Task<JsonObject> HandleAttachAsync(
        JsonObject request,
        CancellationToken cancellationToken
    )
    {
        var targetValue = request["attachTarget"];
        if (targetValue is null)
            return ResponseError("Missing process ID or name");

        var target = targetValue.GetValue<string>();
        if (string.IsNullOrWhiteSpace(target))
            return ResponseError("Missing process ID or name");

        int pid;
        if (int.TryParse(target, out pid))
        {
            // target is a PID
        }
        else
        {
            var candidates = Process.GetProcessesByName(target).ToList();
            if (candidates.Count == 0)
                return ResponseError($"No process named '{target}' found");

            if (candidates.Count > 1)
            {
                var list = string.Join(", ", candidates.Select(p => $"{p.Id}"));
                return ResponseError(
                    $"Multiple processes named '{target}' found: {list}. Use a PID."
                );
            }

            pid = candidates[0].Id;
        }

        await ResetDebuggerAsync(cancellationToken);

        var justMyCode = request["justMyCode"]?.GetValue<bool?>() ?? true;
        _stopEvent.Reset();
        var debugger = _debugger!;
        debugger.Attach(pid, justMyCode);

        try
        {
            await debugger.ConfigurationDone().WaitAsync(cancellationToken);

            // Wait for the asynchronous attach to complete.
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCts.Token
            );
            while (!debugger.IsProcessAttached)
            {
                await Task.Delay(50, linkedCts.Token).ConfigureAwait(false);
            }
        }
        catch (TimeoutException)
        {
            return ResponseError("Timed out waiting for attach to complete");
        }
        catch (OperationCanceledException)
        {
            return ResponseError("Timed out waiting for attach to complete");
        }

        return ResponseOk($"Attached to process {pid}");
    }

    private async Task<JsonObject> HandleDetachAsync()
    {
        if (_debugger is null)
            return ResponseOk("Not attached");

        try
        {
            _debugger.Disconnect(terminateDebuggee: false);
        }
        catch (Exception ex)
        {
            Log($"Detach failed: {ex.Message}");
            return ResponseError($"Detach failed; target state is unknown: {ex.Message}");
        }

        _debugger = null;
        _selectedThreadId = 0;
        _selectedFrameIndex = 0;
        _selectedFrameId = 0;
        Volatile.Write(ref _terminalStop, null);
        Volatile.Write(ref _unhandledException, false);
        _stopEvent.Reset();
        return ResponseOk("Detached");
    }

    private async Task<JsonObject> HandleBreakAsync(JsonObject request, string cwd)
    {
        var location = request["location"]?.GetValue<string>() ?? string.Empty;
        var condition = request["condition"]?.GetValue<string>();
        var name = request["name"]?.GetValue<string>();
        var ilMode = request["ilMode"]?.GetValue<bool>() ?? false;
        var moduleName = request["module"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(location))
            return ResponseError("Missing breakpoint location");

        UserBreakpoint bp;

        if (ilMode)
        {
            if (
                !IlBreakpointLocation.TryParse(
                    location,
                    out var ilMethod,
                    out var ilOffset,
                    out var ilError
                )
            )
                return ResponseError($"Invalid IL breakpoint: {ilError}");

            bp = new UserBreakpoint
            {
                Id = _nextBreakpointId++,
                Name = string.IsNullOrWhiteSpace(name) ? string.Empty : name,
                IsIlBreakpoint = true,
                IlMethod = ilMethod,
                IlOffset = ilOffset,
                IlModule = string.IsNullOrWhiteSpace(moduleName) ? null : moduleName,
                Condition = string.IsNullOrWhiteSpace(condition) ? null : condition,
                Enabled = true,
            };

            lock (_breakpointsLock)
            {
                _breakpoints.Add(bp);
            }

            if (_debugger is not null)
                await RefreshIlBreakpointsAsync(bp.IlModule, bp.IlMethod!);

            return ResponseOk(
                $"Breakpoint {bp.Id} at {bp.IlMethod}:IL_{bp.IlOffset:X4}",
                new JsonObject
                {
                    ["id"] = bp.Id,
                    ["breakpointId"] = bp.Id,
                    ["name"] = bp.Name,
                }
            );
        }

        // Handle module.dll!location
        var parts = location.Split('!', 2);
        var rawLocation = parts.Length == 2 ? parts[1] : parts[0];

        var (filePath, line, column) = ParseLocation(rawLocation, cwd);
        if (filePath is null)
            return ResponseError(
                "Only file:line[:column] breakpoints are supported in this prototype"
            );

        var fullPath = ResolvePath(filePath, cwd);
        bp = new UserBreakpoint
        {
            Id = _nextBreakpointId++,
            Name = string.IsNullOrWhiteSpace(name) ? string.Empty : name,
            FilePath = fullPath,
            Line = line,
            Column = column,
            Condition = string.IsNullOrWhiteSpace(condition) ? null : condition,
            Enabled = true,
        };

        lock (_breakpointsLock)
        {
            var conflict = CoLocatedSourceRuleErrorLocked(bp, bp.Condition, bp.HitCondition);
            if (conflict is not null)
                return ResponseError(conflict);
            _breakpoints.Add(bp);
        }

        if (_debugger is not null)
            await RefreshFileBreakpointsAsync(fullPath);

        var binding =
            bp.BoundLine is { } boundLine && boundLine != bp.Line
                ? $" (bound at {bp.FilePath}:{boundLine})"
                : string.Empty;
        return ResponseOk(
            $"Breakpoint {bp.Id} at {bp.FilePath}:{bp.Line}{binding}",
            new JsonObject
            {
                ["id"] = bp.Id,
                ["breakpointId"] = bp.Id,
                ["name"] = bp.Name,
                ["file"] = bp.FilePath,
                ["line"] = bp.Line,
                ["column"] = bp.Column,
                ["verified"] = bp.Verified,
                ["boundLine"] = bp.BoundLine,
                ["boundColumn"] = bp.BoundColumn,
            }
        );
    }

    private async Task<JsonObject> HandleDeleteAsync(JsonObject request)
    {
        if (!TryGetBreakpointNumber(request, out var id, out var error))
            return error;

        UserBreakpoint? bp;
        lock (_breakpointsLock)
        {
            bp = _breakpoints.FirstOrDefault(b => b.Id == id);
            if (bp is not null)
                _breakpoints.Remove(bp);
        }

        if (bp is null)
            return ResponseError($"Breakpoint {id} not found");

        if (_debugger is not null)
        {
            if (bp.IsIlBreakpoint)
                await RefreshIlBreakpointsAsync(bp.IlModule, bp.IlMethod!);
            else
                await RefreshFileBreakpointsAsync(bp.FilePath);
        }

        return ResponseOk($"Deleted breakpoint {id}");
    }

    private async Task<JsonObject> HandleEnableAsync(JsonObject request)
    {
        if (!TryGetBreakpointNumber(request, out var id, out var error))
            return error;

        UserBreakpoint? bp;
        lock (_breakpointsLock)
        {
            bp = _breakpoints.FirstOrDefault(b => b.Id == id);
            if (bp is not null)
            {
                var conflict = CoLocatedSourceRuleErrorLocked(
                    bp,
                    bp.Condition,
                    bp.HitCondition,
                    enabled: true
                );
                if (conflict is not null)
                    return ResponseError(conflict);
                bp.Enabled = true;
            }
        }

        if (bp is null)
            return ResponseError($"Breakpoint {id} not found");

        if (_debugger is not null)
            await RefreshAllBreakpointsAsync();

        return ResponseOk($"Enabled breakpoint {id}");
    }

    private async Task<JsonObject> HandleDisableAsync(JsonObject request)
    {
        if (!TryGetBreakpointNumber(request, out var id, out var error))
            return error;

        UserBreakpoint? bp;
        lock (_breakpointsLock)
        {
            bp = _breakpoints.FirstOrDefault(b => b.Id == id);
            if (bp is not null)
                bp.Enabled = false;
        }

        if (bp is null)
            return ResponseError($"Breakpoint {id} not found");

        if (_debugger is not null)
            await RefreshAllBreakpointsAsync();

        return ResponseOk($"Disabled breakpoint {id}");
    }

    private async Task<JsonObject> HandleConditionAsync(JsonObject request)
    {
        if (!TryGetBreakpointNumber(request, out var id, out var error))
            return error;

        var condition = request["condition"]?.GetValue<string>() ?? string.Empty;

        UserBreakpoint? bp;
        lock (_breakpointsLock)
        {
            bp = _breakpoints.FirstOrDefault(b => b.Id == id);
            if (bp is not null)
            {
                var updatedCondition = string.IsNullOrWhiteSpace(condition) ? null : condition;
                var conflict = CoLocatedSourceRuleErrorLocked(
                    bp,
                    updatedCondition,
                    bp.HitCondition
                );
                if (conflict is not null)
                    return ResponseError(conflict);
                bp.Condition = updatedCondition;
            }
        }

        if (bp is null)
            return ResponseError($"Breakpoint {id} not found");

        if (_debugger is not null)
            await RefreshAllBreakpointsAsync();

        return ResponseOk(
            string.IsNullOrWhiteSpace(condition)
                ? $"Cleared condition for breakpoint {id}"
                : $"Set condition for breakpoint {id}: {condition}"
        );
    }

    private async Task<JsonObject> HandleIgnoreAsync(JsonObject request)
    {
        if (!TryGetBreakpointNumber(request, out var id, out var error))
            return error;

        var count = request["count"]?.GetValue<int?>() ?? 0;

        UserBreakpoint? bp;
        lock (_breakpointsLock)
        {
            bp = _breakpoints.FirstOrDefault(b => b.Id == id);
            if (bp is not null)
            {
                var updatedHitCondition = count > 0 ? $">{count}" : null;
                var conflict = CoLocatedSourceRuleErrorLocked(
                    bp,
                    bp.Condition,
                    updatedHitCondition
                );
                if (conflict is not null)
                    return ResponseError(conflict);
                bp.HitCondition = updatedHitCondition;
            }
        }

        if (bp is null)
            return ResponseError($"Breakpoint {id} not found");

        if (_debugger is not null)
            await RefreshAllBreakpointsAsync();

        return ResponseOk(
            count > 0
                ? $"Ignoring first {count} hits for breakpoint {id}"
                : $"Cleared hit count for breakpoint {id}"
        );
    }

    private async Task<JsonObject> HandleContinueAsync(
        JsonObject request,
        CancellationToken cancellationToken
    )
    {
        if (_debugger is null)
            return ResponseError("No active session");

        if (Volatile.Read(ref _terminalStop) is { } terminal)
            return ResponseError(
                $"Target has exited (code {terminal.ExitCode?.ToString() ?? "unknown"}). Use 'run' to restart, or 'wait' to read the exit result."
            );

        var wait = request["wait"]?.GetValue<bool>() == true;
        var targetBreakpointId = request["targetBreakpointId"]?.GetValue<int?>();
        if (!wait && request["timeoutSeconds"] is not null)
            return ResponseError("continue: timeoutSeconds requires wait");
        if (targetBreakpointId is not null)
        {
            if (!wait)
                return ResponseError("continue: --to requires waiting for a stop");
            lock (_breakpointsLock)
            {
                if (
                    !_breakpoints.Any(bp =>
                        bp.Id == targetBreakpointId
                        && bp.Enabled
                        && string.IsNullOrWhiteSpace(bp.LogExpression)
                    )
                )
                    return ResponseError(
                        $"Breakpoint {targetBreakpointId} is missing, disabled, or a tracepoint"
                    );
            }
        }

        _resumeSequence = _events.LatestSequence;
        _stopEvent.Reset();
        _selectedFrameId = 0;
        _debugger.HandleContinueRequest();
        if (!wait)
            return ResponseOk("Continuing");

        var waitRequest = new JsonObject
        {
            ["timeoutSeconds"] = request["timeoutSeconds"]?.GetValue<int?>() ?? 30,
        };
        if (targetBreakpointId is not null)
            return await WaitForBreakpointAsync(
                targetBreakpointId.Value,
                waitRequest["timeoutSeconds"]!.GetValue<int>(),
                cancellationToken
            );
        return await HandleWaitAsync(waitRequest, cancellationToken);
    }

    private async Task<JsonObject> WaitForBreakpointAsync(
        int breakpointId,
        int timeoutSeconds,
        CancellationToken cancellationToken
    )
    {
        var timer = Stopwatch.StartNew();
        var skippedStops = 0;
        while (true)
        {
            var remaining = TimeSpan.FromSeconds(timeoutSeconds) - timer.Elapsed;
            if (remaining <= TimeSpan.Zero)
                return ResponseError(
                    $"Timed out waiting for breakpoint {breakpointId} after {timeoutSeconds}s; target is still running"
                );

            StopInfo stop;
            try
            {
                stop = await WaitForStopAsync(remaining, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return ResponseError(
                    $"Timed out waiting for breakpoint {breakpointId} after {timeoutSeconds}s; target is still running"
                );
            }

            var reachedTarget =
                stop.Reason == "breakpoint"
                && stop.Breakpoints.Any(bp => bp?["id"]?.GetValue<int>() == breakpointId);
            if (reachedTarget || stop.Reason == "exited")
            {
                stop.Watches = await EvaluateWatchesAsync().ConfigureAwait(false);
                var response = await BuildStopResponseAsync(stop, $"Stopped: {stop.Reason}")
                    .ConfigureAwait(false);
                var data = (JsonObject)response["data"]!;
                data["targetBreakpointId"] = breakpointId;
                data["targetReached"] = reachedTarget;
                data["skippedStops"] = skippedStops;
                return response;
            }

            skippedStops++;
            _selectedFrameId = 0;
            _debugger!.HandleContinueRequest();
        }
    }

    private async Task<JsonObject> HandleUntilAsync(
        JsonObject request,
        string cwd,
        CancellationToken cancellationToken
    )
    {
        if (_debugger is null)
            return ResponseError("No active session");

        string? filePath;
        int line;
        int? column = null;

        var location = request["location"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(location))
        {
            if (_selectedThreadId == 0)
                return ResponseError("No current frame. Use 'until <location>'.");

            var frames = _debugger.GetStackTrace(_selectedThreadId, 0, _selectedFrameIndex + 1);
            if (_selectedFrameIndex >= frames.Count)
                return ResponseError("Current frame not available");

            var frame = frames[_selectedFrameIndex];
            if (string.IsNullOrWhiteSpace(frame.Source))
                return ResponseError("No source information for current frame");

            filePath = frame.Source;
            line = frame.Line;
        }
        else if (location.Contains(':'))
        {
            var (fp, ln, col) = ParseLocation(location, cwd);
            if (fp is null)
                return ResponseError("Invalid location");
            filePath = ResolvePath(fp, cwd);
            line = ln;
            column = col;
        }
        else
        {
            return ResponseError("Invalid location");
        }

        if (!File.Exists(filePath))
            return ResponseError($"Source file not found: {filePath}");

        var fullPath = ResolvePath(filePath, cwd);
        var tempBp = new UserBreakpoint
        {
            Id = _nextBreakpointId++,
            FilePath = fullPath,
            Line = line,
            Column = column,
            Enabled = true,
            IsTemporary = true,
        };

        lock (_breakpointsLock)
        {
            var conflict = CoLocatedSourceRuleErrorLocked(
                tempBp,
                tempBp.Condition,
                tempBp.HitCondition
            );
            if (conflict is not null)
                return ResponseError(conflict);
            _breakpoints.Add(tempBp);
        }

        await RefreshFileBreakpointsAsync(fullPath);

        try
        {
            _resumeSequence = _events.LatestSequence;
            _stopEvent.Reset();
            _selectedFrameId = 0;
            _debugger.HandleContinueRequest();

            while (true)
            {
                var stop = await _stopEvent.WaitAsync(cancellationToken).ConfigureAwait(false);

                // Tracepoints should not satisfy an 'until' request; they merely log and
                // continue, so keep waiting until we hit a real breakpoint or exit.
                if (stop.Reason == "trace")
                {
                    _selectedFrameId = 0;
                    _debugger.HandleContinueRequest();
                    continue;
                }

                stop.Watches = await EvaluateWatchesAsync().ConfigureAwait(false);
                return await BuildStopResponseAsync(stop, $"Stopped: {stop.Reason}");
            }
        }
        finally
        {
            lock (_breakpointsLock)
            {
                _breakpoints.Remove(tempBp);
            }
            await RefreshFileBreakpointsAsync(fullPath);
        }
    }

    private async Task<JsonObject> HandleInterruptAsync(CancellationToken cancellationToken)
    {
        if (_debugger is null)
            return ResponseError("No active session");

        _stopEvent.Reset();
        _debugger.Pause();
        var stop = await WaitForStopAsync(TimeSpan.FromSeconds(5), cancellationToken);
        stop.Watches = await EvaluateWatchesAsync().ConfigureAwait(false);
        return await BuildStopResponseAsync(stop, $"Interrupted: {stop.Reason}");
    }

    private async Task<JsonObject> HandleStepAsync(
        StepKind kind,
        CancellationToken cancellationToken
    )
    {
        if (_debugger is null)
            return ResponseError("No active session");
        if (_selectedThreadId == 0)
            return ResponseError("No stopped thread. Use 'wait' or 'interrupt' first.");

        _resumeSequence = _events.LatestSequence;
        _stopEvent.Reset();

        switch (kind)
        {
            case StepKind.Next:
                await _debugger.StepNext(_selectedThreadId);
                break;
            case StepKind.In:
                await _debugger.StepIn(_selectedThreadId);
                break;
            case StepKind.Out:
                await _debugger.StepOut(_selectedThreadId);
                break;
        }

        var stop = await WaitForStopAsync(TimeSpan.FromSeconds(10), cancellationToken);
        stop.Watches = await EvaluateWatchesAsync().ConfigureAwait(false);
        var response = await BuildStopResponseAsync(stop, $"Stopped: {stop.Reason}");
        if (stop.Reason == "exception" && response["data"] is JsonObject data)
        {
            data["stepInterrupted"] = true;
            var exceptionHint = data["hint"]?.GetValue<string>();
            const string stepHint =
                "The step stopped at an exception. The selected frame is the throw location, so caller locals may be unavailable. Inspect this stop or continue before printing caller locals.";
            data["hint"] = exceptionHint is null ? stepHint : $"{stepHint} {exceptionHint}";
        }
        return response;
    }

    private async Task<JsonObject> HandleBacktraceAsync(JsonObject request)
    {
        if (_debugger is null)
            return ResponseError("No active session");

        var all = request["all"]?.GetValue<bool>() ?? false;
        var threadId = request["threadId"]?.GetValue<int?>();

        var data = new JsonObject();
        var framesArray = new JsonArray();

        if (all)
        {
            var threads = _debugger.GetThreads();
            foreach (var (id, name) in threads)
            {
                var threadFrames = _debugger.GetStackTrace(id, 0, 100);
                framesArray.Add(
                    new JsonObject
                    {
                        ["threadId"] = id,
                        ["name"] = name,
                        ["frames"] = new JsonArray(
                            threadFrames.Select((f, i) => StackFrameToJson(f, i)).ToArray()
                        ),
                    }
                );
            }
        }
        else
        {
            var tid = threadId ?? _selectedThreadId;
            if (tid == 0)
                return ResponseError("No thread selected");

            var threadFrames = _debugger.GetStackTrace(tid, 0, 100);
            for (var i = 0; i < threadFrames.Count; i++)
            {
                framesArray.Add(StackFrameToJson(threadFrames[i], i));
            }

            data["threadId"] = tid;
        }

        data["frames"] = framesArray;
        return ResponseOk("Backtrace", data);
    }

    private async Task<JsonObject> HandleFrameAsync(JsonObject request)
    {
        if (_debugger is null)
            return ResponseError("No active session");
        if (_selectedThreadId == 0)
            return ResponseError("No stopped thread");

        var index = request["index"]?.GetValue<int?>();
        var frames = _debugger.GetStackTrace(_selectedThreadId, 0, 100);

        if (index is null)
        {
            if (_selectedFrameIndex >= frames.Count)
                return ResponseError("Current frame no longer available");
            return ResponseOk(
                "Current frame",
                StackFrameToJson(frames[_selectedFrameIndex], _selectedFrameIndex)
            );
        }

        if (index.Value < 0 || index.Value >= frames.Count)
            return ResponseError($"Frame index {index.Value} out of range");

        _selectedFrameIndex = index.Value;
        _selectedFrameId = frames[index.Value].Id;
        return ResponseOk("Selected frame", StackFrameToJson(frames[index.Value], index.Value));
    }

    private async Task<JsonObject> HandleUpAsync()
    {
        if (_debugger is null)
            return ResponseError("No active session");
        if (_selectedThreadId == 0)
            return ResponseError("No stopped thread");

        var frames = _debugger.GetStackTrace(_selectedThreadId, 0, 100);
        if (_selectedFrameIndex + 1 >= frames.Count)
            return ResponseError("Already at the top of the stack");

        _selectedFrameIndex++;
        _selectedFrameId = frames[_selectedFrameIndex].Id;
        return ResponseOk(
            "Selected frame",
            StackFrameToJson(frames[_selectedFrameIndex], _selectedFrameIndex)
        );
    }

    private async Task<JsonObject> HandleDownAsync()
    {
        if (_debugger is null)
            return ResponseError("No active session");
        if (_selectedThreadId == 0)
            return ResponseError("No stopped thread");

        var frames = _debugger.GetStackTrace(_selectedThreadId, 0, 100);
        if (_selectedFrameIndex - 1 < 0)
            return ResponseError("Already at the bottom of the stack");

        _selectedFrameIndex--;
        _selectedFrameId = frames[_selectedFrameIndex].Id;
        return ResponseOk(
            "Selected frame",
            StackFrameToJson(frames[_selectedFrameIndex], _selectedFrameIndex)
        );
    }

    private async Task<JsonObject> HandleThreadAsync(JsonObject request)
    {
        if (_debugger is null)
            return ResponseError("No active session");

        var threadId = request["threadId"]?.GetValue<int?>() ?? 0;
        if (threadId == 0)
        {
            var threads = _debugger.GetThreads();
            var current = threads.FirstOrDefault(t => t.Item1 == _selectedThreadId);
            return ResponseOk(
                $"Current thread: {_selectedThreadId}",
                new JsonObject
                {
                    ["id"] = _selectedThreadId,
                    ["threadId"] = _selectedThreadId,
                    ["name"] = current.Item2,
                }
            );
        }

        var all = _debugger.GetThreads();
        if (!all.Any(t => t.Item1 == threadId))
            return ResponseError($"Thread {threadId} not found");

        _selectedThreadId = threadId;
        _selectedFrameIndex = 0;

        var frames = _debugger.GetStackTrace(threadId, 0, 1);
        _selectedFrameId = frames.Count > 0 ? frames[0].Id : 0;

        return ResponseOk($"Selected thread {threadId}");
    }

    private async Task<JsonObject> HandleTraceAsync(JsonObject request, string cwd)
    {
        var location = request["location"]?.GetValue<string>() ?? string.Empty;
        var expression = request["expression"]?.GetValue<string>() ?? string.Empty;
        var name = request["name"]?.GetValue<string>();

        var (filePath, line, column) = ParseLocation(location, cwd);
        if (filePath is null || line == 0)
            return ResponseError("Invalid location. Use file:line or file:line:column");

        if (string.IsNullOrWhiteSpace(expression))
            return ResponseError("Missing expression to log");

        var fullPath = ResolvePath(filePath, cwd);
        UserBreakpoint bp;
        lock (_breakpointsLock)
        {
            bp = new UserBreakpoint
            {
                Id = _nextBreakpointId++,
                Name = string.IsNullOrWhiteSpace(name) ? string.Empty : name,
                FilePath = fullPath,
                Line = line,
                Column = column,
                LogExpression = expression,
                Enabled = true,
            };
            var conflict = CoLocatedSourceRuleErrorLocked(bp, bp.Condition, bp.HitCondition);
            if (conflict is not null)
                return ResponseError(conflict);
            _breakpoints.Add(bp);
        }

        if (_debugger is not null)
            await RefreshFileBreakpointsAsync(fullPath);

        return ResponseOk(
            $"Tracepoint {bp.Id}: {location} => {expression}",
            new JsonObject
            {
                ["id"] = bp.Id,
                ["breakpointId"] = bp.Id,
                ["name"] = bp.Name,
            }
        );
    }

    private async Task<JsonObject> HandleListAsync(JsonObject request, string cwd)
    {
        var location = request["location"]?.GetValue<string>();
        var lineCount = request["lineCount"]?.GetValue<int?>() ?? 5;
        if (lineCount is < 1 or > 100)
            return ResponseError("list: lineCount must be between 1 and 100");
        string? filePath;
        int line;

        if (string.IsNullOrWhiteSpace(location))
        {
            if (_selectedThreadId == 0 || _debugger is null)
                return ResponseError("No current frame");

            var frames = _debugger.GetStackTrace(_selectedThreadId, 0, _selectedFrameIndex + 1);
            if (_selectedFrameIndex >= frames.Count)
                return ResponseError("Current frame not available");

            var frame = frames[_selectedFrameIndex];
            if (string.IsNullOrWhiteSpace(frame.Source))
                return ResponseError("No source information for current frame");

            filePath = frame.Source;
            line = frame.Line;
        }
        else if (location.Contains(':'))
        {
            var (fp, ln, _) = ParseLocation(location, cwd);
            if (fp is null)
                return ResponseError("Invalid location");
            filePath = ResolvePath(fp, cwd);
            line = ln;
        }
        else
        {
            return ResponseError("Invalid location");
        }

        if (!File.Exists(filePath))
            return ResponseError($"Source file not found: {filePath}");

        var lines = await File.ReadAllLinesAsync(filePath);
        if (line < 1 || line > lines.Length)
            return ResponseError($"Source line {line} is outside {filePath} (1-{lines.Length})");

        var start = Math.Clamp(
            line - 1 - (lineCount - 1) / 2,
            0,
            Math.Max(0, lines.Length - lineCount)
        );
        var end = Math.Min(lines.Length, start + lineCount);
        var count = end - start;

        var data = new JsonObject
        {
            ["file"] = filePath,
            ["startLine"] = start + 1,
            ["currentLine"] = line,
            ["lines"] = new JsonArray(
                lines.Skip(start).Take(count).Select(l => (JsonNode?)l).ToArray()
            ),
        };

        return ResponseOk("Source", data);
    }

    private async Task<JsonObject> HandleDecompileAsync(JsonObject request)
    {
        var target = request["target"]?.GetValue<string>() ?? string.Empty;
        var ilMode = request["ilMode"]?.GetValue<bool>() ?? false;

        if (_debugger is null)
            return ResponseError("No active session");

        if (string.IsNullOrWhiteSpace(target))
        {
            if (_selectedFrameId == 0)
                return ResponseError("No current frame");

            var source = _debugger.DecompileFrame(_selectedFrameId, ilMode);
            if (source is null)
                return ResponseError("Could not decompile current frame");

            return ResponseOk("Decompiled", new JsonObject { ["source"] = source });
        }

        return ResponseError("Decompiling by name is not yet supported");
    }

    private async Task<JsonObject> HandlePrintAsync(JsonObject request)
    {
        if (_debugger is null)
            return ResponseError("No active session");
        if (_selectedFrameId == 0)
            return ResponseError("No selected frame");

        var expression = request["expression"]?.GetValue<string>() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(expression))
            return ResponseError("Missing expression");

        if (_debugger.IsSyntheticAsyncCallerFrame(_selectedFrameId))
            return ResponseError(
                "Cannot evaluate expressions in a synthetic async caller frame. Use 'info locals' or 'context' for captured values, or break in the real method."
            );
        var evaluation = await EvaluateExpressionAsync(expression, _selectedFrameId);
        var (result, type, variablesReference) = (
            DisplayValue(evaluation),
            evaluation.Type,
            evaluation.VariablesReference
        );

        if (string.IsNullOrEmpty(type))
            return ResponseError(result);

        var data = new JsonObject
        {
            ["value"] = result,
            ["type"] = type,
            ["variablesReference"] = variablesReference,
        };
        if (LooksLikeQuotedExpression(expression))
            data["hint"] =
                "This is a C# string literal. Remove the surrounding double quotes to evaluate the expression.";

        return ResponseOk(string.Empty, data);
    }

    private static bool LooksLikeQuotedExpression(string expression)
    {
        var text = expression.AsSpan().Trim();
        if (text.Length < 3 || text[0] != '"' || text[^1] != '"')
            return false;

        var candidate = text[1..^1];
        if (
            candidate.IndexOfAny("<>!=+*/-&|") >= 0
            && Regex.IsMatch(
                candidate.ToString(),
                @"^\s*[$A-Za-z_][\w.$]*(?:\s*(?:==|!=|<=|>=|&&|\|\||[<>+*/-])\s*[$A-Za-z_0-9][\w.$]*)+\s*$",
                RegexOptions.CultureInvariant
            )
        )
            return true;

        var hasDollar = candidate[0] == '$';
        if (hasDollar)
            candidate = candidate[1..];
        if (!hasDollar && !candidate.Contains('.'))
            return false;

        foreach (var range in candidate.Split('.'))
        {
            var part = candidate[range];
            if (part.IsEmpty || !(char.IsLetter(part[0]) || part[0] == '_'))
                return false;
            foreach (var ch in part[1..])
            {
                if (!(char.IsLetterOrDigit(ch) || ch == '_'))
                    return false;
            }
        }
        return true;
    }

    private Task<JsonObject> HandleWatchAsync(JsonObject request)
    {
        var expression = request["expression"]?.GetValue<string>() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(expression))
            return Task.FromResult(ResponseError("Missing watch expression"));

        var watch = new WatchEntry { Id = _nextWatchId++, Expression = expression };
        _watches.Add(watch);
        return Task.FromResult(
            ResponseOk(
                $"Watch {watch.Id}: {expression}",
                new JsonObject
                {
                    ["id"] = watch.Id,
                    ["watchId"] = watch.Id,
                    ["expression"] = expression,
                }
            )
        );
    }

    private Task<JsonObject> HandleUnwatchAsync(JsonObject request)
    {
        var id = request["watchId"]?.GetValue<int?>() ?? 0;
        var watch = _watches.FirstOrDefault(w => w.Id == id);
        if (watch is null)
            return Task.FromResult(ResponseError($"Watch {id} not found"));

        _watches.Remove(watch);
        return Task.FromResult(ResponseOk($"Removed watch {id}"));
    }

    private Task<JsonObject> HandleWatchesAsync()
    {
        var array = new JsonArray();
        foreach (var w in _watches)
        {
            array.Add(
                new JsonObject
                {
                    ["id"] = w.Id,
                    ["watchId"] = w.Id,
                    ["expression"] = w.Expression,
                    ["lastValue"] = w.LastValue,
                    ["lastType"] = w.LastType,
                }
            );
        }
        return Task.FromResult(
            ResponseOk($"Watches ({_watches.Count})", new JsonObject { ["watches"] = array })
        );
    }

    private async Task<JsonObject?> EvaluateWatchesAsync()
    {
        if (_debugger is null || _selectedFrameId == 0 || _watches.Count == 0)
            return null;

        var result = new JsonObject();
        foreach (var w in _watches)
        {
            try
            {
                var evaluation = await EvaluateExpressionAsync(w.Expression, _selectedFrameId)
                    .ConfigureAwait(false);
                var (value, type) = (DisplayValue(evaluation), evaluation.Type);
                if (string.IsNullOrEmpty(type))
                    continue;

                var changed =
                    !string.Equals(value, w.LastValue, StringComparison.Ordinal)
                    || !string.Equals(type, w.LastType, StringComparison.Ordinal);
                w.LastValue = value;
                w.LastType = type;
                result[w.Id.ToString()] = new JsonObject
                {
                    ["id"] = w.Id,
                    ["watchId"] = w.Id,
                    ["expression"] = w.Expression,
                    ["value"] = value,
                    ["type"] = type,
                    ["changed"] = changed,
                };
            }
            catch (Exception ex)
            {
                Log($"Watch evaluation failed for '{w.Expression}': {ex.Message}");
            }
        }

        return result.Count > 0 ? result : null;
    }

    private Task<JsonObject> HandleCatchAsync(JsonObject request)
    {
        var mode = request["mode"]?.GetValue<string>();
        if (mode is not null)
        {
            if (mode is not ("all" or "user" or "unhandled" or "none"))
                return Task.FromResult(
                    ResponseError("catch: mode must be all, user, unhandled, or none")
                );
            _exceptionBreakMode = mode;
            if (_debugger is not null)
                _debugger.ExceptionStopMode = ToEngineExceptionStopMode(mode);
        }
        return Task.FromResult(
            ResponseOk("Exception stops", new JsonObject { ["mode"] = _exceptionBreakMode })
        );
    }

    private static ManagedExceptionStopMode ToEngineExceptionStopMode(string mode) =>
        mode switch
        {
            "all" => ManagedExceptionStopMode.All,
            "user" => ManagedExceptionStopMode.User,
            "unhandled" => ManagedExceptionStopMode.Unhandled,
            _ => ManagedExceptionStopMode.None,
        };

    private async Task<JsonObject> HandleInfoAsync(JsonObject request, string cwd)
    {
        var subject = request["subject"]?.GetValue<string>() ?? string.Empty;

        return subject switch
        {
            "breakpoints" or "bp" => await HandleBreakpointsAsync(),
            "files" or "targets" => _debugger is null
                ? ResponseOk("Target", new JsonObject { ["file"] = _targetFilePath })
                : HandleInfoFiles(),
            "modules" or "mods" => _debugger is null
                ? ResponseError("No active session")
                : HandleInfoModules(),
            "exception" or "ex" => _debugger is null
                ? ResponseError("No active session")
                : await HandleInfoExceptionAsync(),
            "status" => HandleInfoStatus(),
            "locals" => _debugger is null
                ? ResponseError("No active session")
                : await HandleInfoLocalsAsync(),
            "args" or "arguments" => _debugger is null
                ? ResponseError("No active session")
                : await HandleInfoArgsAsync(),
            "threads" => _debugger is null
                ? ResponseError("No active session")
                : HandleInfoThreads(),
            "trace" or "trace-log" or "trlog" => HandleInfoTrace(request),
            _ => ResponseError(
                (
                    string.IsNullOrWhiteSpace(subject)
                        ? "No info subject provided"
                        : $"Unknown info subject: {subject}"
                )
                    + ". Try: breakpoints, modules, exception, locals, args, threads, files, status, trace"
            ),
        };
    }

    private JsonObject HandleInfoTrace(JsonObject request)
    {
        long? since = null;
        if (request["since"] is { } sinceNode)
        {
            if (!long.TryParse(sinceNode.ToString(), out var value))
                return ResponseError("info trace: --since requires a nonnegative line index");
            since = value;
        }
        var limit = 100;
        if (request["limit"] is { } limitNode && !int.TryParse(limitNode.ToString(), out limit))
            return ResponseError("info trace: --limit must be 1-200");
        return _traceLog.Read(since, limit);
    }

    private JsonObject HandleInfoStatus()
    {
        int processId;
        bool hasProcess;
        bool isRunning;
        lock (_breakpointsLock)
        {
            processId = _debugger?.ProcessId ?? 0;
            hasProcess = _terminalStop is null && (_debugger?.IsProcessAttached ?? false);
            isRunning = _debugger?.IsProcessRunning ?? false;
        }

        var target = _targetFilePath;
        if (string.IsNullOrEmpty(target) && processId > 0)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                target = process.MainModule?.FileName ?? string.Empty;
            }
            catch
            {
                // Leave target empty if the process is no longer accessible.
            }
        }

        var status = new JsonObject
        {
            ["sessionId"] = _sessionId,
            ["target"] = target,
            ["hasProcess"] = hasProcess,
            ["processId"] = processId,
            ["running"] = isRunning,
            ["selectedThread"] = _selectedThreadId,
            ["selectedFrameIndex"] = _selectedFrameIndex,
            ["breakpointCount"] = _breakpoints.Count,
            ["watchCount"] = _watches.Count,
            ["exitCode"] = _terminalStop?.ExitCode,
            ["terminalReason"] = _terminalStop?.Reason,
            ["unhandledException"] = Volatile.Read(ref _unhandledException),
        };

        return ResponseOk("Status", new JsonObject { ["status"] = status });
    }

    private JsonObject HandleEventQuery(JsonObject request, bool outputOnly)
    {
        var command = outputOnly ? "output" : "events";
        long? since = null;
        if (request["since"] is not null)
        {
            if (
                !long.TryParse(request["since"]!.ToString(), out var parsedSince)
                || parsedSince < 0
            )
                return ResponseError($"{command}: since must be nonnegative");
            since = parsedSince;
        }
        var limit = 50;
        if (request["limit"] is not null && !int.TryParse(request["limit"]!.ToString(), out limit))
            return ResponseError($"{command}: limit must be 1-200");
        if (limit is < 1 or > 200)
            return ResponseError($"{command}: limit must be 1-200");
        var channel = request["channel"]?.GetValue<string>();
        if (
            channel is not null
            && (!outputOnly || channel is not ("stdout" or "stderr" or "debug"))
        )
            return ResponseError("output: channel must be stdout, stderr, or debug");

        var kind = outputOnly ? "output" : request["kind"]?.GetValue<string>();
        if (kind is not null && kind is not ("stop" or "trace" or "output" or "diagnostic"))
            return ResponseError("events: kind must be stop, trace, output, or diagnostic");

        return ResponseOk(
            outputOnly ? "Target output" : "Session events",
            _events.Read(since, limit, kind, channel)
        );
    }

    private async Task<JsonObject> HandleContextAsync()
    {
        var status = HandleInfoStatus()["data"]?["status"]?.DeepClone();
        var data = new JsonObject { ["status"] = status };
        if (Volatile.Read(ref _terminalStop) is { Reason: "exited" })
        {
            data["hint"] =
                "Target exited. Use 'output' or 'events' to inspect the run, or 'run' to restart.";
            return ResponseOk("Context", data);
        }
        if (_debugger is null || _selectedThreadId == 0 || _debugger.IsProcessRunning)
            return ResponseOk("Context", data);

        try
        {
            var frames = _debugger.GetStackTrace(_selectedThreadId, 0, _selectedFrameIndex + 1);
            if (_selectedFrameIndex >= frames.Count)
                return ResponseOk("Context", data);

            var frame = frames[_selectedFrameIndex];
            _selectedFrameId = frame.Id;
            data["frame"] = StackFrameToJson(frame, _selectedFrameIndex);
            if (frame.IsSyntheticAsyncCaller)
                data["hint"] =
                    "Synthetic async caller frame: 'info locals' shows captured values; 'print' cannot evaluate expressions here.";
            string? currentStatement = null;

            if (
                !string.IsNullOrWhiteSpace(frame.Source)
                && frame.Line > 0
                && File.Exists(frame.Source)
            )
            {
                var lines = File.ReadAllLines(frame.Source);
                var start = Math.Clamp(frame.Line - 3, 0, Math.Max(0, lines.Length - 5));
                currentStatement = string.Join(
                    ' ',
                    lines
                        .Skip(frame.Line - 1)
                        .Take(Math.Clamp((frame.EndLine ?? frame.Line) - frame.Line + 1, 1, 5))
                );
                data["source"] = new JsonObject
                {
                    ["file"] = frame.Source,
                    ["startLine"] = start + 1,
                    ["currentLine"] = frame.Line,
                    ["lines"] = new JsonArray(
                        lines
                            .Skip(start)
                            .Take(5)
                            .Select(line => (JsonNode?)ClipText(line, 240))
                            .ToArray()
                    ),
                };
            }

            var scope = _debugger
                .GetScopes(_selectedFrameId)
                .FirstOrDefault(item =>
                    item.Name.Equals("Locals", StringComparison.OrdinalIgnoreCase)
                );
            if (scope is not null)
            {
                var variables = await GetVariablesAsync(scope.VariablesReference)
                    .ConfigureAwait(false);
                var arguments = SuppressDuplicateVariables(
                    variables.Where(variable => variable.IsArgument).ToList()
                );
                var locals = SuppressDuplicateVariables(
                    variables.Where(variable => !variable.IsArgument).ToList()
                );
                data["arguments"] = CompactVariables(arguments);
                data["locals"] = CompactVariables(
                    PrioritizeCurrentStatementLocals(locals, currentStatement)
                );
                data["omittedArguments"] = Math.Max(0, arguments.Count - 8);
                data["omittedLocals"] = Math.Max(0, locals.Count - 8);
            }
        }
        catch (Exception ex)
        {
            data["inspectionError"] = ClipText(ex.Message, 240);
        }

        return ResponseOk("Context", data);
    }

    private static List<VariableInfo> SuppressDuplicateVariables(List<VariableInfo> variables)
    {
        // A catch filter and its handler can expose separate IL slots with the same
        // C# name. Keep the populated slot when the other is an inactive null slot,
        // and show identical populated values only once.
        var populated = variables
            .Where(variable => variable.Value != "null")
            .Select(variable => (variable.Name, variable.Type))
            .ToHashSet();
        var seen = new HashSet<(string Name, string? Type, string Value)>();
        return variables
            .Where(variable =>
                (variable.Value != "null" || !populated.Contains((variable.Name, variable.Type)))
                && seen.Add((variable.Name, variable.Type, variable.Value))
            )
            .ToList();
    }

    private static JsonArray CompactVariables(IEnumerable<VariableInfo> variables) =>
        new(
            variables
                .Take(8)
                .Select(variable =>
                    (JsonNode?)
                        new JsonObject
                        {
                            ["name"] = variable.Name,
                            ["type"] = variable.Type,
                            ["value"] = ClipText(DisplayValue(variable), 160),
                            ["variablesReference"] = variable.VariablesReference,
                        }
                )
                .ToArray()
        );

    private static IReadOnlyList<VariableInfo> PrioritizeCurrentStatementLocals(
        IReadOnlyList<VariableInfo> locals,
        string? currentStatement
    )
    {
        if (locals.Count <= 8 || string.IsNullOrWhiteSpace(currentStatement))
            return locals;

        var identifiers = Regex
            .Matches(currentStatement, @"[_\p{L}][_\p{L}\p{Nd}]*")
            .Select(match => match.Value)
            .ToHashSet(StringComparer.Ordinal);
        var selected = new HashSet<VariableInfo>();
        foreach (
            var local in locals.Where(local => identifiers.Contains(local.Name)).Concat(locals)
        )
        {
            if (selected.Count == 8)
                break;
            selected.Add(local);
        }

        return locals.Where(selected.Contains).ToList();
    }

    private static string ClipText(string? value, int limit) =>
        value is null ? string.Empty
        : value.Length > limit ? value[..limit] + "…"
        : value;

    private JsonObject HandleInfoModules()
    {
        var modules = _debugger!.GetModules();
        var array = new JsonArray();
        foreach (var (name, path, baseAddress, isUserCode, hasSymbols) in modules)
        {
            array.Add(
                new JsonObject
                {
                    ["name"] = name,
                    ["path"] = path,
                    ["baseAddress"] = baseAddress,
                    ["isUserCode"] = isUserCode,
                    ["hasSymbols"] = hasSymbols,
                }
            );
        }

        return ResponseOk("Modules", new JsonObject { ["modules"] = array });
    }

    private JsonObject HandleInfoFiles()
    {
        var files = _debugger!.GetSourceFiles();
        var array = new JsonArray();
        foreach (var file in files)
        {
            array.Add(file);
        }

        return ResponseOk(
            $"Files ({files.Count})",
            new JsonObject { ["files"] = array, ["target"] = _targetFilePath }
        );
    }

    private async Task<JsonObject> HandleInfoExceptionAsync()
    {
        if (_selectedThreadId == 0)
            return ResponseError("No selected thread");

        try
        {
            var info = await WithDebuggerLockAsync(debugger =>
                debugger.ExceptionInfo(new ThreadId(_selectedThreadId))
            );
            var data = new JsonObject
            {
                ["id"] = info.ExceptionId,
                ["description"] = info.Description,
                ["breakMode"] = info.BreakMode.ToString(),
                ["code"] = info.Code,
                ["message"] = info.Details.Message,
                ["type"] = info.Details.TypeName,
                ["fullType"] = info.Details.FullTypeName,
                ["hresult"] = info.Details.HResult,
                ["source"] = info.Details.Source,
                ["stackTrace"] = info.Details.StackTrace,
            };

            return ResponseOk("Exception", new JsonObject { ["exception"] = data });
        }
        catch (Exception ex)
        {
            return ResponseError($"Could not get exception info: {ex.Message}");
        }
    }

    private async Task<JsonObject> HandleInfoLocalsAsync()
    {
        if (_selectedFrameId == 0)
            return ResponseError("No selected frame");

        try
        {
            var scopes = _debugger!.GetScopes(_selectedFrameId);
            var localsScope = scopes.FirstOrDefault(s =>
                s.Name.Equals("Locals", StringComparison.OrdinalIgnoreCase)
            );
            if (localsScope is null)
                return ResponseError("No locals scope found");

            var variables = await GetVariablesAsync(localsScope.VariablesReference)
                .ConfigureAwait(false);
            var locals = SuppressDuplicateVariables(variables.Where(v => !v.IsArgument).ToList());
            return ResponseOk($"Locals ({locals.Count})", VariablesToJson(locals));
        }
        catch (Exception ex)
        {
            return ResponseError($"Cannot inspect locals: {ex.Message}");
        }
    }

    private async Task<JsonObject> HandleInfoArgsAsync()
    {
        if (_selectedFrameId == 0)
            return ResponseError("No selected frame");

        try
        {
            var scopes = _debugger!.GetScopes(_selectedFrameId);
            var localsScope = scopes.FirstOrDefault(s =>
                s.Name.Equals("Locals", StringComparison.OrdinalIgnoreCase)
            );
            if (localsScope is null)
                return ResponseError("No locals scope found");

            var variables = await GetVariablesAsync(localsScope.VariablesReference)
                .ConfigureAwait(false);
            var args = SuppressDuplicateVariables(variables.Where(v => v.IsArgument).ToList());
            return ResponseOk($"Arguments ({args.Count})", VariablesToJson(args));
        }
        catch (Exception ex)
        {
            return ResponseError($"Cannot inspect arguments: {ex.Message}");
        }
    }

    private JsonObject HandleInfoThreads()
    {
        var threads = _debugger!.GetThreads();
        var array = new JsonArray();
        foreach (var (id, name) in threads)
        {
            var selected = id == _selectedThreadId;
            array.Add(
                new JsonObject
                {
                    ["id"] = id,
                    ["name"] = name,
                    ["selected"] = selected,
                }
            );
        }
        return ResponseOk($"Threads ({threads.Count})", new JsonObject { ["threads"] = array });
    }

    private async Task<VariableInfo> EvaluateExpressionAsync(string expression, int frameId) =>
        await WithDebuggerLockAsync(debugger => debugger.Evaluate(expression, frameId))
            .ConfigureAwait(false);

    private async Task<List<VariableInfo>> GetVariablesAsync(int variablesReference) =>
        await WithDebuggerLockAsync(debugger => debugger.GetVariables(variablesReference))
            .ConfigureAwait(false);

    private async Task<T> WithDebuggerLockAsync<T>(Func<ManagedDebugger, Task<T>> action)
    {
        var debugger = _debugger ?? throw new InvalidOperationException("No active debugger");
        using (await debugger.DapRequestAndRuntimeEventLock.LockAsync().ConfigureAwait(false))
        {
            await debugger.DrainRuntimeEventQueue().ConfigureAwait(false);
            return await action(debugger).ConfigureAwait(false);
        }
    }

    private static string DisplayValue(VariableInfo variable)
    {
        var value = variable.Value ?? string.Empty;
        if (
            variable.Type is not ("string" or "System.String")
            || value.Length < 2
            || value[0] != '"'
            || value[^1] != '"'
        )
            return value;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<string>(value) ?? value;
        }
        catch (System.Text.Json.JsonException)
        {
            return value;
        }
    }

    private static JsonObject VariablesToJson(List<VariableInfo> variables)
    {
        var array = new JsonArray();
        foreach (var v in variables)
        {
            array.Add(
                new JsonObject
                {
                    ["name"] = v.Name,
                    ["value"] = DisplayValue(v),
                    ["type"] = v.Type,
                    ["variablesReference"] = v.VariablesReference,
                }
            );
        }
        return new JsonObject { ["variables"] = array };
    }

    private async Task<JsonObject> HandleWaitAsync(
        JsonObject request,
        CancellationToken cancellationToken
    )
    {
        if (_debugger is null)
            return ResponseError("No active session");

        var timeoutSeconds = request["timeoutSeconds"]?.GetValue<int?>();
        if (timeoutSeconds is < 1 or > 3600)
            return ResponseError("wait: timeoutSeconds must be between 1 and 3600");

        if (Volatile.Read(ref _terminalStop) is { } terminal)
            return await BuildStopResponseAsync(terminal, $"Stopped: {terminal.Reason}");

        StopInfo stop;
        try
        {
            stop = timeoutSeconds.HasValue
                ? await WaitForStopAsync(
                    TimeSpan.FromSeconds(timeoutSeconds.Value),
                    cancellationToken
                )
                : await _stopEvent.WaitAsync(cancellationToken);
        }
        catch (TimeoutException)
        {
            return ResponseError($"Timed out waiting for the debuggee after {timeoutSeconds}s");
        }

        if (stop.Reason == "trace")
        {
            if (stop.TraceOutput.Count == 0 && stop.TraceExpressions.Count > 0)
                await EvaluateTraceExpressionsAsync(stop).ConfigureAwait(false);
            stop.Watches = await EvaluateWatchesAsync().ConfigureAwait(false);

            // The frame is about to become stale; clear the selection so
            // commands like info locals don't return stale data while running.
            _selectedFrameId = 0;

            // Continue without resetting the event, so a pending regular stop
            // at the same location is still captured.
            try
            {
                _debugger.HandleContinueRequest();
            }
            catch (Exception ex)
            {
                Log($"Failed to continue after trace: {ex.Message}");
            }

            return ResponseOk("Trace", stop.ToJson());
        }

        if (stop.TraceOutput.Count == 0 && stop.TraceExpressions.Count > 0)
        {
            await EvaluateTraceExpressionsAsync(stop).ConfigureAwait(false);
        }

        stop.Watches = await EvaluateWatchesAsync().ConfigureAwait(false);
        return await BuildStopResponseAsync(stop, $"Stopped: {stop.Reason}");
    }

    private async Task<JsonObject> BuildStopResponseAsync(StopInfo stop, string message)
    {
        var data = stop.ToJson();
        if (stop.Reason == "exception" && !stop.UnhandledException)
            data["hint"] =
                "First-chance exception; it may be handled or rethrown. Use 'info exception' to inspect it. 'catch user' skips framework async rethrows; 'catch unhandled' skips all first-chance stops.";
        if (stop.Reason is not ("exited" or "trace"))
        {
            var contextResponse = await HandleContextAsync();
            if (contextResponse["data"] is JsonObject context)
            {
                var compact = (JsonObject)context.DeepClone();
                compact.Remove("status");
                if (compact.Count > 0)
                    data["context"] = compact;
            }
        }

        var output = _events.Read(_resumeSequence, 1024, "output", null);
        var outputEvents = output["events"]!.AsArray();
        var recent = new JsonArray();
        foreach (var entry in outputEvents.TakeLast(4))
        {
            recent.Add(
                new JsonObject
                {
                    ["seq"] = entry!["seq"]?.GetValue<long>(),
                    ["channel"] = entry["channel"]?.GetValue<string>(),
                    ["text"] = ClipText(
                        entry["text"]?.GetValue<string>()?.TrimEnd('\r', '\n'),
                        160
                    ),
                }
            );
        }
        data["recentOutput"] = recent;
        data["omittedOutput"] = Math.Max(0, outputEvents.Count - recent.Count);
        data["outputNextSeq"] = output["nextSeq"]?.GetValue<long>() ?? _events.LatestSequence;

        var diagnosticEvents = _events.Read(_resumeSequence, 1024, "diagnostic", null)[
            "events"
        ]!.AsArray();
        if (diagnosticEvents.Count > 0)
        {
            var diagnostics = new JsonArray();
            foreach (var entry in diagnosticEvents.TakeLast(4))
            {
                diagnostics.Add(
                    new JsonObject
                    {
                        ["seq"] = entry!["seq"]?.GetValue<long>(),
                        ["text"] = ClipText(entry["text"]?.GetValue<string>(), 240),
                    }
                );
            }
            data["recentDiagnostics"] = diagnostics;
            data["omittedDiagnostics"] = Math.Max(0, diagnosticEvents.Count - diagnostics.Count);
        }
        return ResponseOk(message, data);
    }

    private async Task EvaluateTraceExpressionsAsync(StopInfo stop)
    {
        if (_debugger is null || _selectedFrameId == 0)
            return;

        foreach (var expr in stop.TraceExpressions)
        {
            try
            {
                var evaluation = await EvaluateExpressionAsync(expr, _selectedFrameId)
                    .ConfigureAwait(false);
                var (value, type) = (DisplayValue(evaluation), evaluation.Type);
                var typeStr = string.IsNullOrEmpty(type) ? "" : $"({type}) ";
                stop.TraceOutput.Add($"{expr} = {typeStr}{value}");
            }
            catch (Exception ex)
            {
                stop.TraceOutput.Add($"{expr} = <error: {ex.Message}>");
            }
        }
    }

    private Task<JsonObject> HandleSourceAsync(
        JsonObject request,
        string cwd,
        CancellationToken cancellationToken
    ) =>
        SourceScriptRunner.RunAsync(
            request,
            cwd,
            cancellationToken,
            HandleOperationAsync,
            path => ResolvePath(path, cwd)
        );

    private Task<JsonObject> HandleQuitAsync()
    {
        if (_debugger is not null)
        {
            try
            {
                _debugger.Disconnect(terminateDebuggee: Volatile.Read(ref _terminalStop) is null);
            }
            catch (Exception ex)
            {
                Log($"Quit failed: {ex.Message}");
                return Task.FromResult(
                    ResponseError($"Quit failed; target state is unknown: {ex.Message}")
                );
            }
            _debugger = null;
        }

        _shutdownCts.Cancel();
        return Task.FromResult(ResponseOk("Goodbye"));
    }

    private Task<JsonObject> HandleKillAsync()
    {
        if (_debugger is null)
            return Task.FromResult(ResponseError("No active session"));

        try
        {
            _debugger.Disconnect(terminateDebuggee: Volatile.Read(ref _terminalStop) is null);
        }
        catch (Exception ex)
        {
            Log($"Failed to kill target: {ex.Message}");
            return Task.FromResult(
                ResponseError($"Kill failed; target state is unknown: {ex.Message}")
            );
        }

        _debugger = null;
        _selectedThreadId = 0;
        _selectedFrameIndex = 0;
        _selectedFrameId = 0;
        Volatile.Write(ref _terminalStop, null);
        Volatile.Write(ref _unhandledException, false);
        _stopEvent.Reset();

        return Task.FromResult(ResponseOk("Target killed"));
    }

    private Task<JsonObject> HandleBreakpointsAsync()
    {
        var array = new JsonArray();
        lock (_breakpointsLock)
        {
            foreach (var bp in _breakpoints.Where(b => !b.IsTemporary))
            {
                var obj = new JsonObject
                {
                    ["id"] = bp.Id,
                    ["breakpointId"] = bp.Id,
                    ["name"] = bp.Name,
                    ["condition"] = bp.Condition,
                    ["hitCondition"] = bp.HitCondition,
                    ["log"] = bp.LogExpression,
                    ["enabled"] = bp.Enabled,
                    ["verified"] = bp.Verified,
                    ["isIl"] = bp.IsIlBreakpoint,
                };

                if (bp.IsIlBreakpoint)
                {
                    obj["method"] = bp.IlMethod;
                    obj["ilOffset"] = bp.IlOffset;
                    obj["module"] = bp.IlModule;
                }
                else
                {
                    obj["file"] = bp.FilePath;
                    obj["line"] = bp.Line;
                    obj["column"] = bp.Column;
                    obj["boundLine"] = bp.BoundLine;
                    obj["boundColumn"] = bp.BoundColumn;
                    if (!string.IsNullOrWhiteSpace(bp.VerifiedMessage))
                        obj["verificationMessage"] = bp.VerifiedMessage;
                }

                array.Add(obj);
            }
        }

        return Task.FromResult(
            ResponseOk("Breakpoints", new JsonObject { ["breakpoints"] = array })
        );
    }

    private async Task ResetDebuggerAsync(CancellationToken cancellationToken)
    {
        if (_debugger is not null)
        {
            try
            {
                _debugger.Disconnect(terminateDebuggee: Volatile.Read(ref _terminalStop) is null);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to terminate previous target; session state is unknown: {ex.Message}",
                    ex
                );
            }
            _debugger = null;
        }

        _debugger = new ManagedDebugger(_logger);
        _debugger.ExceptionStopMode = ToEngineExceptionStopMode(_exceptionBreakMode);
        SubscribeToDebuggerEvents(_debugger);
        _stopEvent.Reset();
        Volatile.Write(ref _terminalStop, null);
        Volatile.Write(ref _unhandledException, false);
        _selectedThreadId = 0;
        _selectedFrameIndex = 0;
        _selectedFrameId = 0;

        await RefreshAllBreakpointsAsync();
    }

    private async Task RefreshAllBreakpointsAsync()
    {
        List<string> files;
        List<string> methodKeys;
        lock (_breakpointsLock)
        {
            files = _breakpoints
                .Where(b => !b.IsIlBreakpoint && !string.IsNullOrWhiteSpace(b.FilePath))
                .Select(b => b.FilePath)
                .Distinct(SourcePathComparer.Instance)
                .ToList();
            methodKeys = _breakpoints
                .Where(b => b.IsIlBreakpoint && !string.IsNullOrWhiteSpace(b.IlMethod))
                .Select(b => IlBreakpointLocation.BuildMethodKey(b.IlModule, b.IlMethod!))
                .Distinct()
                .ToList();
        }

        foreach (var file in files)
        {
            await RefreshFileBreakpointsAsync(file).ConfigureAwait(false);
        }

        foreach (var methodKey in methodKeys)
        {
            var (moduleName, methodName) = IlBreakpointLocation.SplitMethodKey(methodKey);
            await RefreshIlBreakpointsAsync(moduleName, methodName).ConfigureAwait(false);
        }
    }

    private void SubscribeToDebuggerEvents(ManagedDebugger debugger)
    {
        debugger.OnStopped += async (threadId, reason) =>
            await OnStopped(threadId, reason, null, 0, 0);
        debugger.OnStopped2 += async (threadId, filePath, line, column, reason, breakpointIds) =>
            await OnStopped(
                threadId,
                reason,
                filePath,
                line,
                column,
                breakpointIds?.FirstOrDefault() ?? 0
            );
        debugger.OnUnhandledException += () =>
        {
            if (ReferenceEquals(_debugger, debugger))
                Volatile.Write(ref _unhandledException, true);
        };
        debugger.OnExited += exitCode =>
        {
            if (!ReferenceEquals(_debugger, debugger))
                return;
            var unhandledException = Volatile.Read(ref _unhandledException);
            var stop = new StopInfo
            {
                Reason = "exited",
                ExitCode = exitCode,
                UnhandledException = unhandledException,
            };
            Volatile.Write(ref _terminalStop, stop);
            _selectedThreadId = 0;
            _selectedFrameIndex = 0;
            _selectedFrameId = 0;
            _events.AddStop("exited", null, 0, 0, exitCode, unhandledException);
            _stopEvent.Set(stop);
        };
        debugger.OnOutput += (text, isError) =>
            _events.AddOutput(isError ? "stderr" : "stdout", text.TrimEnd('\r', '\n'));
        debugger.OnDebugOutput += text => _events.AddOutput("debug", text);
        debugger.OnConditionEvaluationError += (condition, error) =>
        {
            if (ReferenceEquals(_debugger, debugger))
                _events.AddDiagnostic($"Condition '{condition}' failed: {error}");
        };
        debugger.OnBreakpointChanged += bp =>
        {
            lock (_breakpointsLock)
            {
                var userBp = _breakpoints.FirstOrDefault(b => b.InternalId == bp.Id);
                if (userBp is not null)
                {
                    userBp.Verified = bp.Verified;
                    userBp.VerifiedMessage = bp.Message;
                    if (!userBp.IsIlBreakpoint)
                    {
                        userBp.BoundLine = bp.Verified ? bp.Line : null;
                        userBp.BoundColumn = bp.Verified ? bp.Column : null;
                    }
                }
            }
        };
    }

    private async Task OnStopped(
        int threadId,
        string reason,
        string? filePath,
        int line,
        int column,
        int breakpointId = 0
    )
    {
        _selectedThreadId = threadId;
        _selectedFrameIndex = 0;
        _selectedFrameId = 0;

        if (_debugger is not null)
        {
            try
            {
                var frames = _debugger.GetStackTrace(threadId, 0, 1);
                if (frames.Count > 0)
                {
                    _selectedFrameId = frames[0].Id;
                    if (string.IsNullOrWhiteSpace(filePath))
                    {
                        filePath = frames[0].Source;
                        line = frames[0].Line;
                        column = frames[0].Column;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to resolve frame for stop: {ex.Message}");
            }
        }

        var stop = new StopInfo
        {
            ThreadId = threadId,
            Reason = reason,
            UnhandledException = reason == "exception" && Volatile.Read(ref _unhandledException),
            FilePath = filePath,
            Line = line,
            Column = column,
            FrameId = _selectedFrameId,
            Index = 0,
            Description = $"Stopped in thread {threadId}: {reason}",
        };
        if (!string.IsNullOrWhiteSpace(filePath))
            stop.Description += $" at {filePath}:{line}";

        // Check for tracepoints. If the location only contains tracepoints, log and
        // continue; otherwise treat as a regular stop and attach any trace logs to it.
        if (reason == "breakpoint")
        {
            List<string> traceExpressions = new();
            bool allTracepoints;
            lock (_breakpointsLock)
            {
                var matching = _breakpoints
                    .Where(b =>
                        b.Enabled
                        && (
                            breakpointId > 0
                                ? b.InternalId == breakpointId
                                : !string.IsNullOrWhiteSpace(filePath)
                                    && string.Equals(
                                        b.FilePath,
                                        filePath,
                                        SourcePathComparer.Comparison
                                    )
                                    && (b.BoundLine ?? b.Line) == line
                        )
                    )
                    .ToList();
                foreach (var bp in matching.Where(b => !b.IsTemporary))
                {
                    var item = new JsonObject
                    {
                        ["id"] = bp.Id,
                        ["name"] = bp.Name,
                        ["isTracepoint"] = !string.IsNullOrWhiteSpace(bp.LogExpression),
                    };
                    if (bp.IsIlBreakpoint)
                    {
                        item["method"] = bp.IlMethod;
                        item["ilOffset"] = bp.IlOffset;
                        item["module"] = bp.IlModule;
                    }
                    else
                    {
                        item["file"] = bp.FilePath;
                        item["line"] = bp.Line;
                        item["column"] = bp.Column;
                    }
                    stop.Breakpoints.Add(item);
                }
                traceExpressions = matching
                    .Where(b => !string.IsNullOrWhiteSpace(b.LogExpression))
                    .Select(b => b.LogExpression!)
                    .ToList();
                allTracepoints =
                    matching.Count > 0
                    && matching.All(b => !string.IsNullOrWhiteSpace(b.LogExpression));
            }

            if (allTracepoints && traceExpressions.Count > 0)
            {
                stop.Reason = "trace";
                stop.TraceExpressions = traceExpressions;
                stop.Description = $"Trace in thread {threadId} at {filePath}:{line}";
            }
            else if (traceExpressions.Count > 0)
            {
                // Regular breakpoint triggered, but tracepoints also exist at this location.
                stop.TraceExpressions = traceExpressions;
            }
        }

        // Evaluate any trace expressions so the values are available both for the
        // response and for the persistent trace log, regardless of whether the stop
        // is a pure trace or a regular breakpoint with co-located tracepoints.
        if (stop.TraceExpressions.Count > 0)
        {
            await EvaluateTraceExpressionsAsync(stop).ConfigureAwait(false);
            if (stop.TraceOutput.Count > 0)
            {
                var traceLogError = _traceLog.Append(
                    stop.TraceOutput.Select(n =>
                        n?.GetValue<string>() ?? n?.ToString() ?? string.Empty
                    )
                );
                if (traceLogError is not null)
                    _events.AddDiagnostic(traceLogError);
            }
        }

        if (stop.Reason != "trace")
            _events.AddStop(
                stop.Reason,
                stop.FilePath,
                stop.Line,
                stop.ThreadId,
                unhandledException: stop.UnhandledException,
                breakpoints: stop.Reason == "breakpoint" ? stop.Breakpoints : null
            );
        foreach (var trace in stop.TraceOutput)
        {
            _events.AddTrace(
                trace?.GetValue<string>() ?? trace?.ToString() ?? string.Empty,
                stop.FilePath,
                stop.Line,
                stop.ThreadId
            );
        }

        if (stop.Reason == "trace")
        {
            // If a waiter is active, let HandleWaitAsync return the trace output.
            if (_stopEvent.TrySet(stop))
                return;

            // No waiter pending: auto-continue so tracepoints do not block execution.
            _selectedFrameId = 0;
            try
            {
                _debugger?.HandleContinueRequest();
            }
            catch (Exception ex)
            {
                Log($"Failed to continue after trace: {ex.Message}");
            }
            return;
        }

        _stopEvent.Set(stop);
    }

    private async Task<StopInfo> WaitForStopAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            return await _stopEvent.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cts.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out waiting for the debuggee to stop");
        }
    }

    private async Task RefreshFileBreakpointsAsync(string filePath)
    {
        if (_debugger is null)
            return;

        List<UserBreakpoint> active;
        lock (_breakpointsLock)
        {
            active = _breakpoints
                .Where(b =>
                    string.Equals(b.FilePath, filePath, SourcePathComparer.Comparison) && b.Enabled
                )
                .OrderBy(b => b.Id)
                .ToList();
        }

        // Combine multiple user breakpoints at the same source location so the
        // engine creates a single CorBreakpoint per location. This prevents
        // duplicate stops and keeps trace/break behavior low variance.
        var groups = active
            .GroupBy(b => (b.Line, b.Column))
            .Select(g => new { Location = g.Key, Breakpoints = g.ToList() })
            .ToList();

        var requests = new List<SharpDbgBreakpointRequest>();
        foreach (var group in groups)
        {
            var bp = group.Breakpoints[0];
            string? condition = null;
            string? hitCondition = null;
            if (group.Breakpoints.Count == 1)
            {
                condition = bp.Condition;
                hitCondition = bp.HitCondition;
            }
            else if (
                group.Breakpoints.Any(b =>
                    !string.IsNullOrWhiteSpace(b.Condition)
                    || !string.IsNullOrWhiteSpace(b.HitCondition)
                )
            )
                throw new InvalidOperationException(
                    "Co-located source breakpoints with conditions or ignore counts are unsupported"
                );

            requests.Add(
                new SharpDbgBreakpointRequest(bp.Line, condition, hitCondition, bp.Column)
            );
        }

        var result = _debugger.SetBreakpoints(filePath, requests.ToArray());

        for (var i = 0; i < groups.Count; i++)
        {
            if (i < result.Count)
            {
                foreach (var bp in groups[i].Breakpoints)
                {
                    bp.InternalId = result[i].Id;
                    bp.Verified = result[i].Verified;
                    bp.VerifiedMessage = result[i].Message;
                    bp.BoundLine = result[i].Verified ? result[i].Line : null;
                    bp.BoundColumn = result[i].Verified ? result[i].Column : null;
                }
            }
        }
    }

    private async Task RefreshIlBreakpointsAsync(string? moduleName, string method)
    {
        if (_debugger is null)
            return;

        List<UserBreakpoint> active;
        lock (_breakpointsLock)
        {
            active = _breakpoints
                .Where(b =>
                    b.IsIlBreakpoint
                    && b.Enabled
                    && string.Equals(b.IlMethod, method, StringComparison.Ordinal)
                    && string.Equals(
                        b.IlModule ?? string.Empty,
                        moduleName ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .OrderBy(b => b.Id)
                .ToList();
        }

        var requests = active
            .Select(bp => new SharpDbgIlBreakpointRequest(
                bp.IlMethod!,
                bp.IlOffset!.Value,
                bp.IlModule,
                bp.Condition,
                bp.HitCondition
            ))
            .ToArray();

        var methodKey = IlBreakpointLocation.BuildMethodKey(moduleName, method);
        var result = _debugger.SetIlBreakpoints(methodKey, requests);

        for (var i = 0; i < active.Count; i++)
        {
            if (i < result.Count)
            {
                var bp = active[i];
                bp.InternalId = result[i].Id;
                bp.Verified = result[i].Verified;
                bp.VerifiedMessage = result[i].Message;
            }
        }
    }

    // Call while holding _breakpointsLock. A shared engine breakpoint cannot honor
    // separate conditions or hit counts for user breakpoints at the same location.
    private string? CoLocatedSourceRuleErrorLocked(
        UserBreakpoint candidate,
        string? condition,
        string? hitCondition,
        bool? enabled = null
    )
    {
        if (candidate.IsIlBreakpoint || !(enabled ?? candidate.Enabled))
            return null;

        var peers = _breakpoints
            .Where(bp =>
                bp.Id != candidate.Id
                && !bp.IsIlBreakpoint
                && bp.Enabled
                && string.Equals(bp.FilePath, candidate.FilePath, SourcePathComparer.Comparison)
                && bp.Line == candidate.Line
                && bp.Column == candidate.Column
            )
            .ToList();
        if (peers.Count == 0)
            return null;

        var hasRules =
            !string.IsNullOrWhiteSpace(condition)
            || !string.IsNullOrWhiteSpace(hitCondition)
            || peers.Any(bp =>
                !string.IsNullOrWhiteSpace(bp.Condition)
                || !string.IsNullOrWhiteSpace(bp.HitCondition)
            );
        return hasRules
            ? $"Co-located source breakpoints at {candidate.FilePath}:{candidate.Line} cannot combine conditions or ignore counts; use one breakpoint or disable a peer"
            : null;
    }

    private (string? filePath, int line, int? column) ParseLocation(string location, string cwd)
    {
        return LocationParser.TryParse(location, out var filePath, out var line, out var column)
            ? (filePath, line, column)
            : (null, 0, null);
    }

    private string ResolvePath(string path, string cwd)
    {
        if (Path.IsPathFullyQualified(path))
            return path;

        var basePath = string.IsNullOrWhiteSpace(cwd) ? _workingDirectory : cwd;
        if (string.IsNullOrWhiteSpace(basePath))
            basePath = Environment.CurrentDirectory;

        return Path.GetFullPath(Path.Combine(basePath, path));
    }

    private static JsonObject StackFrameToJson(StackFrameInfo frame, int index) =>
        new()
        {
            ["id"] = frame.Id,
            ["frameId"] = frame.Id,
            ["index"] = index,
            ["name"] = frame.Name,
            ["line"] = frame.Line,
            ["column"] = frame.Column,
            ["endLine"] = frame.EndLine,
            ["endColumn"] = frame.EndColumn,
            ["source"] = frame.Source,
            ["syntheticAsyncCaller"] = frame.IsSyntheticAsyncCaller,
        };

    private static bool TryGetBreakpointNumber(JsonObject request, out int id, out JsonObject error)
    {
        id = 0;
        error = ResponseError("");
        var number = request["breakpointId"]?.GetValue<int?>();
        if (!number.HasValue)
        {
            error = ResponseError("Missing breakpoint number");
            return false;
        }

        id = number.Value;
        return true;
    }

    public void Dispose()
    {
        _shutdownCts.Cancel();
        _shutdownCts.Dispose();
        if (_debugger is not null)
        {
            try
            {
                _debugger.Disconnect(terminateDebuggee: true);
            }
            catch
            { /* ignore */
            }
        }
        _traceLog.Dispose();
    }

    private enum StepKind
    {
        Next,
        In,
        Out,
    }
}
