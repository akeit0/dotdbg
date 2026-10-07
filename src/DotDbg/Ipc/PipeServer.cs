using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using DotDbg.Engine;

namespace DotDbg.Ipc;

public sealed class PipeServer : IDisposable
{
    private readonly DebugSession _session;
    private readonly string _pipeName;
    private readonly List<Task> _clientTasks = new();
    private readonly object _clientTasksLock = new();
    private Task? _listenerTask;

    public PipeServer(DebugSession session, string sessionId)
    {
        _session = session;
        _pipeName = ComputePipeName(sessionId);
    }

    public string PipeName => _pipeName;

    public Task StartAsync()
    {
        _listenerTask = ListenLoopAsync();
        return Task.CompletedTask;
    }

    public async Task WaitForShutdownAsync()
    {
        if (_listenerTask is not null)
            await _listenerTask.ConfigureAwait(false);

        Task[] remaining;
        lock (_clientTasksLock)
        {
            remaining = _clientTasks.ToArray();
        }

        await Task.WhenAll(remaining).ConfigureAwait(false);
    }

    private async Task ListenLoopAsync()
    {
        while (!_session.ShutdownToken.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous
                );

                await server.WaitForConnectionAsync(_session.ShutdownToken).ConfigureAwait(false);

                // Handle each client on its own pipe instance so a blocking
                // command (e.g. 'wait') does not prevent other clients from
                // connecting (e.g. 'quit').
                var clientTask = HandleClientAsync(server);
                lock (_clientTasksLock)
                {
                    _clientTasks.Add(clientTask);
                }

                _ = clientTask.ContinueWith(
                    t =>
                    {
                        lock (_clientTasksLock)
                        {
                            _clientTasks.Remove(t);
                        }
                    },
                    TaskContinuationOptions.ExecuteSynchronously
                );
            }
            catch (OperationCanceledException)
            {
                server?.Dispose();
                break;
            }
            catch (ArgumentException ex)
            {
                // The pipe name and its socket path do not change between iterations,
                // so a rejected name never becomes valid. Retrying would spin for the
                // life of the process and leave the daemon running with no listener.
                server?.Dispose();
                _session.Log($"Named pipe server cannot listen on '{_pipeName}': {ex.Message}");
                _session.Shutdown();
                break;
            }
            catch (Exception ex)
            {
                server?.Dispose();
                _session.Log($"Named pipe server error: {ex}");
                try
                {
                    await Task.Delay(500, _session.ShutdownToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream server)
    {
        await using var _ = server;
        while (server.IsConnected && !_session.ShutdownToken.IsCancellationRequested)
        {
            JsonObject request;
            try
            {
                request = await OperationProtocol
                    .ReadMessageAsync(server, _session.ShutdownToken)
                    .ConfigureAwait(false);
            }
            catch (IOException)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var response = await _session
                .HandleOperationAsync(request, _session.ShutdownToken)
                .ConfigureAwait(false);

            // Write the response without tying it to the shutdown token so the client can read it.
            try
            {
                await OperationProtocol
                    .WriteMessageAsync(server, response, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (InvalidDataException ex)
            {
                await OperationProtocol
                    .WriteMessageAsync(
                        server,
                        OperationResponse.ResponseError(
                            $"Response exceeded IPC limit: {ex.Message}"
                        ),
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
            }

            if (
                request["op"]?.GetValue<string>() is "quit"
                && response["success"]?.GetValue<bool>() == true
            )
            {
                _session.Shutdown();
                break;
            }
        }
    }

    public void Dispose()
    {
        // No-op: shutdown is handled by the session.
    }

    // On Unix a named pipe is backed by a Unix domain socket created at
    // Path.GetTempPath() + SocketPrefix + pipe name, and sun_path is far shorter
    // than a file path may be: 104 bytes on macOS and 108 on Linux, including the
    // terminator. macOS compounds this by placing the temp directory under a
    // per-user /var/folders path of about 49 characters, leaving room for only
    // ~37 characters of session id where Linux allows ~85.
    private const string SocketPrefix = "CoreFxPipe_";

    private static int MaxSocketPathBytes => OperatingSystem.IsMacOS() ? 103 : 107;

    /// <summary>
    /// Maps a session id onto a pipe name whose socket path fits the platform limit.
    /// The client and the daemon derive the name from the same session id, so a
    /// hashed name still resolves to the same pipe on both sides.
    /// </summary>
    public static string ComputePipeName(string sessionId)
    {
        var safe = string.Join("_", sessionId.Split(Path.GetInvalidFileNameChars()));
        var name = $"dotdbg_{safe}";

        // Windows pipes live under \\.\pipe\ and are not subject to the socket limit.
        if (OperatingSystem.IsWindows())
            return name;

        var overhead = Encoding.UTF8.GetByteCount(Path.GetTempPath() + SocketPrefix);
        if (overhead + Encoding.UTF8.GetByteCount(name) <= MaxSocketPathBytes)
            return name;

        // Hash the session id rather than truncating it: truncation would collide
        // across sessions sharing a prefix, which is common for generated ids.
        // 64 bits matches the width of the default working-directory session id.
        var hash = Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionId)))[..16]
            .ToLowerInvariant();
        var hashed = $"dotdbg_{hash}";
        if (overhead + Encoding.UTF8.GetByteCount(hashed) <= MaxSocketPathBytes)
            return hashed;

        throw new InvalidOperationException(
            $"The temporary directory is too long to hold a debugger IPC socket: "
                + $"'{Path.GetTempPath()}' needs "
                + $"{overhead + Encoding.UTF8.GetByteCount(hashed)} bytes of the "
                + $"{MaxSocketPathBytes} available. Point TMPDIR at a shorter directory."
        );
    }
}
