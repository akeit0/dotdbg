using System.IO.Pipes;
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

    public static string ComputePipeName(string sessionId)
    {
        var safe = string.Join("_", sessionId.Split(Path.GetInvalidFileNameChars()));
        return $"dotdbg_{safe}";
    }
}
