using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotDbg.Ipc;

public static class OperationProtocol
{
    public const int MaxMessageBytes = 10 * 1024 * 1024;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static byte[] Serialize(JsonObject value)
    {
        var json = value.ToJsonString(JsonOptions);
        var payload = Encoding.UTF8.GetBytes(json);
        if (payload.Length > MaxMessageBytes)
            throw new InvalidDataException(
                $"Message is {payload.Length} bytes; maximum is {MaxMessageBytes}"
            );
        var message = new byte[4 + payload.Length];
        BitConverter.GetBytes(payload.Length).CopyTo(message, 0);
        payload.CopyTo(message, 4);
        return message;
    }

    public static JsonObject Deserialize(byte[] payload)
    {
        var json = Encoding.UTF8.GetString(payload);
        return JsonSerializer.Deserialize<JsonObject>(json, JsonOptions) ?? new JsonObject();
    }

    public static async Task<JsonObject> ReadMessageAsync(
        Stream stream,
        CancellationToken cancellationToken
    )
    {
        var lengthBuffer = new byte[4];
        await ReadExactlyAsync(stream, lengthBuffer, cancellationToken).ConfigureAwait(false);
        var length = BitConverter.ToInt32(lengthBuffer, 0);
        if (length < 0 || length > MaxMessageBytes)
            throw new InvalidDataException($"Invalid message length: {length}");

        var payload = new byte[length];
        await ReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false);
        return Deserialize(payload);
    }

    public static async Task WriteMessageAsync(
        Stream stream,
        JsonObject value,
        CancellationToken cancellationToken
    )
    {
        var message = Serialize(value);
        await stream.WriteAsync(message, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReadExactlyAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken
    )
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream
                .ReadAsync(buffer.AsMemory(total), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                throw new IOException("Connection closed while reading message");
            total += read;
        }
    }
}
