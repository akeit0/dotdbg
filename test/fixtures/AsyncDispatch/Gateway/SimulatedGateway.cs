using System.Collections.Concurrent;
using System.Diagnostics;
using AsyncDispatch.Domain;

namespace AsyncDispatch.Gateway;

internal sealed class SimulatedGateway(string? failOnceJobId)
{
    private readonly ConcurrentDictionary<string, int> _calls = new(StringComparer.Ordinal);

    public async Task<int> SendAsync(DispatchJob job, CancellationToken cancellationToken = default)
    {
        await Task.Delay(job.GatewayDelayMs, cancellationToken);
        var call = _calls.AddOrUpdate(job.Id, 1, (_, previous) => previous + 1);
        Debug.WriteLine($"gateway {job.Id}: call={call}");

        if (string.Equals(job.Id, failOnceJobId, StringComparison.Ordinal) && call == 1)
            throw new TransientGatewayException(job.Id);

        return call;
    }
}
