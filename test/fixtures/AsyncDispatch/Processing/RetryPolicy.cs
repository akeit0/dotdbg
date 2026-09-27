using System.Diagnostics;
using AsyncDispatch.Domain;
using AsyncDispatch.Gateway;

namespace AsyncDispatch.Processing;

internal sealed class RetryPolicy(SimulatedGateway gateway)
{
    public async Task<DispatchResult> SendAsync(
        DispatchJob job,
        int attemptLimit,
        CancellationToken cancellationToken = default
    )
    {
        for (var attempt = 1; attempt <= attemptLimit; attempt++)
        {
            try
            {
                var gatewayCalls = await gateway.SendAsync(job, cancellationToken);
                return new DispatchResult(job, true, gatewayCalls, null);
            }
            catch (TransientGatewayException error) when (attempt < attemptLimit)
            {
                Debug.WriteLine($"retry {job.Id}: {error.Code} after attempt {attempt}");
                await Task.Delay(5, cancellationToken);
            }
            catch (TransientGatewayException error)
            {
                return new DispatchResult(job, false, attempt, error.Code);
            }
        }

        throw new InvalidOperationException("The retry policy had no initial attempt");
    }
}
