using System.IO.Pipes;
using System.Text.Json.Nodes;

namespace DotDbg.Ipc;

public static class PipeClient
{
    public static async Task<JsonObject> SendAsync(
        string sessionId,
        JsonObject request,
        int timeoutMs = 5000,
        CancellationToken cancellationToken = default
    )
    {
        var pipeName = PipeServer.ComputePipeName(sessionId);
        await using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous
        );

        if (timeoutMs <= 0)
        {
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await client.ConnectAsync(timeoutMs, cancellationToken).ConfigureAwait(false);
        }

        await OperationProtocol
            .WriteMessageAsync(client, request, cancellationToken)
            .ConfigureAwait(false);
        return await OperationProtocol
            .ReadMessageAsync(client, cancellationToken)
            .ConfigureAwait(false);
    }
}
