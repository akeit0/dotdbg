using AsyncDispatch.Cli;
using AsyncDispatch.Domain;

namespace AsyncDispatch.Processing;

internal sealed class DispatchCoordinator(RetryPolicy retryPolicy)
{
    public Task<DispatchResult[]> RunAsync(
        IReadOnlyList<DispatchJob> jobs,
        RunOptions options,
        CancellationToken cancellationToken = default
    )
    {
        var attemptLimit = options.RetryCount + 1;
        var pending = jobs.Select(job =>
            retryPolicy.SendAsync(job, attemptLimit, cancellationToken)
        );
        return Task.WhenAll(pending);
    }
}
